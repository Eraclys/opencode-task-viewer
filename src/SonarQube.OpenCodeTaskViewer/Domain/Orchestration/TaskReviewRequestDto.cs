namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class TaskReviewRequestDto
{
    public string? Instructions { get; init; }
    public string? Reason { get; init; }
}
