namespace TaskViewer.Server.Application.Orchestration;

internal interface IQueueEnqueueService
{
    Task<QueueEnqueueBatchResult> EnqueueRawIssuesAsync(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues);
}
