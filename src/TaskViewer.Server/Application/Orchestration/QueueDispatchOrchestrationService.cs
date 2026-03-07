using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

sealed class QueueDispatchOrchestrationService : IQueueDispatchOrchestrationService
{
    readonly IDispatchFailurePolicy _dispatchFailurePolicy;
    readonly Func<DateTimeOffset> _nowUtc;
    readonly IQueueDispatchService _queueDispatchService;
    readonly IQueueRepository _queueRepository;

    public QueueDispatchOrchestrationService(
        IQueueRepository queueRepository,
        IQueueDispatchService queueDispatchService,
        IDispatchFailurePolicy dispatchFailurePolicy,
        Func<DateTimeOffset>? nowUtc = null)
    {
        _queueRepository = queueRepository;
        _queueDispatchService = queueDispatchService;
        _dispatchFailurePolicy = dispatchFailurePolicy;
        _nowUtc = nowUtc ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task DispatchAndPersistAsync(QueueItemRecord item)
    {
        try
        {
            var dispatch = await _queueDispatchService.DispatchAsync(item);

            await _queueRepository.MarkSessionCreated(
                item.Id,
                dispatch.SessionId,
                dispatch.OpenCodeUrl,
                _nowUtc());
        }
        catch (Exception ex)
        {
            var (attemptCount, maxAttempts) = await _queueRepository.GetAttemptInfo(item.Id, item.AttemptCount, item.MaxAttempts);
            var utcNow = _nowUtc();
            var decision = _dispatchFailurePolicy.Decide(attemptCount, maxAttempts, utcNow);

            await _queueRepository.MarkDispatchFailure(
                item.Id,
                decision.State,
                decision.NextAttemptAt,
                ex.Message,
                utcNow);
        }
    }
}
