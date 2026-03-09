using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public interface IQueueDispatchService
{
    Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item, IReadOnlyList<NormalizedIssue> issues);
}
