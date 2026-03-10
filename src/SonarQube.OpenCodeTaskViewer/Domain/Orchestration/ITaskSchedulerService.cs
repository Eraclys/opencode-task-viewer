using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface ITaskSchedulerService
{
    Task<QueueItemRecord?> LeaseNextTaskAsync(
        string leaseOwner,
        int globalMaxActive,
        int perProjectMaxActive,
        int leaseSeconds);
}
