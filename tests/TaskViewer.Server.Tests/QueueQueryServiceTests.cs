using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class QueueQueryServiceTests
{
    [Fact]
    public async Task ListQueueAsync_NormalizesStatesAndClampsLimit()
    {
        var repo = new FakeQueueRepository();
        var sut = new QueueQueryService(repo);

        await sut.ListQueueAsync("queued,FAILED,ignored,queued", "10000");

        Assert.Equal(
            [
                "queued",
                "failed"
            ],
            repo.LastStates);

        Assert.Equal(5000, repo.LastLimit);
    }

    [Fact]
    public async Task ListQueueAsync_HandlesCsvStateInput()
    {
        var repo = new FakeQueueRepository();
        var sut = new QueueQueryService(repo);

        await sut.ListQueueAsync("done,cancelled,bogus", "-2");

        Assert.Equal(
            [
                "done",
                "cancelled"
            ],
            repo.LastStates);

        Assert.Equal(1, repo.LastLimit);
    }

    [Fact]
    public async Task GetQueueStatsAsync_ReturnsRepositoryStats()
    {
        var repo = new FakeQueueRepository
        {
            Stats = new QueueStats(
                1,
                2,
                3,
                4,
                5,
                6)
        };

        var sut = new QueueQueryService(repo);

        var stats = await sut.GetQueueStatsAsync();

        Assert.Equal(1, stats.Queued);
        Assert.Equal(6, stats.Cancelled);
    }

    sealed class FakeQueueRepository : IQueueRepository
    {
        public List<string> LastStates { get; private set; } = new();
        public int LastLimit { get; private set; }

        public QueueStats Stats { get; set; } = new(
            0,
            0,
            0,
            0,
            0,
            0);

        public Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(
            MappingRecord mapping,
            string? type,
            string instructionText,
            IReadOnlyList<NormalizedIssue> issues,
            int maxAttempts,
            DateTimeOffset now)
            => throw new NotSupportedException();

        public Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<string> states, int limit)
        {
            LastStates = states.ToList();
            LastLimit = limit;

            return Task.FromResult(new List<QueueItemRecord>());
        }

        public Task<QueueStats> GetQueueStats() => Task.FromResult(Stats);
        public Task<bool> CancelQueueItem(int id, DateTimeOffset now) => throw new NotSupportedException();
        public Task<int> RetryFailed(DateTimeOffset now) => throw new NotSupportedException();
        public Task<int> ClearQueued(DateTimeOffset now) => throw new NotSupportedException();
        public Task<QueueItemRecord?> TryLeaseTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt) => throw new NotSupportedException();
        public Task<bool> HeartbeatTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt) => throw new NotSupportedException();
        public Task<List<NormalizedIssue>> GetTaskIssues(int id) => throw new NotSupportedException();
        public Task<bool> MarkTaskRunning(int id, string sessionId, string? openCodeUrl, string leaseOwner, DateTimeOffset timestamp, DateTimeOffset leaseExpiresAt) => throw new NotSupportedException();
        public Task<bool> MarkTaskAwaitingReview(int id, DateTimeOffset timestamp) => throw new NotSupportedException();

        public Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts) => throw new NotSupportedException();

        public Task<bool> MarkDispatchFailure(
            int id,
            string state,
            DateTimeOffset? nextAttemptAt,
            string lastError,
            DateTimeOffset updatedAt) => throw new NotSupportedException();
    }
}
