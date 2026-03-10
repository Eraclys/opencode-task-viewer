namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record QueueDispatchResult(
    string SessionId,
    string? OpenCodeUrl);
