using TaskViewer.Server;

namespace TaskViewer.Server.Application.Orchestration;

internal interface IQueueDispatchOrchestrationService
{
    Task DispatchAndPersistAsync(QueueItemRecord item);
}
