using Dapper;
using Microsoft.Data.Sqlite;
using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class SqliteQueueRepositoryTests
{
    [Fact]
    public async Task EnqueueIssuesBatch_CreatesItems_AndSkipsQueuedDuplicates()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);
        var mapping = CreateMapping();
        var now = DateTimeOffset.Parse("2026-03-08T10:00:00Z");

        var first = await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-001"), CreateIssue("sq-002")],
            3,
            now);

        Assert.Single(first.CreatedItems);
        Assert.Equal(2, first.CreatedItems[0].IssueCount);
        Assert.Empty(first.Skipped);

        var second = await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-001")],
            3,
            now.AddMinutes(1));

        Assert.Empty(second.CreatedItems);
        var skipped = Assert.Single(second.Skipped);
        Assert.Equal("sq-001", skipped.IssueKey);
        Assert.Equal("already-queued", skipped.Reason);
    }

    [Fact]
    public async Task TryLeaseTask_ClaimsOldestQueuedItem_First()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);
        var mapping = CreateMapping();

        await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-old")],
            3,
            DateTimeOffset.Parse("2026-03-08T10:00:00Z"));

        await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-new", "src/other-file.js")],
            3,
            DateTimeOffset.Parse("2026-03-08T10:05:00Z"));

        var queuedItems = await repository.ListQueue(["queued"], 10);
        var oldest = queuedItems.OrderBy(item => item.CreatedAt).First();
        var claimed = await repository.TryLeaseTask(
            oldest.Id,
            "worker-1",
            DateTimeOffset.Parse("2026-03-08T10:10:00Z"),
            DateTimeOffset.Parse("2026-03-08T10:13:00Z"));

        Assert.NotNull(claimed);
        Assert.Equal("sq-old", claimed.IssueKey);
        Assert.Equal("leased", claimed.State);
        Assert.Equal("worker-1", claimed.LeaseOwner);
        Assert.NotNull(claimed.LeaseExpiresAt);

        var remainingQueued = await repository.ListQueue(["queued"], 10);
        var queued = Assert.Single(remainingQueued);
        Assert.Equal("sq-new", queued.IssueKey);
    }

    [Fact]
    public async Task RetryFailed_AndMarkTaskRunning_PreserveQueueMetadata()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);
        var mapping = CreateMapping();

        await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-retry")],
            4,
            DateTimeOffset.Parse("2026-03-08T10:00:00Z"));

        var queuedItems = await repository.ListQueue(["queued"], 10);
        var claimed = await repository.TryLeaseTask(
            queuedItems.Single().Id,
            "worker-1",
            DateTimeOffset.Parse("2026-03-08T10:01:00Z"),
            DateTimeOffset.Parse("2026-03-08T10:04:00Z"));
        Assert.NotNull(claimed);

        var attemptInfo = await repository.GetAttemptInfo(claimed.Id, 0, 0);
        Assert.Equal(1, attemptInfo.AttemptCount);
        Assert.Equal(4, attemptInfo.MaxAttempts);

        var failed = await repository.MarkDispatchFailure(
            claimed.Id,
            "failed",
            null,
            "boom",
            DateTimeOffset.Parse("2026-03-08T10:02:00Z"));

        Assert.True(failed);

        await using (var conn = OpenConnection(dbPath))
        {
            var state = await conn.QuerySingleAsync<string>("SELECT state FROM queue_items WHERE id = @Id", new { Id = claimed.Id });
            Assert.Equal("failed", state);
        }

        var failedItems = await repository.ListQueue(["failed"], 10);
        var failedItem = Assert.Single(failedItems);
        Assert.Equal("boom", failedItem.LastError);

        var retried = await repository.RetryFailed(DateTimeOffset.Parse("2026-03-08T10:03:00Z"));
        Assert.Equal(1, retried);

        var queuedAgain = await repository.ListQueue(["queued"], 10);
        var claimedAgain = await repository.TryLeaseTask(
            queuedAgain.Single().Id,
            "worker-1",
            DateTimeOffset.Parse("2026-03-08T10:04:00Z"),
            DateTimeOffset.Parse("2026-03-08T10:07:00Z"));
        Assert.NotNull(claimedAgain);

        var marked = await repository.MarkTaskRunning(
            claimedAgain.Id,
            "sess-123",
            "http://opencode.local/session/sess-123",
            "worker-1",
            DateTimeOffset.Parse("2026-03-08T10:05:00Z"),
            DateTimeOffset.Parse("2026-03-08T10:08:00Z"));

        Assert.True(marked);

        var completedItems = await repository.ListQueue(["running"], 10);
        var completed = Assert.Single(completedItems);
        Assert.Equal("sess-123", completed.SessionId);
        Assert.Equal("http://opencode.local/session/sess-123", completed.OpenCodeUrl);
        Assert.Equal("running", completed.State);
    }

    [Fact]
    public async Task CancelAndClearQueued_UpdateQueueStats()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);
        var mapping = CreateMapping();

        var created = await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-cancel"), CreateIssue("sq-clear")],
            3,
            DateTimeOffset.Parse("2026-03-08T10:00:00Z"));

        Assert.Single(created.CreatedItems);

        var secondCreate = await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-clear-2", "src/second-file.js")],
            3,
            DateTimeOffset.Parse("2026-03-08T10:00:01Z"));

        Assert.Single(secondCreate.CreatedItems);

        var cancelled = await repository.CancelQueueItem(
            created.CreatedItems[0].Id,
            DateTimeOffset.Parse("2026-03-08T10:01:00Z"));

        Assert.True(cancelled);

        var cleared = await repository.ClearQueued(DateTimeOffset.Parse("2026-03-08T10:02:00Z"));
        Assert.Equal(1, cleared);

        var stats = await repository.GetQueueStats();
        Assert.Equal(0, stats.Queued);
        Assert.Equal(2, stats.Cancelled);
    }

    [Fact]
    public async Task ReviewActions_RecordLatestReviewMetadata()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);
        var mapping = CreateMapping();

        var created = await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-review")],
            3,
            DateTimeOffset.Parse("2026-03-08T10:00:00Z"));

        var taskId = Assert.Single(created.CreatedItems).Id;

        var leased = await repository.TryLeaseTask(
            taskId,
            "worker-1",
            DateTimeOffset.Parse("2026-03-08T10:01:00Z"),
            DateTimeOffset.Parse("2026-03-08T10:04:00Z"));
        Assert.NotNull(leased);

        Assert.True(await repository.MarkTaskRunning(
            taskId,
            "sess-review",
            "http://opencode.local/session/sess-review",
            "worker-1",
            DateTimeOffset.Parse("2026-03-08T10:02:00Z"),
            DateTimeOffset.Parse("2026-03-08T10:05:00Z")));

        Assert.True(await repository.MarkTaskAwaitingReview(taskId, DateTimeOffset.Parse("2026-03-08T10:03:00Z")));
        Assert.True(await repository.RejectTask(taskId, "Needs manual follow-up", DateTimeOffset.Parse("2026-03-08T10:04:00Z")));

        var rejected = Assert.Single(await repository.ListQueue(["rejected"], 10));
        Assert.Equal("rejected", rejected.LastReviewAction);
        Assert.Equal("Needs manual follow-up", rejected.LastReviewReason);
        Assert.NotNull(rejected.LastReviewedAt);

        Assert.True(await repository.RequeueTask(taskId, "Retry with tuned prompt", DateTimeOffset.Parse("2026-03-08T10:05:00Z")));

        var queuedAgain = Assert.Single(await repository.ListQueue(["queued"], 10));
        Assert.Equal("requeue", queuedAgain.LastReviewAction);
        Assert.Equal("Retry with tuned prompt", queuedAgain.LastReviewReason);
        Assert.NotNull(queuedAgain.LastReviewedAt);

        var history = await repository.GetTaskReviewHistory(taskId);
        Assert.True(history.Count >= 2);
        Assert.Equal("requeue", history[0].Action);
    }

    [Fact]
    public async Task RepromptTask_UpdatesInstructionsAndRequeuesTask()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);
        var mapping = CreateMapping();

        var created = await repository.EnqueueIssuesBatch(
            mapping,
            "CODE_SMELL",
            "Keep the fix focused",
            [CreateIssue("sq-reprompt")],
            3,
            DateTimeOffset.Parse("2026-03-08T10:00:00Z"));

        var taskId = Assert.Single(created.CreatedItems).Id;

        var leased = await repository.TryLeaseTask(
            taskId,
            "worker-1",
            DateTimeOffset.Parse("2026-03-08T10:01:00Z"),
            DateTimeOffset.Parse("2026-03-08T10:04:00Z"));
        Assert.NotNull(leased);

        Assert.True(await repository.MarkTaskRunning(
            taskId,
            "sess-reprompt",
            "http://opencode.local/session/sess-reprompt",
            "worker-1",
            DateTimeOffset.Parse("2026-03-08T10:02:00Z"),
            DateTimeOffset.Parse("2026-03-08T10:05:00Z")));

        Assert.True(await repository.MarkTaskAwaitingReview(taskId, DateTimeOffset.Parse("2026-03-08T10:03:00Z")));
        Assert.True(await repository.RepromptTask(taskId, "Retry with a narrower patch", "Previous patch was too broad", DateTimeOffset.Parse("2026-03-08T10:04:00Z")));

        var queuedAgain = Assert.Single(await repository.ListQueue(["queued"], 10));
        Assert.Equal("Retry with a narrower patch", queuedAgain.Instructions);
        Assert.Equal("reprompt", queuedAgain.LastReviewAction);
        Assert.Equal("Previous patch was too broad", queuedAgain.LastReviewReason);
        Assert.Null(queuedAgain.SessionId);
        Assert.Equal(0, queuedAgain.AttemptCount);

        var history = await repository.GetTaskReviewHistory(taskId);
        Assert.Equal("reprompt", history[0].Action);
    }

    static SqliteQueueRepository CreateRepository(string dbPath)
    {
        var dbLock = new SemaphoreSlim(1, 1);
        var onChange = () => { };

        return new SqliteQueueRepository(dbLock, () => OpenConnection(dbPath), onChange);
    }

    static MappingRecord CreateMapping()
    {
        var timestamp = DateTimeOffset.Parse("2026-03-08T09:00:00Z");

        return new MappingRecord
        {
            Id = 1,
            SonarProjectKey = "gamma-key",
            Directory = "C:/Work/Gamma",
            Branch = "main",
            Enabled = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    static NormalizedIssue CreateIssue(string key, string relativePath = "src/file.js")
    {
        return new NormalizedIssue
        {
            Key = key,
            Type = "CODE_SMELL",
            Severity = "MAJOR",
            Rule = "javascript:S1126",
            Message = $"Message for {key}",
            Line = 42,
            Status = "OPEN",
            Component = $"gamma-key:{relativePath}",
            RelativePath = relativePath,
            AbsolutePath = $"C:/Work/Gamma/{relativePath}"
        };
    }

    static SqliteConnection OpenConnection(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
        }

        return connection;
    }

    static async Task<string> InitializeSchemaAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-queue-repo-{Guid.NewGuid():N}.sqlite");

        await using var persistence = new SqliteOrchestrationPersistence(dbPath, () => { });

        await using var conn = OpenConnection(dbPath);
        await conn.ExecuteAsync(@"
INSERT INTO project_mappings (id, sonar_project_key, directory, branch, enabled, created_at, updated_at)
VALUES (1, 'gamma-key', 'C:/Work/Gamma', 'main', 1, '2026-03-08T09:00:00Z', '2026-03-08T09:00:00Z')
ON CONFLICT(id) DO NOTHING;");

        return dbPath;
    }
}
