namespace TaskViewer.Application.Orchestration;

public interface ITaskSchedulerService
{
    Task<QueueItemRecord?> LeaseNextTaskAsync(string leaseOwner, int globalMaxActive, int perProjectMaxActive, int leaseSeconds);
}
