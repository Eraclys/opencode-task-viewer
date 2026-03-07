namespace TaskViewer.Server.Application.Orchestration;

internal sealed record QueueEnqueueBatchResult(
    List<QueueItemRecord> CreatedItems,
    List<QueueEnqueueSkipView> Skipped);
