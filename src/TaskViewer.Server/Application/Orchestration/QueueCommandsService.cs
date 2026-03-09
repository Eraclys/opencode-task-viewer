using TaskViewer.OpenCode;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

sealed class QueueCommandsService : IQueueCommandsService
{
    readonly Func<string, string?, Task<DateTimeOffset?>> _archiveSessionOnOpenCode;
    readonly Func<DateTimeOffset> _nowUtc;
    readonly IQueueRepository _queueRepository;

    public QueueCommandsService(
        IQueueRepository queueRepository,
        Func<string, string?, Task<DateTimeOffset?>>? archiveSessionOnOpenCode = null,
        Func<DateTimeOffset>? nowUtc = null)
    {
        _queueRepository = queueRepository;
        _archiveSessionOnOpenCode = archiveSessionOnOpenCode ?? ((_, _) => Task.FromResult<DateTimeOffset?>(null));
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

    public async Task<bool> ApproveTaskAsync(int? taskId)
    {
        var id = taskId.GetValueOrDefault(-1);

        if (id <= 0)
            throw new InvalidOperationException("Invalid task id");

        var task = (await _queueRepository.ListQueue(["awaiting_review"], 5000)).FirstOrDefault(item => item.Id == id);

        if (task is null)
            return false;

        if (!string.IsNullOrWhiteSpace(task.SessionId))
            await _archiveSessionOnOpenCode(task.SessionId, task.Directory);

        return await _queueRepository.ApproveTask(id, _nowUtc());
    }

    public async Task<bool> RejectTaskAsync(int? taskId, string? reason)
    {
        var id = taskId.GetValueOrDefault(-1);

        if (id <= 0)
            throw new InvalidOperationException("Invalid task id");

        return await _queueRepository.RejectTask(id, reason, _nowUtc());
    }

    public async Task<bool> RequeueTaskAsync(int? taskId, string? reason)
    {
        var id = taskId.GetValueOrDefault(-1);

        if (id <= 0)
            throw new InvalidOperationException("Invalid task id");

        return await _queueRepository.RequeueTask(id, reason, _nowUtc());
    }

    public async Task<bool> RepromptTaskAsync(int? taskId, string? instructions, string? reason)
    {
        var id = taskId.GetValueOrDefault(-1);

        if (id <= 0)
            throw new InvalidOperationException("Invalid task id");

        var updatedInstructions = instructions?.Trim();

        if (string.IsNullOrWhiteSpace(updatedInstructions))
            throw new InvalidOperationException("Missing instructions");

        return await _queueRepository.RepromptTask(id, updatedInstructions, reason, _nowUtc());
    }

}
