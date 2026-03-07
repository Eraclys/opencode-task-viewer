using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

sealed class QueueCommandsService : IQueueCommandsService
{
    readonly Func<DateTimeOffset> _nowUtc;
    readonly IQueueRepository _queueRepository;

    public QueueCommandsService(IQueueRepository queueRepository, Func<DateTimeOffset>? nowUtc = null)
    {
        _queueRepository = queueRepository;
        _nowUtc = nowUtc ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<bool> CancelQueueItemAsync(int? queueId)
    {
        var id = queueId.GetValueOrDefault(-1);

        if (id <= 0)
            throw new InvalidOperationException("Invalid queue id");

        return await _queueRepository.CancelQueueItem(id, _nowUtc());
    }

    public Task<int> RetryFailedAsync() => _queueRepository.RetryFailed(_nowUtc());

    public Task<int> ClearQueuedAsync() => _queueRepository.ClearQueued(_nowUtc());

}
