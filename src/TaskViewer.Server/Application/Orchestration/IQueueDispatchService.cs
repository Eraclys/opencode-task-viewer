namespace TaskViewer.Server.Application.Orchestration;

public interface IQueueDispatchService
{
    Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item);
}
