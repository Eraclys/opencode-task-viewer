namespace TaskViewer.Application.Sessions;

public sealed class SessionSummaryDto
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Project { get; init; }
    public string? Description { get; init; }
    public string? GitBranch { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
    public required SessionRuntimeStatus RuntimeStatus { get; init; }
    public required string Status { get; init; }
    public bool? HasAssistantResponse { get; init; }
    public string? OpenCodeUrl { get; init; }

    public bool? IsQueueItem { get; init; }
    public long? QueueItemId { get; init; }
    public string? QueueState { get; init; }
    public long? TaskId { get; init; }
    public string? TaskState { get; init; }
    public string? TaskKey { get; init; }
    public string? TaskUnit { get; init; }
    public string? TaskInstructions { get; init; }
    public int? TaskIssueCount { get; init; }
    public string? IssueKey { get; init; }
    public string? IssueType { get; init; }
    public string? IssueSeverity { get; init; }
    public string? IssueRule { get; init; }
    public string? IssuePath { get; init; }
    public int? IssueLine { get; init; }
    public string? LastError { get; init; }
    public string? LastReviewAction { get; init; }
    public string? LastReviewReason { get; init; }
    public DateTimeOffset? LastReviewedAt { get; init; }
}
