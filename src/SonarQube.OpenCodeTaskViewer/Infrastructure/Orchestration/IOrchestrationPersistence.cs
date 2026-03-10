using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;

public interface IOrchestrationPersistence : IAsyncDisposable
{
    IQueueRepository QueueRepository { get; }
    IMappingRepository MappingRepository { get; }
    Task ResetStateAsync();
}
