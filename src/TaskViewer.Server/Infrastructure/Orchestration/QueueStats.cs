namespace TaskViewer.Server.Infrastructure.Orchestration;

sealed record QueueStats(
    int Queued,
    int Dispatching,
    int SessionCreated,
    int Done,
    int Failed,
    int Cancelled,
    int Leased = 0,
    int Running = 0,
    int AwaitingReview = 0,
    int Rejected = 0);
