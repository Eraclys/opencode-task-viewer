namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class QueueEnqueueSkipView
{
    public string? IssueKey { get; init; }
    public required string Reason { get; init; }
}
