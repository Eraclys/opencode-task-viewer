namespace TaskViewer.Server.Application.Orchestration;

public interface IOrchestratorRuntime : IAsyncDisposable
{
    Task RunOnceAsync(Func<Task> tickBody);
    void Start(int pollMs, CancellationToken stoppingToken, Func<Task> tick);
}
