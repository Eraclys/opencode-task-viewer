using TaskViewer.Server.Application.Sessions;

namespace TaskViewer.Server.Infrastructure.OpenCode;

sealed class QueueItemSessionSummaryMapper
{
    public SessionSummaryDto? Map(QueueItemRecord item)
    {
        var queueState = (item.State ?? string.Empty).Trim().ToLowerInvariant();

        if (queueState is not ("queued" or "dispatching" or "leased" or "running"))
            return null;

        var titleParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.IssueKey))
            titleParts.Add(item.IssueKey);

        if (!string.IsNullOrWhiteSpace(item.Rule))
            titleParts.Add(item.Rule);

        if (!string.IsNullOrWhiteSpace(item.Message))
            titleParts.Add(item.Message);

        var queueLabel = queueState switch
        {
            "leased" => "Leased",
            "running" => "Running",
            _ => "Queued"
        };

        var name = titleParts.Count > 0
            ? $"[{queueLabel}] {string.Join(" - ", titleParts)}"
            : $"[{queueLabel}] Item #{item.Id}";

        return new SessionSummaryDto
        {
            Id = $"queue-{item.Id}",
            Name = name,
            Project = item.Directory,
            Description = item.Message,
            GitBranch = null,
            CreatedAt = item.CreatedAt,
            ModifiedAt = item.UpdatedAt,
            RuntimeStatus = new SessionRuntimeStatus(queueState is "dispatching" or "leased" or "running" ? "busy" : "queued"),
            Status = "pending",
            HasAssistantResponse = false,
            OpenCodeUrl = null,
            IsQueueItem = true,
            QueueItemId = item.Id,
            QueueState = queueState,
            IssueKey = string.IsNullOrWhiteSpace(item.IssueKey) ? null : item.IssueKey,
            IssueType = item.IssueType,
            IssueSeverity = item.Severity,
            IssueRule = item.Rule,
            IssuePath = item.RelativePath ?? item.AbsolutePath,
            IssueLine = item.Line,
            LastError = item.LastError
        };
    }
}
