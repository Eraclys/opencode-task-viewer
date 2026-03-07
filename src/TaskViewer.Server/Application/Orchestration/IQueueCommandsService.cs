using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

internal interface IQueueCommandsService
{
    Task<bool> CancelQueueItemAsync(object? queueId);
    Task<int> RetryFailedAsync();
    Task<int> ClearQueuedAsync();
}

internal sealed class QueueCommandsService : IQueueCommandsService
{
    private readonly IQueueRepository _queueRepository;
    private readonly Func<string> _nowIso;

    public QueueCommandsService(IQueueRepository queueRepository, Func<string>? nowIso = null)
    {
        _queueRepository = queueRepository;
        _nowIso = nowIso ?? (() => DateTimeOffset.UtcNow.ToString("O"));
    }

    public async Task<bool> CancelQueueItemAsync(object? queueId)
    {
        var id = ParseIntSafe(queueId, -1);

        if (id <= 0)
            throw new InvalidOperationException("Invalid queue id");

        return await _queueRepository.CancelQueueItem(id, _nowIso());
    }

    public Task<int> RetryFailedAsync()
    {
        return _queueRepository.RetryFailed(_nowIso());
    }

    public Task<int> ClearQueuedAsync()
    {
        return _queueRepository.ClearQueued(_nowIso());
    }

    private static int ParseIntSafe(object? value, int fallback)
    {
        if (value is null)
            return fallback;

        if (value is int i)
            return i;

        if (value is long l && l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }
}
