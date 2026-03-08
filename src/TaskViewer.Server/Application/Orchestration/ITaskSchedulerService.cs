namespace TaskViewer.Server.Application.Orchestration;

interface ITaskSchedulerService
{
    Task<QueueItemRecord?> LeaseNextTaskAsync(string leaseOwner, int globalMaxActive, int perProjectMaxActive, int leaseSeconds);
}
