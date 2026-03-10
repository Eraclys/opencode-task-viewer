using TaskViewer.Domain.Sessions;
using TaskViewer.Infrastructure.Persistence;
using TaskViewer.OpenCode;

namespace TaskViewer.Domain.Orchestration;

sealed class TaskReconcilerService : ITaskReconcilerService
{
    readonly IDispatchFailurePolicy _dispatchFailurePolicy;
    readonly Func<DateTimeOffset> _nowUtc;
    readonly IOpenCodeService _openCodeService;
    readonly IQueueRepository _queueRepository;

    public TaskReconcilerService(
        IQueueRepository queueRepository,
        IOpenCodeService openCodeService,
        IDispatchFailurePolicy dispatchFailurePolicy,
        Func<DateTimeOffset>? nowUtc = null)
    {
        _queueRepository = queueRepository;
        _openCodeService = openCodeService;
        _dispatchFailurePolicy = dispatchFailurePolicy;
        _nowUtc = nowUtc ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task ReconcileAsync(int leaseSeconds)
    {
        var now = _nowUtc();
        var activeTasks = await _queueRepository.ListQueue([QueueState.Leased, QueueState.Running], 5000);

        if (activeTasks.Count == 0)
            return;

        var statusMaps = new Dictionary<string, Dictionary<string, SessionRuntimeStatus>>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in activeTasks.OrderBy(item => item.Id))
        {
            if (task.QueueState == QueueState.Leased)
            {
                if (task.LeaseExpiresAt is not null && task.LeaseExpiresAt <= now)
                    await RecoverTaskAsync(task, "Task lease expired before runner startup.", now);

                continue;
            }

            if (task.QueueState != QueueState.Running)
                continue;

            var statusMap = await GetStatusMapAsync(statusMaps, task.Directory);
            var runtimeStatus = string.IsNullOrWhiteSpace(task.SessionId) || !statusMap.TryGetValue(task.SessionId, out var status)
                ? SessionRuntimeStatus.Idle
                : status;

            if (runtimeStatus.IsRunning)
            {
                if (!string.IsNullOrWhiteSpace(task.LeaseOwner))
                {
                    await _queueRepository.HeartbeatTask(
                        task.Id,
                        task.LeaseOwner,
                        now,
                        now.AddSeconds(Math.Max(30, leaseSeconds)));
                }

                continue;
            }

            await _queueRepository.MarkTaskAwaitingReview(task.Id, now);
        }
    }

    async Task RecoverTaskAsync(QueueItemRecord task, string lastError, DateTimeOffset now)
    {
        var (attemptCount, maxAttempts) = await _queueRepository.GetAttemptInfo(task.Id, task.AttemptCount, task.MaxAttempts);
        var decision = _dispatchFailurePolicy.Decide(attemptCount, maxAttempts, now);

        await _queueRepository.MarkDispatchFailure(
            task.Id,
            decision.State,
            decision.NextAttemptAt,
            lastError,
            now);
    }

    async Task<Dictionary<string, SessionRuntimeStatus>> GetStatusMapAsync(
        IDictionary<string, Dictionary<string, SessionRuntimeStatus>> cache,
        string directory)
    {
        if (cache.TryGetValue(directory, out var cached))
            return cached;

        try
        {
            var statusMap = await _openCodeService.ReadWorkingStatusMapAsync(directory);
            cache[directory] = statusMap;
            return statusMap;
        }
        catch
        {
            var empty = new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);
            cache[directory] = empty;
            return empty;
        }
    }
}
