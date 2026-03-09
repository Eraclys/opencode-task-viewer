namespace TaskViewer.Application.Orchestration;

public sealed class TaskReviewRequestDto
{
    public string? Instructions { get; init; }
    public string? Reason { get; init; }
}
