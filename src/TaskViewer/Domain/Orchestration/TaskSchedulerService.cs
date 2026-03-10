using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

sealed class TaskSchedulerService : ITaskSchedulerService
{
    readonly Func<DateTimeOffset> _nowUtc;
    readonly IQueueRepository _queueRepository;

    public TaskSchedulerService(IQueueRepository queueRepository, Func<DateTimeOffset>? nowUtc = null)
    {
        _queueRepository = queueRepository;
        _nowUtc = nowUtc ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<QueueItemRecord?> LeaseNextTaskAsync(string leaseOwner, int globalMaxActive, int perProjectMaxActive, int leaseSeconds)
    {
        if (globalMaxActive <= 0)
            return null;

        var now = _nowUtc();
        var active = await _queueRepository.ListQueue([QueueState.Leased, QueueState.Running], 5000);

        if (active.Count >= globalMaxActive)
            return null;

        var ready = await _queueRepository.ListQueue([QueueState.Queued], 5000);

        if (ready.Count == 0)
            return null;

        var activeByProject = active
            .GroupBy(GetProjectKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var readyByProject = ready
            .GroupBy(GetProjectKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var activeLocks = new HashSet<string>(
            active.Select(task => NormalizeLockKey(task.LockKey)).Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);

        var candidate = ready
            .Where(task => task.NextAttemptAt is null || task.NextAttemptAt <= now)
            .Where(task => GetProjectCount(activeByProject, task) < perProjectMaxActive)
            .Where(task => !HasConflictingLock(activeLocks, task))
            .OrderByDescending(task => ComputeEffectiveScore(task, readyByProject, now))
            .ThenBy(task => task.CreatedAt)
            .ThenBy(task => task.Id)
            .FirstOrDefault();

        if (candidate is null)
            return null;

        var heartbeatAt = now;
        var expiresAt = now.AddSeconds(Math.Max(30, leaseSeconds));
        return await _queueRepository.TryLeaseTask(candidate.Id, leaseOwner, heartbeatAt, expiresAt);
    }

    static int ComputeEffectiveScore(QueueItemRecord task, IReadOnlyDictionary<string, int> readyByProject, DateTimeOffset now)
    {
        var ageMinutes = Math.Max(0, (int)(now - task.CreatedAt).TotalMinutes);
        var ageBonus = Math.Min(30, ageMinutes / 5);
        var cheapFixBonus = task.IssueCount <= 3 ? 8 : 0;
        var noisyProjectPenalty = Math.Max(0, GetProjectCount(readyByProject, task) - 1) * 2;

        return task.PriorityScore + ageBonus + cheapFixBonus - noisyProjectPenalty;
    }

    static bool HasConflictingLock(HashSet<string> activeLocks, QueueItemRecord task)
    {
        var lockKey = NormalizeLockKey(task.LockKey);
        return !string.IsNullOrWhiteSpace(lockKey) && activeLocks.Contains(lockKey);
    }

    static string GetProjectKey(QueueItemRecord task)
    {
        return $"{task.SonarProjectKey}::{task.Branch ?? string.Empty}";
    }

    static int GetProjectCount(IReadOnlyDictionary<string, int> counts, QueueItemRecord task)
    {
        return counts.TryGetValue(GetProjectKey(task), out var count)
            ? count
            : 0;
    }

    static string NormalizeLockKey(string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}
