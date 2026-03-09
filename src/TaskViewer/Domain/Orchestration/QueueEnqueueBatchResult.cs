using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed record QueueEnqueueBatchResult(
    List<QueueItemRecord> CreatedItems,
    List<QueueEnqueueSkipView> Skipped);
