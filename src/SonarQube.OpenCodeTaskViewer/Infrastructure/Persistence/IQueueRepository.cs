using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

public interface IQueueRepository
{
    Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<QueueState> states, int limit, CancellationToken cancellationToken = default);
    Task<QueueStats> GetQueueStats(CancellationToken cancellationToken = default);
    Task<bool> CancelQueueItem(int id, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<int> RetryFailed(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<int> ClearQueued(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<QueueItemRecord?> TryLeaseTask(
        int id,
        string leaseOwner,
        DateTimeOffset heartbeatAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task<bool> HeartbeatTask(
        int id,
        string leaseOwner,
        DateTimeOffset heartbeatAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task<List<NormalizedIssue>> GetTaskIssues(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskReviewHistoryRecord>> GetTaskReviewHistory(int id, CancellationToken cancellationToken = default);

    Task<bool> MarkTaskRunning(
        int id,
        string sessionId,
        string? openCodeUrl,
        string leaseOwner,
        DateTimeOffset timestamp,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default);

    Task<bool> MarkTaskAwaitingReview(int id, DateTimeOffset timestamp, CancellationToken cancellationToken = default);
    Task<bool> ApproveTask(int id, DateTimeOffset timestamp, CancellationToken cancellationToken = default);

    Task<bool> RejectTask(
        int id,
        string? reason,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default);

    Task<bool> RequeueTask(
        int id,
        string? reason,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default);

    Task<bool> RepromptTask(
        int id,
        string instructions,
        string? reason,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default);

    Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(
        int id,
        int fallbackAttemptCount,
        int fallbackMaxAttempts,
        CancellationToken cancellationToken = default);

    Task<bool> MarkDispatchFailure(
        int id,
        QueueState state,
        DateTimeOffset? nextAttemptAt,
        string lastError,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}
