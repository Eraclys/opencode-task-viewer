namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class TaskReviewSummaryDto
{
    public int AwaitingReview { get; init; }
    public int Rejected { get; init; }
}
