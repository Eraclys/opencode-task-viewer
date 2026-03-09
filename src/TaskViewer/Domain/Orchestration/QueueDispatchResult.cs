namespace TaskViewer.Domain.Orchestration;

public sealed record QueueDispatchResult(
    string SessionId,
    string? OpenCodeUrl);
