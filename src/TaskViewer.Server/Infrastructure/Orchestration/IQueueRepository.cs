namespace TaskViewer.Server.Infrastructure.Orchestration;

interface IQueueRepository
{
    Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues,
        int maxAttempts,
        DateTimeOffset now);

    Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<string> states, int limit);
    Task<QueueStats> GetQueueStats();
    Task<bool> CancelQueueItem(int id, DateTimeOffset now);
    Task<int> RetryFailed(DateTimeOffset now);
    Task<int> ClearQueued(DateTimeOffset now);
    Task<QueueItemRecord?> ClaimNextQueuedItem(DateTimeOffset now);

    Task<bool> MarkSessionCreated(
        int id,
        string sessionId,
        string? openCodeUrl,
        DateTimeOffset timestamp);

    Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts);

    Task<bool> MarkDispatchFailure(
        int id,
        string state,
        DateTimeOffset? nextAttemptAt,
        string lastError,
        DateTimeOffset updatedAt);
}
