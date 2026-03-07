namespace TaskViewer.Server;

public sealed class QueueItemRecord
{
    public int Id { get; init; }
    public string IssueKey { get; init; } = "";
    public int MappingId { get; init; }
    public string SonarProjectKey { get; init; } = "";
    public string Directory { get; init; } = "";
    public string? Branch { get; init; }
    public string? IssueType { get; init; }
    public string? Severity { get; init; }
    public string? Rule { get; init; }
    public string? Message { get; init; }
    public string? Component { get; init; }
    public string? RelativePath { get; init; }
    public string? AbsolutePath { get; init; }
    public int? Line { get; init; }
    public string? IssueStatus { get; init; }
    public string? Instructions { get; init; }
    public string State { get; init; } = "queued";
    public int AttemptCount { get; init; }
    public int MaxAttempts { get; init; }
    public string? NextAttemptAt { get; init; }
    public string? SessionId { get; init; }
    public string? OpenCodeUrl { get; init; }
    public string? LastError { get; init; }
    public string CreatedAt { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
    public string? DispatchedAt { get; init; }
    public string? CompletedAt { get; init; }
    public string? CancelledAt { get; init; }
}
