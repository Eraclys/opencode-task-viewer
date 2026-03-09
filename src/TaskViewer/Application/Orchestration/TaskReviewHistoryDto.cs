namespace TaskViewer.Application.Orchestration;

public sealed class TaskReviewHistoryDto
{
    public required string Action { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
