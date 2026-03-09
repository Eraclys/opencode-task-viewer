namespace TaskViewer.Application.Orchestration;

public interface IQueueEnqueueService
{
    Task<QueueEnqueueBatchResult> EnqueueRawIssuesAsync(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues);
}
