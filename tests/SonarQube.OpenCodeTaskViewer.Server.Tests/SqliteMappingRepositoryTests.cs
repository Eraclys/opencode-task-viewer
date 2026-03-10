using Microsoft.Data.Sqlite;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;
using SonarQube.OpenCodeTaskViewer.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class SqliteMappingRepositoryTests
{
    [Fact]
    public async Task UpsertMapping_CreatesAndUpdatesMapping()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);

        var created = await repository.UpsertMapping(
            null,
            "alpha-key",
            "C:/Work/Alpha",
            null,
            true,
            DateTimeOffset.UtcNow);

        Assert.True(created.Id > 0);
        Assert.Equal("alpha-key", created.SonarProjectKey);

        var updated = await repository.UpsertMapping(
            created.Id,
            "alpha-key",
            "C:/Work/Alpha2",
            "main",
            false,
            DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("C:/Work/Alpha2", updated.Directory);
        Assert.Equal("main", updated.Branch);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task InstructionProfile_RoundTrips()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);

        var mapping = await repository.UpsertMapping(
            null,
            "gamma-key",
            "C:/Work/Gamma",
            null,
            true,
            DateTimeOffset.UtcNow);

        var saved = await repository.UpsertInstructionProfile(
            mapping.Id,
            "CODE_SMELL",
            "Fix only this issue",
            DateTimeOffset.UtcNow);

        Assert.Equal("CODE_SMELL", saved.IssueType);

        var loaded = await repository.GetInstructionProfile(mapping.Id, "CODE_SMELL");
        Assert.NotNull(loaded);
        Assert.Equal("Fix only this issue", loaded.Instructions);
    }

    [Fact]
    public async Task ListEnabledMappingDirectories_DeduplicatesSlashes()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);

        await repository.UpsertMapping(
            null,
            "p1",
            "C:/Work/One",
            null,
            true,
            DateTimeOffset.UtcNow);

        await repository.UpsertMapping(
            null,
            "p2",
            "C:\\Work\\One",
            null,
            true,
            DateTimeOffset.UtcNow);

        var dirs = await repository.ListEnabledMappingDirectories();

        Assert.Single(dirs);
    }

    [Fact]
    public async Task DeleteMapping_RemovesMappingAndInstructionProfiles()
    {
        var dbPath = await InitializeSchemaAsync();
        var repository = CreateRepository(dbPath);

        var mapping = await repository.UpsertMapping(
            null,
            "gamma-key",
            "C:/Work/Gamma",
            null,
            true,
            DateTimeOffset.UtcNow);

        await repository.UpsertInstructionProfile(
            mapping.Id,
            "CODE_SMELL",
            "Fix only this issue",
            DateTimeOffset.UtcNow);

        Assert.True(await repository.DeleteMapping(mapping.Id));
        Assert.Null(await repository.GetMappingById(mapping.Id));
        Assert.Null(await repository.GetInstructionProfile(mapping.Id, "CODE_SMELL"));
    }

    [Fact]
    public async Task DeleteMapping_CascadesQueueItemsAndReviewArtifacts()
    {
        var dbPath = await InitializeSchemaAsync();
        var mappingRepository = CreateRepository(dbPath);
        var queueRepository = CreateQueueRepository(dbPath);

        var mapping = await mappingRepository.UpsertMapping(
            null,
            "gamma-key",
            "C:/Work/Gamma",
            "main",
            true,
            DateTimeOffset.UtcNow);

        var created = await queueRepository.EnqueueIssuesBatch(
            new MappingRecord
            {
                Id = mapping.Id,
                SonarProjectKey = mapping.SonarProjectKey,
                Directory = mapping.Directory,
                Branch = mapping.Branch,
                Enabled = mapping.Enabled,
                CreatedAt = mapping.CreatedAt,
                UpdatedAt = mapping.UpdatedAt
            },
            "CODE_SMELL",
            "Fix only this issue",
            [
                new NormalizedIssue
                {
                    Key = "sq-1",
                    Type = "CODE_SMELL",
                    Severity = "MAJOR",
                    Rule = "javascript:S1126",
                    Message = "Remove redundant boolean literal",
                    Line = 42,
                    Status = "OPEN",
                    Component = "gamma-key:src/file.js",
                    RelativePath = "src/file.js",
                    AbsolutePath = "C:/Work/Gamma/src/file.js"
                }
            ],
            3,
            DateTimeOffset.UtcNow);

        var taskId = Assert.Single(created.CreatedItems).Id;

        Assert.True(
            await queueRepository.TryLeaseTask(
                taskId,
                "worker-1",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1)) is not null);

        Assert.True(
            await queueRepository.MarkTaskRunning(
                taskId,
                "sess-1",
                null,
                "worker-1",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1)));

        Assert.True(await queueRepository.MarkTaskAwaitingReview(taskId, DateTimeOffset.UtcNow));
        Assert.True(await queueRepository.RejectTask(taskId, "Needs follow-up", DateTimeOffset.UtcNow));

        Assert.True(await mappingRepository.DeleteMapping(mapping.Id));
        Assert.Empty(await queueRepository.ListQueue([], 10));
        Assert.Empty(await queueRepository.GetTaskIssues(taskId));
        Assert.Empty(await queueRepository.GetTaskReviewHistory(taskId));
    }

    [Fact]
    public async Task Schema_EnablesForeignKeys()
    {
        var dbPath = await InitializeSchemaAsync();

        await using var conn = OpenConnection(dbPath);
        await using var command = conn.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_list(task_issue_links)";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("queue_items", reader.GetString(2));
    }

    static SqliteMappingRepository CreateRepository(string dbPath)
    {
        var onChange = () => { };

        return new SqliteMappingRepository(() => OpenConnection(dbPath), onChange);
    }

    static SqliteQueueRepository CreateQueueRepository(string dbPath)
    {
        var onChange = () => { };

        return new SqliteQueueRepository(() => OpenConnection(dbPath), onChange);
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
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-mapping-repo-{Guid.NewGuid():N}.sqlite");

        await using var persistence = new SqliteOrchestrationPersistence(dbPath, () => { });

        return dbPath;
    }
}
