using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed class EnqueueIssuesResultDto
{
    public int Requested { get; set; }
    public required int Created { get; init; }
    public required IReadOnlyList<QueueEnqueueSkipView> Skipped { get; init; }
    public required IReadOnlyList<QueueItemRecord> Items { get; init; }
}
