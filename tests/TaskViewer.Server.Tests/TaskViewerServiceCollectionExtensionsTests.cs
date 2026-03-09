using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using TaskViewer.Application.Orchestration;
using TaskViewer.OpenCode;
using TaskViewer.Server.Configuration;
using TaskViewer.Server.DependencyInjection;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Tests;

public sealed class TaskViewerServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddTaskViewerServerInfrastructure_RegistersDirectAdr0006Boundaries()
    {
        var services = new ServiceCollection();
        services.AddSingleton(
            new AppRuntimeSettings(
                new ViewerRuntimeSettings("127.0.0.1", 8080),
                new OpenCodeRuntimeSettings("http://localhost:4096", "opencode", "secret"),
                new SonarQubeRuntimeSettings("http://sonar.local", "token", SonarQubeMode.Real),
                new OrchestrationRuntimeSettings(
                    Path.Combine(Path.GetTempPath(), $"taskviewer-di-{Guid.NewGuid():N}.sqlite"),
                    MaxActive: 1,
                    PerProjectMaxActive: 1,
                    PollMs: 1000,
                    LeaseSeconds: 180,
                    MaxAttempts: 3,
                    MaxWorkingGlobal: 5,
                    WorkingResumeBelow: 3)));

        services
            .AddTaskViewerServerInfrastructure()
            .AddTaskViewerServerApplication();

        await using var provider = services.BuildServiceProvider();

        var openCodeApiClient = provider.GetRequiredService<IOpenCodeService>();
        var openCodeStatusReader = provider.GetRequiredService<IOpenCodeStatusReader>();
        var openCodeDispatchClient = provider.GetRequiredService<IOpenCodeDispatchClient>();
        var sonarQubeApiClient = provider.GetRequiredService<SonarQubeService>();
        var sonarQubeService = provider.GetRequiredService<ISonarQubeService>();
        var orchestrator = provider.GetRequiredService<SonarOrchestrator>();
        var orchestrationUseCases = provider.GetRequiredService<IOrchestrationUseCases>();
        var orchestrationGateway = provider.GetService<IOrchestrationGateway>();

        Assert.Same(openCodeApiClient, openCodeStatusReader);
        Assert.Same(openCodeApiClient, openCodeDispatchClient);
        Assert.Same(sonarQubeApiClient, sonarQubeService);
        Assert.IsType<OrchestrationUseCases>(orchestrationUseCases);
        Assert.Null(orchestrationGateway);
        Assert.NotNull(orchestrator);
    }

    [Fact]
    public async Task AddTaskViewerServerInfrastructure_UsesFakeSonarServiceInFakeMode()
    {
        var services = new ServiceCollection();
        services.AddSingleton(
            new AppRuntimeSettings(
                new ViewerRuntimeSettings("127.0.0.1", 8080),
                new OpenCodeRuntimeSettings("http://localhost:4096", "opencode", "secret"),
                new SonarQubeRuntimeSettings(string.Empty, string.Empty, SonarQubeMode.Fake),
                new OrchestrationRuntimeSettings(
                    Path.Combine(Path.GetTempPath(), $"taskviewer-fake-sonar-{Guid.NewGuid():N}.sqlite"),
                    MaxActive: 1,
                    PerProjectMaxActive: 1,
                    PollMs: 1000,
                    LeaseSeconds: 180,
                    MaxAttempts: 3,
                    MaxWorkingGlobal: 5,
                    WorkingResumeBelow: 3)));

        services
            .AddTaskViewerServerInfrastructure()
            .AddTaskViewerServerApplication();

        await using var provider = services.BuildServiceProvider();

        var sonarQubeService = provider.GetRequiredService<ISonarQubeService>();

        Assert.Equal("FakeSonarQubeService", sonarQubeService.GetType().Name);
    }

    [Fact]
    public async Task AddTaskViewerServerInfrastructure_UpgradesLegacyQueueSchema()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-legacy-di-{Guid.NewGuid():N}.sqlite");

        await CreateLegacyQueueSchemaAsync(dbPath);

        var services = new ServiceCollection();
        services.AddSingleton(
            new AppRuntimeSettings(
                new ViewerRuntimeSettings("127.0.0.1", 8080),
                new OpenCodeRuntimeSettings("http://localhost:4096", "opencode", "secret"),
                new SonarQubeRuntimeSettings("http://sonar.local", "token", SonarQubeMode.Real),
                new OrchestrationRuntimeSettings(
                    dbPath,
                    MaxActive: 1,
                    PerProjectMaxActive: 1,
                    PollMs: 1000,
                    LeaseSeconds: 180,
                    MaxAttempts: 3,
                    MaxWorkingGlobal: 5,
                    WorkingResumeBelow: 3)));

        services
            .AddTaskViewerServerInfrastructure()
            .AddTaskViewerServerApplication();

        await using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<SonarOrchestrator>();

        Assert.NotNull(orchestrator);

        await using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString);
        await conn.OpenAsync();

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(queue_items)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(1));
        }

        Assert.Contains("priority_score", columns);
        Assert.Contains("task_key", columns);
        Assert.Contains("task_unit", columns);
        Assert.Contains("issue_count", columns);
        Assert.Contains("lock_key", columns);
        Assert.Contains("instructions_snapshot", columns);
        Assert.Contains("lease_owner", columns);
        Assert.Contains("lease_heartbeat_at", columns);
        Assert.Contains("lease_expires_at", columns);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT lock_key FROM queue_items WHERE id = 1";
            var value = (string?)await cmd.ExecuteScalarAsync();
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.Contains("gamma-key", value, StringComparison.Ordinal);
            Assert.Contains("javascript:S1126", value, StringComparison.Ordinal);
        }
    }

    static async Task CreateLegacyQueueSchemaAsync(string dbPath)
    {
        await using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE queue_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  issue_key TEXT NOT NULL,
  mapping_id INTEGER NOT NULL,
  sonar_project_key TEXT NOT NULL,
  directory TEXT NOT NULL,
  branch TEXT,
  issue_type TEXT,
  severity TEXT,
  rule TEXT,
  message TEXT,
  component TEXT,
  relative_path TEXT,
  absolute_path TEXT,
  lock_key TEXT,
  line INTEGER,
  issue_status TEXT,
  state TEXT NOT NULL,
  attempt_count INTEGER NOT NULL DEFAULT 0,
  max_attempts INTEGER NOT NULL DEFAULT 3,
  next_attempt_at TEXT,
  session_id TEXT,
  open_code_url TEXT,
  last_error TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  dispatched_at TEXT,
  completed_at TEXT,
  cancelled_at TEXT
);

INSERT INTO queue_items (
  issue_key, mapping_id, sonar_project_key, directory, branch,
  issue_type, severity, rule, message, component, relative_path, absolute_path,
  lock_key, line, issue_status, state, attempt_count, max_attempts,
  created_at, updated_at
) VALUES (
  'legacy-1', 1, 'gamma-key', 'C:/Work/Gamma', 'main',
  'CODE_SMELL', 'MAJOR', 'javascript:S1126', 'Legacy row', 'gamma-key:src/file.js', 'src/file.js', 'C:/Work/Gamma/src/file.js',
  'gamma-key|main|src/file.js|javascript:S1126', 42, 'OPEN', 'queued', 0, 3,
  '2026-03-01T00:00:00Z', '2026-03-01T00:00:00Z'
);";
        await cmd.ExecuteNonQueryAsync();
    }
}
