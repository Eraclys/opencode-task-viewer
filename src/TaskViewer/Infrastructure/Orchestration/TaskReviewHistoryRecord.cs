namespace TaskViewer.Infrastructure.Orchestration;

sealed record TaskReviewHistoryRecord(
    string Action,
    string? Reason,
    DateTimeOffset CreatedAt);
