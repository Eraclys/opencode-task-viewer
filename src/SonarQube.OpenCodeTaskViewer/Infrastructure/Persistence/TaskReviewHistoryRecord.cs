using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

public sealed record TaskReviewHistoryRecord(
    string Action,
    string? Reason,
    DateTimeOffset CreatedAt)
{
    public TaskReviewAction ReviewAction => TaskReviewAction.FromRaw(Action);
}
