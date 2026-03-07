using Microsoft.Data.Sqlite;
using TaskViewer.OpenCode;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

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
        Assert.Equal("Fix only this issue", loaded!.Instructions);
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

    static SqliteMappingRepository CreateRepository(string dbPath)
    {
        var dbLock = new SemaphoreSlim(1, 1);
        var onChange = () => { };

        return new SqliteMappingRepository(dbLock, () => OpenConnection(dbPath), onChange);
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
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-mapping-repo-{Guid.NewGuid():N}.sqlite");

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
