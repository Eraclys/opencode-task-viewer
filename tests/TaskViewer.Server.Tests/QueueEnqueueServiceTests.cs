using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class QueueEnqueueServiceTests
{
    [Fact]
    public async Task EnqueueRawIssuesAsync_PassesNormalizedIssuesToRepository_AndCollectsRepoSkips()
    {
        var repo = new FakeQueueRepository
        {
            CreatedItems =
            [
                new QueueItemRecord
                {
                    Id = 42,
                    IssueKey = "sq-created",
                    MappingId = 1,
                    SonarProjectKey = "alpha",
                    Directory = "C:/Work/Alpha",
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    UpdatedAt = DateTimeOffset.UnixEpoch
                }
            ],
            RepoSkipped = [new QueueSkip("sq-dupe", "already-queued")]
        };

        var now = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
        var sut = new QueueEnqueueService(repo, maxAttempts: 7, nowUtc: () => now);
        var result = await sut.EnqueueRawIssuesAsync(
            new MappingRecord
            {
                Id = 1,
                SonarProjectKey = "alpha",
                Directory = "C:/Work/Alpha",
                Enabled = true,
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            },
            "CODE_SMELL",
            "instruction",
            [
                new NormalizedIssue
                {
                    Key = "sq-valid",
                    Type = "CODE_SMELL",
                    Component = "alpha:src/file.js",
                    RelativePath = "src/file.js",
                    AbsolutePath = "C:/Work/Alpha/src/file.js"
                }
            ]);

        Assert.Single(result.CreatedItems);
        Assert.Single(result.Skipped);
        Assert.Contains(result.Skipped, s => string.Equals(s.Reason, "already-queued", StringComparison.Ordinal));

        var normalized = Assert.Single(repo.LastIssues);
        Assert.Equal("sq-valid", normalized.Key);
        Assert.Equal(7, repo.LastMaxAttempts);
        Assert.Equal(now, repo.LastNow);
    }

    private sealed class FakeQueueRepository : IQueueRepository
    {
        public List<QueueItemRecord> CreatedItems { get; set; } = new();
        public List<QueueSkip> RepoSkipped { get; set; } = new();
        public List<NormalizedIssue> LastIssues { get; private set; } = new();
        public int LastMaxAttempts { get; private set; }
        public DateTimeOffset? LastNow { get; private set; }

        public Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(MappingRecord mapping, string? type, string instructionText, IReadOnlyList<NormalizedIssue> issues, int maxAttempts, DateTimeOffset now)
        {
            LastIssues = issues.ToList();
            LastMaxAttempts = maxAttempts;
            LastNow = now;
            return Task.FromResult((CreatedItems, RepoSkipped));
        }

        public Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<string> states, int limit) => throw new NotSupportedException();
        public Task<QueueStats> GetQueueStats() => throw new NotSupportedException();
        public Task<bool> CancelQueueItem(int id, DateTimeOffset now) => throw new NotSupportedException();
        public Task<int> RetryFailed(DateTimeOffset now) => throw new NotSupportedException();
        public Task<int> ClearQueued(DateTimeOffset now) => throw new NotSupportedException();
        public Task<QueueItemRecord?> TryLeaseTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt) => throw new NotSupportedException();
        public Task<bool> HeartbeatTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt) => throw new NotSupportedException();
        public Task<List<NormalizedIssue>> GetTaskIssues(int id) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskReviewHistoryRecord>> GetTaskReviewHistory(int id) => throw new NotSupportedException();
        public Task<bool> MarkTaskRunning(int id, string sessionId, string? openCodeUrl, string leaseOwner, DateTimeOffset timestamp, DateTimeOffset leaseExpiresAt) => throw new NotSupportedException();
        public Task<bool> MarkTaskAwaitingReview(int id, DateTimeOffset timestamp) => throw new NotSupportedException();
        public Task<bool> ApproveTask(int id, DateTimeOffset timestamp) => throw new NotSupportedException();
        public Task<bool> RejectTask(int id, string? reason, DateTimeOffset timestamp) => throw new NotSupportedException();
        public Task<bool> RequeueTask(int id, string? reason, DateTimeOffset timestamp) => throw new NotSupportedException();
        public Task<bool> RepromptTask(int id, string instructions, string? reason, DateTimeOffset timestamp) => throw new NotSupportedException();
        public Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts) => throw new NotSupportedException();
        public Task<bool> MarkDispatchFailure(int id, string state, DateTimeOffset? nextAttemptAt, string lastError, DateTimeOffset updatedAt) => throw new NotSupportedException();
    }
}
