namespace TaskViewer.Server.Application.Orchestration;

public sealed class EnqueueIssuesResultDto
{
    public required int Created { get; init; }
    public required IReadOnlyList<QueueEnqueueSkipView> Skipped { get; init; }
    public required IReadOnlyList<QueueItemRecord> Items { get; init; }
}
