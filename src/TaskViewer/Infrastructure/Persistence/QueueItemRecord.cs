namespace TaskViewer.Infrastructure.Persistence;

public sealed class QueueItemRecord
{
    public int Id { get; init; }
    public string? TaskKey { get; init; }
    public string? TaskUnit { get; init; }
    public string IssueKey { get; init; } = "";
    public int IssueCount { get; init; } = 1;
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
    public string? LockKey { get; init; }
    public int? Line { get; init; }
    public string? IssueStatus { get; init; }
    public string? Instructions { get; init; }
    public string State { get; init; } = "queued";
    public int PriorityScore { get; init; }
    public int AttemptCount { get; init; }
    public int MaxAttempts { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset? LeaseHeartbeatAt { get; init; }
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public string? SessionId { get; init; }
    public string? OpenCodeUrl { get; init; }
    public string? LastError { get; init; }
    public string? LastReviewAction { get; init; }
    public string? LastReviewReason { get; init; }
    public DateTimeOffset? LastReviewedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? DispatchedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
}
