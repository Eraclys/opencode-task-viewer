using TaskViewer.OpenCode;
using TaskViewer.Server.Application.Sessions;

namespace TaskViewer.Server.Tests;

public sealed class SessionsUseCasesTests
{
    [Fact]
    public async Task GetSessionTasksAsync_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        await using var orchestrator = CreateOrchestrator();

        var sut = CreateUseCases(
            orchestrator,
            findSessionInfo: _ => Task.FromResult<OpenCodeSessionDto?>(null));

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
            findSessionInfo: _ => Task.FromResult<OpenCodeSessionDto?>(session),
            getLastAssistantMessage: _ => Task.FromResult<LastAssistantMessage?>(new LastAssistantMessage("done", DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"))));

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
            findSessionInfo: _ => Task.FromResult<OpenCodeSessionDto?>(session),
            archiveSessionOnOpenCode: (_, _) => Task.FromResult<DateTimeOffset?>(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00")),
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
    public async Task ListSessionsAsync_UsesDefaultLimitAndReturnsItems()
    {
        await using var orchestrator = CreateOrchestrator();
        string? capturedLimit = null;

        var session = new OpenCodeSessionDto(
            "sess-1",
            "Session One",
            "C:/Work/Alpha",
            "C:/Work/Alpha",
            DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"),
            DateTimeOffset.Parse("2026-01-01T00:00:01.0000000+00:00"));

        var sut = CreateUseCases(
            orchestrator,
            limit =>
            {
                capturedLimit = limit;

                return Task.FromResult(
                    new List<OpenCodeSessionDto>
                    {
                        session
                    });
            },
            getHasAssistantResponse: _ => Task.FromResult<bool?>(false));

        var result = await sut.ListSessionsAsync(null);

        Assert.Equal("20", capturedLimit);
        var item = Assert.Single(result);
        Assert.Equal("sess-1", item.Id);
        Assert.Equal("Session One", item.Name);
        Assert.Equal("idle", item.RuntimeStatus.Type);
        Assert.Equal("pending", item.Status);
    }

    static SessionsUseCases CreateUseCases(
        SonarOrchestrator orchestrator,
        Func<string, Task<List<OpenCodeSessionDto>>>? listGlobalSessions = null,
        Func<string?, Task<Dictionary<string, SessionRuntimeStatus>>>? getStatusMapForDirectory = null,
        Func<string?, string, Dictionary<string, SessionRuntimeStatus>, string>? normalizeRuntimeStatus = null,
        Func<string, Task<bool?>>? getHasAssistantResponse = null,
        Func<string, DateTimeOffset, bool?, string>? deriveSessionKanbanStatus = null,
        Func<string, string?, string?>? buildOpenCodeSessionUrl = null,
        Func<QueueItemRecord, SessionSummaryDto?>? mapQueueItemToSessionSummary = null,
        Func<string, Task<OpenCodeSessionDto?>>? findSessionInfo = null,
        Func<string, string?, Task<List<SessionTodoDto>>>? getTodosForSession = null,
        Func<List<SessionTodoDto>, List<ViewerTaskDto>>? mapTodosToViewerTasks = null,
        Func<string, Task<LastAssistantMessage?>>? getLastAssistantMessage = null,
        Func<string, string?, Task<DateTimeOffset?>>? archiveSessionOnOpenCode = null,
        Action? invalidateAllCaches = null,
        Func<Task>? broadcastUpdate = null)
    {
        return new SessionsUseCases(
            listGlobalSessions ?? (_ => Task.FromResult(new List<OpenCodeSessionDto>())),
            getStatusMapForDirectory ?? (_ => Task.FromResult(new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal))),
            normalizeRuntimeStatus ?? ((_, _, _) => "idle"),
            getHasAssistantResponse ?? (_ => Task.FromResult<bool?>(false)),
            deriveSessionKanbanStatus ?? ((_, _, _) => "pending"),
            buildOpenCodeSessionUrl ?? ((sessionId, _) => $"http://localhost:4096/session/{sessionId}"),
            orchestrator,
            mapQueueItemToSessionSummary ?? (_ => null),
            findSessionInfo ??
            (_ => Task.FromResult<OpenCodeSessionDto?>(new OpenCodeSessionDto("sess-1", "Session One", "C:/Work/Alpha", "C:/Work/Alpha", null, null))),
            getTodosForSession ?? ((_, _) => Task.FromResult(new List<SessionTodoDto>())),
            mapTodosToViewerTasks ?? (_ => new List<ViewerTaskDto>()),
            getLastAssistantMessage ?? (_ => Task.FromResult<LastAssistantMessage?>(null)),
            archiveSessionOnOpenCode ?? ((_, _) => Task.FromResult<DateTimeOffset?>(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"))),
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
                MaxActive = 1,
                PerProjectMaxActive = 1,
                PollMs = 1000,
                LeaseSeconds = 180,
                MaxAttempts = 1,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeStatusReader = new DisabledOpenCodeStatusReader(),
                OpenCodeDispatchClient = new DisabledOpenCodeDispatchClient(),
                TaskReadinessGate = new TestTaskReadinessGate(),
                NormalizeDirectory = value => value,
                BuildOpenCodeSessionUrl = (_, _) => null,
                OnChange = () => { }
            });
    }
}
