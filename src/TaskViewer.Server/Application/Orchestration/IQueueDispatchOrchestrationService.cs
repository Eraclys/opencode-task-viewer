namespace TaskViewer.Server.Application.Orchestration;

interface IQueueDispatchOrchestrationService
{
    Task DispatchAndPersistAsync(QueueItemRecord item);
}
