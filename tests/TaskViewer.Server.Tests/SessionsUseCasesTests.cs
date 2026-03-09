using TaskViewer.Domain.Orchestration;
using TaskViewer.Domain.Sessions;
using TaskViewer.Infrastructure.OpenCode;
using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.Persistence;
using TaskViewer.OpenCode;
using TaskViewer.Persistence;

namespace TaskViewer.Server.Tests;

public sealed class SessionsUseCasesTests
{
    [Fact]
    public async Task GetSessionTasksAsync_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        await using var orchestrator = CreateOrchestrator();

        var sut = CreateUseCases(
            orchestrator,
            findSessionInfo: (_, _) => Task.FromResult<OpenCodeSessionDto?>(null));

        var result = await sut.GetSessionTasksAsync("missing-session");

        Assert.False(result.Found);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task GetLastAssistantMessageAsync_ReturnsMessage_WhenFound()
    {
        await using var orchestrator = CreateOrchestrator();

        var session = new OpenCodeSessionDto("sess-1", "Session One", "C:/Work/Alpha", "C:/Work/Alpha", null, null);

        var sut = CreateUseCases(
            orchestrator,
            findSessionInfo: (_, _) => Task.FromResult<OpenCodeSessionDto?>(session),
            getLastAssistantMessage: (_, _) => Task.FromResult<LastAssistantMessage?>(new LastAssistantMessage("done", DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"))));

        var result = await sut.GetLastAssistantMessageAsync("sess-1");

        Assert.True(result.Found);
        Assert.Equal("sess-1", result.SessionId);
        Assert.Equal("done", result.Message);
    }

    [Fact]
    public async Task ArchiveSessionAsync_InvalidatesCachesAndBroadcasts_WhenFound()
    {
        await using var orchestrator = CreateOrchestrator();

        var session = new OpenCodeSessionDto("sess-1", "Session One", "C:/Work/Alpha", "C:/Work/Alpha", null, null);

        var invalidated = 0;
        var broadcasted = 0;

        var sut = CreateUseCases(
            orchestrator,
            findSessionInfo: (_, _) => Task.FromResult<OpenCodeSessionDto?>(session),
            archiveSessionOnOpenCode: (_, _, _) => Task.FromResult<DateTimeOffset?>(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00")),
            invalidateAllCaches: () => invalidated++,
            broadcastUpdate: () =>
            {
                broadcasted++;

                return Task.CompletedTask;
            });

        var result = await sut.ArchiveSessionAsync("sess-1");

        Assert.True(result.Found);
        Assert.Equal(1, invalidated);
        Assert.Equal(1, broadcasted);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsQueueBackedTasksOnly()
    {
        await using var orchestrator = CreateOrchestrator();

        var mapping = await orchestrator.UpsertMapping(new UpsertMappingRequest(null, "gamma-key", "C:/Work/Gamma", null, true));

        var created = await orchestrator.EnqueueIssues(
            new EnqueueIssuesRequest(
                mapping.Id,
                "CODE_SMELL",
                "Fix safely",
                [new TaskViewer.SonarQube.SonarIssueTransport("sq-1", null, "CODE_SMELL", null, "MAJOR", "javascript:S1126", "Remove this redundant assignment.", "gamma-key:src/worker.js", null, "42", "OPEN")]));

        Assert.Single(created.Items);

        var sut = CreateUseCases(
            orchestrator);

        var result = await sut.ListSessionsAsync(null);

        var item = Assert.Single(result);
        Assert.Equal($"queue-{created.Items[0].Id}", item.Id);
        Assert.Equal("queued", item.RuntimeStatus.Type);
        Assert.Equal("pending", item.Status);
        Assert.True(item.IsQueueItem);
    }

    static SessionsUseCases CreateUseCases(
        SonarOrchestrator orchestrator,
        Func<string, string?, string?>? buildOpenCodeSessionUrl = null,
        Func<QueueItemRecord, SessionSummaryDto?>? mapQueueItemToSessionSummary = null,
        Func<string, CancellationToken, Task<OpenCodeSessionDto?>>? findSessionInfo = null,
        Func<string, string?, CancellationToken, Task<List<SessionTodoDto>>>? getTodosForSession = null,
        Func<List<SessionTodoDto>, List<ViewerTaskDto>>? mapTodosToViewerTasks = null,
        Func<string, CancellationToken, Task<LastAssistantMessage?>>? getLastAssistantMessage = null,
        Func<string, string?, CancellationToken, Task<DateTimeOffset?>>? archiveSessionOnOpenCode = null,
        Action? invalidateAllCaches = null,
        Func<Task>? broadcastUpdate = null)
    {
        return new SessionsUseCases(
            orchestrator,
            mapQueueItemToSessionSummary ?? (new QueueItemSessionSummaryMapper().Map),
            findSessionInfo ??
            ((_, _) => Task.FromResult<OpenCodeSessionDto?>(new OpenCodeSessionDto("sess-1", "Session One", "C:/Work/Alpha", "C:/Work/Alpha", null, null))),
            getTodosForSession ?? ((_, _, _) => Task.FromResult(new List<SessionTodoDto>())),
            mapTodosToViewerTasks ?? (_ => new List<ViewerTaskDto>()),
            getLastAssistantMessage ?? ((_, _) => Task.FromResult<LastAssistantMessage?>(null)),
            archiveSessionOnOpenCode ?? ((_, _, _) => Task.FromResult<DateTimeOffset?>(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"))),
            invalidateAllCaches ?? (() => { }),
            broadcastUpdate ?? (() => Task.CompletedTask));
    }

    static SonarOrchestrator CreateOrchestrator()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-usecases-{Guid.NewGuid():N}.sqlite");

        return new SonarOrchestrator(
            new SonarOrchestratorOptions
            {
                SonarUrl = string.Empty,
                SonarToken = string.Empty,
                DbPath = dbPath,
                Persistence = new SqliteOrchestrationPersistence(dbPath, () => { }),
                MaxActive = 1,
                PerProjectMaxActive = 1,
                PollMs = 1000,
                LeaseSeconds = 180,
                MaxAttempts = 1,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeApiClient = new DisabledOpenCodeService(),
                TaskReadinessGate = new TestTaskReadinessGate(),
                NormalizeDirectory = value => value,
                BuildOpenCodeSessionUrl = (_, _) => null,
                OnChange = () => { }
            });
    }
}
