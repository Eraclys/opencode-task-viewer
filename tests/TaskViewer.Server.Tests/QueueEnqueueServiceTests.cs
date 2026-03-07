using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class QueueEnqueueServiceTests
{
    [Fact]
    public async Task EnqueueRawIssuesAsync_NormalizesValidIssuesAndCollectsSkips()
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
                    CreatedAt = "",
                    UpdatedAt = ""
                }
            ],
            RepoSkipped = [new QueueSkip("sq-dupe", "already-queued")]
        };

        var sut = new QueueEnqueueService(repo, maxAttempts: 7, nowIso: () => "now");
        var mapping = new MappingRecord
        {
            Id = 1,
            SonarProjectKey = "alpha",
            Directory = "C:/Work/Alpha",
            Enabled = true,
            CreatedAt = "",
            UpdatedAt = ""
        };

        var result = await sut.EnqueueRawIssuesAsync(
            mapping,
            "CODE_SMELL",
            "instruction",
            [
                new JsonObject(),
                new JsonObject
                {
                    ["key"] = "sq-valid",
                    ["component"] = "alpha:src/file.js"
                }
            ]);

        Assert.Single(result.CreatedItems);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Contains(result.Skipped, s => string.Equals(s.reason, "invalid-issue", StringComparison.Ordinal));
        Assert.Contains(result.Skipped, s => string.Equals(s.reason, "already-queued", StringComparison.Ordinal));

        var normalized = Assert.Single(repo.LastIssues);
        Assert.Equal("sq-valid", normalized.Key);
        Assert.Equal(7, repo.LastMaxAttempts);
        Assert.Equal("now", repo.LastNow);
    }

    private sealed class FakeQueueRepository : IQueueRepository
    {
        public List<QueueItemRecord> CreatedItems { get; set; } = new();
        public List<QueueSkip> RepoSkipped { get; set; } = new();
        public List<NormalizedIssue> LastIssues { get; private set; } = new();
        public int LastMaxAttempts { get; private set; }
        public string? LastNow { get; private set; }

        public Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(MappingRecord mapping, string? type, string instructionText, IReadOnlyList<NormalizedIssue> issues, int maxAttempts, string now)
        {
            LastIssues = issues.ToList();
            LastMaxAttempts = maxAttempts;
            LastNow = now;
            return Task.FromResult((CreatedItems, RepoSkipped));
        }

        public Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<string> states, int limit) => throw new NotSupportedException();
        public Task<QueueStats> GetQueueStats() => throw new NotSupportedException();
        public Task<bool> CancelQueueItem(int id, string now) => throw new NotSupportedException();
        public Task<int> RetryFailed(string now) => throw new NotSupportedException();
        public Task<int> ClearQueued(string now) => throw new NotSupportedException();
        public Task<QueueItemRecord?> ClaimNextQueuedItem(string now) => throw new NotSupportedException();
        public Task<bool> MarkSessionCreated(int id, string sessionId, string? openCodeUrl, string timestamp) => throw new NotSupportedException();
        public Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts) => throw new NotSupportedException();
        public Task<bool> MarkDispatchFailure(int id, string state, string? nextAttemptAt, string lastError, string updatedAt) => throw new NotSupportedException();
    }
}
