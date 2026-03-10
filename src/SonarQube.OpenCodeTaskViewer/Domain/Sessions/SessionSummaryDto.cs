using System.Text.Json.Serialization;
using OpenCode.Client;
using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Domain.Sessions;

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

    [JsonIgnore] public required ViewerTaskStatus Status { get; init; }

    [JsonPropertyName("status")] public string StatusValue => Status.Value;

    public bool? HasAssistantResponse { get; init; }
    public string? OpenCodeUrl { get; init; }

    public bool? IsQueueItem { get; init; }
    public long? QueueItemId { get; init; }

    [JsonIgnore] public QueueState? QueueState { get; init; }

    [JsonPropertyName("queueState")] public string? QueueStateValue => QueueState?.Value;

    public long? TaskId { get; init; }

    [JsonIgnore] public QueueState? TaskState { get; init; }

    [JsonPropertyName("taskState")] public string? TaskStateValue => TaskState?.Value;

    public string? TaskKey { get; init; }
    public string? TaskUnit { get; init; }
    public string? TaskInstructions { get; init; }
    public int? TaskIssueCount { get; init; }
    public string? IssueKey { get; init; }

    [JsonIgnore] public SonarIssueType IssueType { get; init; }

    [JsonPropertyName("issueType")] public string? IssueTypeValue => IssueType.OrNull();

    [JsonIgnore] public SonarIssueSeverity IssueSeverity { get; init; }

    [JsonPropertyName("issueSeverity")] public string? IssueSeverityValue => IssueSeverity.OrNull();

    public string? IssueRule { get; init; }
    public string? IssuePath { get; init; }
    public int? IssueLine { get; init; }
    public string? LastError { get; init; }

    [JsonIgnore] public TaskReviewAction LastReviewAction { get; init; }

    [JsonPropertyName("lastReviewAction")] public string? LastReviewActionValue => LastReviewAction.OrNull();

    public string? LastReviewReason { get; init; }
    public DateTimeOffset? LastReviewedAt { get; init; }
}
