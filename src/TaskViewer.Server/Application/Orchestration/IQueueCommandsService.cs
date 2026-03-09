namespace TaskViewer.Server.Application.Orchestration;

interface IQueueCommandsService
{
    Task<bool> CancelQueueItemAsync(int? queueId);
    Task<int> RetryFailedAsync();
    Task<int> ClearQueuedAsync();
    Task<bool> ApproveTaskAsync(int? taskId);
    Task<bool> RejectTaskAsync(int? taskId, string? reason);
    Task<bool> RequeueTaskAsync(int? taskId, string? reason);
    Task<bool> RepromptTaskAsync(int? taskId, string? instructions, string? reason);
}
