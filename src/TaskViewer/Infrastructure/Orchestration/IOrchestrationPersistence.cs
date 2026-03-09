using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Infrastructure.Orchestration;

public interface IOrchestrationPersistence : IAsyncDisposable
{
    IQueueRepository QueueRepository { get; }
    IMappingRepository MappingRepository { get; }
    Task ResetStateAsync();
}
