using TaskViewer.Domain.Orchestration;
using TaskViewer.Domain.Sessions;
using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class QueueItemSessionSummaryMapper
{
    public SessionSummaryDto? Map(QueueItemRecord item)
    {
        var taskState = item.QueueState;

        if (!QueueState.SessionVisibleStates.Contains(taskState))
            return null;

        var titleParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.IssueKey))
            titleParts.Add(item.IssueKey);

        if (!string.IsNullOrWhiteSpace(item.Rule))
            titleParts.Add(item.Rule);

        if (!string.IsNullOrWhiteSpace(item.Message))
            titleParts.Add(item.Message);

        var taskLabel = taskState.DisplayLabel;
        var boardStatus = taskState.BoardStatus;
        var issueType = item.ParsedIssueType.Or(item.IssueType);
        var issueSeverity = item.ParsedSeverity.Or(item.Severity);
        var lastReviewAction = item.ParsedLastReviewAction.Or(item.LastReviewAction);

        var name = titleParts.Count > 0
            ? $"[{taskLabel}] {string.Join(" - ", titleParts)}"
            : $"[{taskLabel}] Item #{item.Id}";

        return new SessionSummaryDto
        {
            Id = $"queue-{item.Id}",
            Name = name,
            Project = item.Directory,
            Description = item.Message,
            GitBranch = null,
            CreatedAt = item.CreatedAt,
            ModifiedAt = item.UpdatedAt,
            RuntimeStatus = SessionRuntimeStatus.FromRaw(taskState.Value),
            Status = boardStatus,
            HasAssistantResponse = item.SessionId is { Length: > 0 },
            OpenCodeUrl = item.OpenCodeUrl,
            IsQueueItem = true,
            QueueItemId = item.Id,
            QueueState = taskState.Value,
            TaskId = item.Id,
            TaskState = taskState.Value,
            TaskKey = item.TaskKey,
            TaskUnit = item.TaskUnit,
            TaskInstructions = item.Instructions,
            TaskIssueCount = item.IssueCount,
            IssueKey = string.IsNullOrWhiteSpace(item.IssueKey) ? null : item.IssueKey,
            IssueType = issueType,
            IssueSeverity = issueSeverity,
            IssueRule = item.Rule,
            IssuePath = item.RelativePath ?? item.AbsolutePath,
            IssueLine = item.Line,
            LastError = item.LastError,
            LastReviewAction = lastReviewAction,
            LastReviewReason = item.LastReviewReason,
            LastReviewedAt = item.LastReviewedAt
        };
    }
}
