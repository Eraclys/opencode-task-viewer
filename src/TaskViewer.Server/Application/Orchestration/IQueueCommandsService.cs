namespace TaskViewer.Server.Application.Orchestration;

interface IQueueCommandsService
{
    Task<bool> CancelQueueItemAsync(int? queueId);
    Task<int> RetryFailedAsync();
    Task<int> ClearQueuedAsync();
}
