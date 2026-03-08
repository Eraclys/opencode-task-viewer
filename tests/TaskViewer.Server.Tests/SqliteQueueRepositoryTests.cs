using Microsoft.Data.Sqlite;
using TaskViewer.OpenCode;
using TaskViewer.Server.Infrastructure.Orchestration;

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

        Assert.Equal(2, first.CreatedItems.Count);
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
    public async Task ClaimNextQueuedItem_ClaimsOldestQueuedItem_First()
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
            [CreateIssue("sq-new")],
            3,
            DateTimeOffset.Parse("2026-03-08T10:05:00Z"));

        var claimed = await repository.ClaimNextQueuedItem(DateTimeOffset.Parse("2026-03-08T10:10:00Z"));

        Assert.NotNull(claimed);
        Assert.Equal("sq-old", claimed!.IssueKey);
        Assert.Equal("dispatching", claimed.State);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.NotNull(claimed.DispatchedAt);

        var remainingQueued = await repository.ListQueue(["queued"], 10);
        var queued = Assert.Single(remainingQueued);
        Assert.Equal("sq-new", queued.IssueKey);
    }

    [Fact]
    public async Task RetryFailed_AndMarkSessionCreated_PreserveQueueMetadata()
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

        var claimed = await repository.ClaimNextQueuedItem(DateTimeOffset.Parse("2026-03-08T10:01:00Z"));
        Assert.NotNull(claimed);

        var attemptInfo = await repository.GetAttemptInfo(claimed!.Id, 0, 0);
        Assert.Equal(1, attemptInfo.AttemptCount);
        Assert.Equal(4, attemptInfo.MaxAttempts);

        var failed = await repository.MarkDispatchFailure(
            claimed.Id,
            "failed",
            null,
            "boom",
            DateTimeOffset.Parse("2026-03-08T10:02:00Z"));

        Assert.True(failed);

        var failedItems = await repository.ListQueue(["failed"], 10);
        var failedItem = Assert.Single(failedItems);
        Assert.Equal("boom", failedItem.LastError);

        var retried = await repository.RetryFailed(DateTimeOffset.Parse("2026-03-08T10:03:00Z"));
        Assert.Equal(1, retried);

        var claimedAgain = await repository.ClaimNextQueuedItem(DateTimeOffset.Parse("2026-03-08T10:04:00Z"));
        Assert.NotNull(claimedAgain);

        var marked = await repository.MarkSessionCreated(
            claimedAgain!.Id,
            "sess-123",
            "http://opencode.local/session/sess-123",
            DateTimeOffset.Parse("2026-03-08T10:05:00Z"));

        Assert.True(marked);

        var completedItems = await repository.ListQueue(["session_created"], 10);
        var completed = Assert.Single(completedItems);
        Assert.Equal("sess-123", completed.SessionId);
        Assert.Equal("http://opencode.local/session/sess-123", completed.OpenCodeUrl);
        Assert.Equal("session_created", completed.State);
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

        Assert.Equal(2, created.CreatedItems.Count);

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

    static NormalizedIssue CreateIssue(string key)
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
            Component = "gamma-key:src/file.js",
            RelativePath = "src/file.js",
            AbsolutePath = "C:/Work/Gamma/src/file.js"
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

        return connection;
    }

    static async Task<string> InitializeSchemaAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-queue-repo-{Guid.NewGuid():N}.sqlite");

        await using var orchestrator = new SonarOrchestrator(
            new SonarOrchestratorOptions
            {
                SonarUrl = string.Empty,
                SonarToken = string.Empty,
                DbPath = dbPath,
                MaxActive = 1,
                PollMs = 1000,
                MaxAttempts = 1,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeStatusReader = new DisabledOpenCodeStatusReader(),
                OpenCodeDispatchClient = new DisabledOpenCodeDispatchClient(),
                NormalizeDirectory = value => value,
                BuildOpenCodeSessionUrl = (_, _) => null,
                OnChange = () => { }
            });

        return dbPath;
    }
}
