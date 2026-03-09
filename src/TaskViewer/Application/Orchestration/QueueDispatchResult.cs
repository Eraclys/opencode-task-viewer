namespace TaskViewer.Application.Orchestration;

public sealed record QueueDispatchResult(
    string SessionId,
    string? OpenCodeUrl);
