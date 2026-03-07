namespace TaskViewer.Server.Infrastructure.Orchestration;

sealed record QueueStats(
    int Queued,
    int Dispatching,
    int SessionCreated,
    int Done,
    int Failed,
    int Cancelled);
