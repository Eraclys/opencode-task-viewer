using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed class QueueOverviewDto
{
    public required List<QueueItemRecord> Items { get; init; }
    public required QueueStatsDto Stats { get; init; }
    public required OrchestrationWorkerStateDto Worker { get; init; }
    public TaskReviewSummaryDto? Review { get; init; }
}
