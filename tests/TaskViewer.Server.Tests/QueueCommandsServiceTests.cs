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
        var sut = new QueueCommandsService(repo, () => now);

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
        var sut = new QueueCommandsService(repo, () => now);

        var retried = await sut.RetryFailedAsync();
        var cleared = await sut.ClearQueuedAsync();

        Assert.Equal(4, retried);
        Assert.Equal(3, cleared);
        Assert.Equal(now, repo.RetryNow);
        Assert.Equal(now, repo.ClearNow);
    }

    sealed class FakeQueueRepository : IQueueRepository
    {
        public int? CancelId { get; private set; }
        public DateTimeOffset? CancelNow { get; private set; }
        public DateTimeOffset? RetryNow { get; private set; }
        public DateTimeOffset? ClearNow { get; private set; }

        public Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(
            MappingRecord mapping,
            string? type,
            string instructionText,
            IReadOnlyList<NormalizedIssue> issues,
            int maxAttempts,
            DateTimeOffset now)
            => throw new NotSupportedException();

        public Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<string> states, int limit)
            => throw new NotSupportedException();

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

        public Task<QueueItemRecord?> ClaimNextQueuedItem(DateTimeOffset now)
            => throw new NotSupportedException();

        public Task<bool> MarkSessionCreated(
            int id,
            string sessionId,
            string? openCodeUrl,
            DateTimeOffset timestamp)
            => throw new NotSupportedException();

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
