using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface IQueueDispatchService
{
    Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item, IReadOnlyList<NormalizedIssue> issues);
}
