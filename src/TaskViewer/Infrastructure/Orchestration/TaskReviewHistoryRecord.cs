namespace TaskViewer.Infrastructure.Orchestration;

public sealed record TaskReviewHistoryRecord(
    string Action,
    string? Reason,
    DateTimeOffset CreatedAt);
