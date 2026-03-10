using TaskViewer.Domain.Orchestration;

namespace TaskViewer.Infrastructure.Persistence;

public sealed record TaskReviewHistoryRecord(
    string Action,
    string? Reason,
    DateTimeOffset CreatedAt)
{
    public TaskReviewAction ParsedAction => TaskReviewAction.FromRaw(Action);
}
