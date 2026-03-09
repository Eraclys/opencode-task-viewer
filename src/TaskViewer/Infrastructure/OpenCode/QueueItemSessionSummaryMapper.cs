using TaskViewer.Application.Sessions;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class QueueItemSessionSummaryMapper
{
    public SessionSummaryDto? Map(QueueItemRecord item)
    {
        var taskState = (item.State ?? string.Empty).Trim().ToLowerInvariant();

        if (taskState is not ("queued" or "dispatching" or "leased" or "running" or "awaiting_review" or "rejected" or "failed" or "cancelled"))
            return null;

        var titleParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.IssueKey))
            titleParts.Add(item.IssueKey);

        if (!string.IsNullOrWhiteSpace(item.Rule))
            titleParts.Add(item.Rule);

        if (!string.IsNullOrWhiteSpace(item.Message))
            titleParts.Add(item.Message);

        var taskLabel = taskState switch
        {
            "leased" => "Leased",
            "running" => "Running",
            "awaiting_review" => "Review",
            "rejected" => "Rejected",
            "failed" => "Failed",
            "cancelled" => "Cancelled",
            _ => "Task"
        };

        var boardStatus = taskState switch
        {
            "dispatching" or "leased" or "running" => "in_progress",
            "awaiting_review" => "completed",
            "rejected" => "cancelled",
            "failed" or "cancelled" => "cancelled",
            _ => "pending"
        };

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
            RuntimeStatus = new SessionRuntimeStatus(taskState),
            Status = boardStatus,
            HasAssistantResponse = item.SessionId is { Length: > 0 },
            OpenCodeUrl = item.OpenCodeUrl,
            IsQueueItem = true,
            QueueItemId = item.Id,
            QueueState = taskState,
            TaskId = item.Id,
            TaskState = taskState,
            TaskKey = item.TaskKey,
            TaskUnit = item.TaskUnit,
            TaskInstructions = item.Instructions,
            TaskIssueCount = item.IssueCount,
            IssueKey = string.IsNullOrWhiteSpace(item.IssueKey) ? null : item.IssueKey,
            IssueType = item.IssueType,
            IssueSeverity = item.Severity,
            IssueRule = item.Rule,
            IssuePath = item.RelativePath ?? item.AbsolutePath,
            IssueLine = item.Line,
            LastError = item.LastError,
            LastReviewAction = item.LastReviewAction,
            LastReviewReason = item.LastReviewReason,
            LastReviewedAt = item.LastReviewedAt
        };
    }
}
