namespace TaskViewer.Server.Application.Sessions;

public sealed class SessionSummaryDto
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Project { get; init; }
    public string? Description { get; init; }
    public string? GitBranch { get; init; }
    public string? CreatedAt { get; init; }
    public required string ModifiedAt { get; init; }
    public required SessionRuntimeStatus RuntimeStatus { get; init; }
    public required string Status { get; init; }
    public bool? HasAssistantResponse { get; init; }
    public string? OpenCodeUrl { get; init; }

    public bool? IsQueueItem { get; init; }
    public long? QueueItemId { get; init; }
    public string? QueueState { get; init; }
    public string? IssueKey { get; init; }
    public string? IssueType { get; init; }
    public string? IssueSeverity { get; init; }
    public string? IssueRule { get; init; }
    public string? IssuePath { get; init; }
    public int? IssueLine { get; init; }
    public string? LastError { get; init; }
}

public sealed record OpenCodeSessionDto(
    string Id,
    string? Name,
    string? Directory,
    string? Project,
    string? CreatedAt,
    string? UpdatedAt);
