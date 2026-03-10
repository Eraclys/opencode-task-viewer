namespace SonarQube.OpenCodeTaskViewer.Domain.Sessions;

public sealed record LastAssistantMessageResult(
    bool Found,
    string SessionId,
    string? Message,
    DateTimeOffset? CreatedAt);
