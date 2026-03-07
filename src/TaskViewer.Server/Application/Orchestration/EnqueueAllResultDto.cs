namespace TaskViewer.Server.Application.Orchestration;

public sealed class EnqueueAllResultDto
{
    public required int Matched { get; init; }
    public required int Created { get; init; }
    public required IReadOnlyList<QueueEnqueueSkipView> Skipped { get; init; }
    public required bool Truncated { get; init; }
    public required IReadOnlyList<QueueItemRecord> Items { get; init; }
}
