namespace TaskViewer.Application.Orchestration;

public interface IQueueDispatchService
{
    Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item, IReadOnlyList<NormalizedIssue> issues);
}
