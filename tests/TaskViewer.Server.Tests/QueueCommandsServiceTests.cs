using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class QueueCommandsServiceTests
{
    [Fact]
    public async Task CancelQueueItemAsync_ValidatesIdAndCallsRepository()
    {
        var repo = new FakeQueueRepository();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
        var sut = new QueueCommandsService(repo, nowUtc: () => now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CancelQueueItemAsync(null));

        var result = await sut.CancelQueueItemAsync(12);

        Assert.True(result);
        Assert.Equal(12, repo.CancelId);
        Assert.Equal(now, repo.CancelNow);
    }

    [Fact]
    public async Task RetryAndClear_UseSharedTimestampProvider()
    {
        var repo = new FakeQueueRepository();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
        var sut = new QueueCommandsService(repo, nowUtc: () => now);

        var retried = await sut.RetryFailedAsync();
        var cleared = await sut.ClearQueuedAsync();

        Assert.Equal(4, retried);
        Assert.Equal(3, cleared);
        Assert.Equal(now, repo.RetryNow);
        Assert.Equal(now, repo.ClearNow);
    }

    [Fact]
    public async Task ReviewActions_ValidateIdsAndCallRepository()
    {
        var repo = new FakeQueueRepository();
        repo.QueueItems =
        [
            new QueueItemRecord
            {
                Id = 10,
                State = "awaiting_review",
                SessionId = "sess-10",
                Directory = "C:/Work/Gamma",
                MappingId = 1,
                SonarProjectKey = "gamma",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            }
        ];
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
        var archivedSessions = new List<(string SessionId, string? Directory)>();
        var sut = new QueueCommandsService(repo, (sessionId, directory) =>
        {
            archivedSessions.Add((sessionId, directory));
            return Task.FromResult<DateTimeOffset?>(now);
        }, () => now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ApproveTaskAsync(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RejectTaskAsync(null, "bad output"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RequeueTaskAsync(null, "retry later"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RepromptTaskAsync(null, "retry with tighter instructions", "adjust prompt"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RepromptTaskAsync(13, null, "adjust prompt"));

        Assert.True(await sut.ApproveTaskAsync(10));
        Assert.True(await sut.RejectTaskAsync(11, "bad output"));
        Assert.True(await sut.RequeueTaskAsync(12, "retry later"));
        Assert.True(await sut.RepromptTaskAsync(13, "retry with tighter instructions", "adjust prompt"));

        Assert.Equal(10, repo.ApproveId);
        Assert.Equal(11, repo.RejectId);
        Assert.Equal(12, repo.RequeueId);
        Assert.Equal(13, repo.RepromptId);
        Assert.Equal("bad output", repo.RejectReason);
        Assert.Equal("retry later", repo.RequeueReason);
        Assert.Equal("retry with tighter instructions", repo.RepromptInstructions);
        Assert.Equal("adjust prompt", repo.RepromptReason);
        Assert.Equal(now, repo.ApproveNow);
        Assert.Equal(now, repo.RejectNow);
        Assert.Equal(now, repo.RequeueNow);
        Assert.Equal(now, repo.RepromptNow);
        Assert.Single(archivedSessions);
        Assert.Equal("sess-10", archivedSessions[0].SessionId);
    }

    sealed class FakeQueueRepository : IQueueRepository
    {
        public int? CancelId { get; private set; }
        public DateTimeOffset? CancelNow { get; private set; }
        public DateTimeOffset? RetryNow { get; private set; }
        public DateTimeOffset? ClearNow { get; private set; }
        public List<QueueItemRecord> QueueItems { get; set; } = [];
        public int? ApproveId { get; private set; }
        public DateTimeOffset? ApproveNow { get; private set; }
        public int? RejectId { get; private set; }
        public string? RejectReason { get; private set; }
        public DateTimeOffset? RejectNow { get; private set; }
        public int? RequeueId { get; private set; }
        public string? RequeueReason { get; private set; }
        public DateTimeOffset? RequeueNow { get; private set; }
        public int? RepromptId { get; private set; }
        public string? RepromptInstructions { get; private set; }
        public string? RepromptReason { get; private set; }
        public DateTimeOffset? RepromptNow { get; private set; }

        public Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(
            MappingRecord mapping,
            string? type,
            string instructionText,
            IReadOnlyList<NormalizedIssue> issues,
            int maxAttempts,
            DateTimeOffset now)
            => throw new NotSupportedException();

        public Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<string> states, int limit)
            => Task.FromResult(QueueItems);

        public Task<QueueStats> GetQueueStats()
            => throw new NotSupportedException();

        public Task<bool> CancelQueueItem(int id, DateTimeOffset now)
        {
            CancelId = id;
            CancelNow = now;

            return Task.FromResult(true);
        }

        public Task<int> RetryFailed(DateTimeOffset now)
        {
            RetryNow = now;

            return Task.FromResult(4);
        }

        public Task<int> ClearQueued(DateTimeOffset now)
        {
            ClearNow = now;

            return Task.FromResult(3);
        }

        public Task<QueueItemRecord?> TryLeaseTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt)
            => throw new NotSupportedException();

        public Task<bool> HeartbeatTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt)
            => throw new NotSupportedException();

        public Task<List<NormalizedIssue>> GetTaskIssues(int id)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TaskReviewHistoryRecord>> GetTaskReviewHistory(int id)
            => throw new NotSupportedException();

        public Task<bool> MarkTaskRunning(int id, string sessionId, string? openCodeUrl, string leaseOwner, DateTimeOffset timestamp, DateTimeOffset leaseExpiresAt)
            => throw new NotSupportedException();

        public Task<bool> MarkTaskAwaitingReview(int id, DateTimeOffset timestamp)
            => throw new NotSupportedException();

        public Task<bool> ApproveTask(int id, DateTimeOffset timestamp)
        {
            ApproveId = id;
            ApproveNow = timestamp;
            return Task.FromResult(true);
        }

        public Task<bool> RejectTask(int id, string? reason, DateTimeOffset timestamp)
        {
            RejectId = id;
            RejectReason = reason;
            RejectNow = timestamp;
            return Task.FromResult(true);
        }

        public Task<bool> RequeueTask(int id, string? reason, DateTimeOffset timestamp)
        {
            RequeueId = id;
            RequeueReason = reason;
            RequeueNow = timestamp;
            return Task.FromResult(true);
        }

        public Task<bool> RepromptTask(int id, string instructions, string? reason, DateTimeOffset timestamp)
        {
            RepromptId = id;
            RepromptInstructions = instructions;
            RepromptReason = reason;
            RepromptNow = timestamp;
            return Task.FromResult(true);
        }

        public Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts)
            => throw new NotSupportedException();

        public Task<bool> MarkDispatchFailure(
            int id,
            string state,
            DateTimeOffset? nextAttemptAt,
            string lastError,
            DateTimeOffset updatedAt)
            => throw new NotSupportedException();
    }
}
