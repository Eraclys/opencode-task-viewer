namespace TaskViewer.Server.Application.Orchestration;

public sealed class QueueOverviewDto
{
    public required List<QueueItemRecord> Items { get; init; }
    public required QueueStatsDto Stats { get; init; }
    public required OrchestrationWorkerStateDto Worker { get; init; }
}
