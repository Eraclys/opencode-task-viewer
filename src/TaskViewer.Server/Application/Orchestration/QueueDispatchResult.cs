namespace TaskViewer.Server.Application.Orchestration;

public sealed record QueueDispatchResult(
    string SessionId,
    string? OpenCodeUrl);
