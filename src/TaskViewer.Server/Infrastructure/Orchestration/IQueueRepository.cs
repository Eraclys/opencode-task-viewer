using TaskViewer.Server;

namespace TaskViewer.Server.Infrastructure.Orchestration;

internal interface IQueueRepository
{
    Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues,
        int maxAttempts,
        string now);

    Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<string> states, int limit);
    Task<QueueStats> GetQueueStats();
    Task<bool> CancelQueueItem(int id, string now);
    Task<int> RetryFailed(string now);
    Task<int> ClearQueued(string now);
    Task<QueueItemRecord?> ClaimNextQueuedItem(string now);
    Task<bool> MarkSessionCreated(int id, string sessionId, string? openCodeUrl, string timestamp);
    Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts);
    Task<bool> MarkDispatchFailure(int id, string state, string? nextAttemptAt, string lastError, string updatedAt);
}

internal sealed record QueueSkip(string IssueKey, string Reason);

internal sealed record QueueStats(int Queued, int Dispatching, int SessionCreated, int Done, int Failed, int Cancelled);
