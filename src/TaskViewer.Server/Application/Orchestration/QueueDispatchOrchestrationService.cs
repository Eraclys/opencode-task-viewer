using TaskViewer.Server;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

internal sealed class QueueDispatchOrchestrationService : IQueueDispatchOrchestrationService
{
    private readonly IQueueRepository _queueRepository;
    private readonly IQueueDispatchService _queueDispatchService;
    private readonly IDispatchFailurePolicy _dispatchFailurePolicy;
    private readonly Func<string> _nowIso;

    public QueueDispatchOrchestrationService(
        IQueueRepository queueRepository,
        IQueueDispatchService queueDispatchService,
        IDispatchFailurePolicy dispatchFailurePolicy,
        Func<string>? nowIso = null)
    {
        _queueRepository = queueRepository;
        _queueDispatchService = queueDispatchService;
        _dispatchFailurePolicy = dispatchFailurePolicy;
        _nowIso = nowIso ?? (() => DateTimeOffset.UtcNow.ToString("O"));
    }

    public async Task DispatchAndPersistAsync(QueueItemRecord item)
    {
        try
        {
            var dispatch = await _queueDispatchService.DispatchAsync(item);
            await _queueRepository.MarkSessionCreated(item.Id, dispatch.SessionId, dispatch.OpenCodeUrl, _nowIso());
        }
        catch (Exception ex)
        {
            var (attemptCount, maxAttempts) = await _queueRepository.GetAttemptInfo(item.Id, item.AttemptCount, item.MaxAttempts);
            var decision = _dispatchFailurePolicy.Decide(attemptCount, maxAttempts, DateTimeOffset.UtcNow);
            await _queueRepository.MarkDispatchFailure(item.Id, decision.State, decision.NextAttemptAt, ex.Message, _nowIso());
        }
    }
}
