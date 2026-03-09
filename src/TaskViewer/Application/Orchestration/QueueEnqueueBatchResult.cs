namespace TaskViewer.Application.Orchestration;

public sealed record QueueEnqueueBatchResult(
    List<QueueItemRecord> CreatedItems,
    List<QueueEnqueueSkipView> Skipped);
