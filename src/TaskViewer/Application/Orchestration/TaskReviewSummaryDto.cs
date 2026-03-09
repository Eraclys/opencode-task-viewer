namespace TaskViewer.Application.Orchestration;

public sealed class TaskReviewSummaryDto
{
    public int AwaitingReview { get; init; }
    public int Rejected { get; init; }
}
