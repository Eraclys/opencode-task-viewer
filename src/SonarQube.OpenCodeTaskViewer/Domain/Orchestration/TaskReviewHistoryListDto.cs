namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class TaskReviewHistoryListDto
{
    public required IReadOnlyList<TaskReviewHistoryDto> Items { get; init; }
}
