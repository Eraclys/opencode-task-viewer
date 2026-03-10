using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface IQueueWorkerCoordinator
{
    Task ScheduleAsync(
        HashSet<string> inFlight,
        int maxActive,
        Func<Task<QueueItemRecord?>> claimNext,
        Func<QueueItemRecord, Task> dispatch,
        Action onChange);
}
