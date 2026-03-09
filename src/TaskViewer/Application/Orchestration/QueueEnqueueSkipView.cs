namespace TaskViewer.Application.Orchestration;

public sealed class QueueEnqueueSkipView
{
    public string? IssueKey { get; init; }
    public required string Reason { get; init; }
}
