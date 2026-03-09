namespace TaskViewer.Infrastructure.Persistence;

public sealed record TaskReviewHistoryRecord(
    string Action,
    string? Reason,
    DateTimeOffset CreatedAt);
