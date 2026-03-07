using System.Text.Json.Nodes;
using TaskViewer.Server;
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
            findSessionInfo: _ => Task.FromResult<JsonObject?>(null));

        var result = await sut.GetSessionTasksAsync("missing-session");

        Assert.False(result.Found);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task GetLastAssistantMessageAsync_ReturnsMessage_WhenFound()
    {
        await using var orchestrator = CreateOrchestrator();
        var session = new JsonObject { ["id"] = "sess-1", ["directory"] = "C:/Work/Alpha" };

        var sut = CreateUseCases(
            orchestrator,
            findSessionInfo: _ => Task.FromResult<JsonObject?>(session),
            getLastAssistantMessage: _ => Task.FromResult<LastAssistantMessage?>(new LastAssistantMessage("done", "2026-01-01T00:00:00.0000000+00:00")));

        var result = await sut.GetLastAssistantMessageAsync("sess-1");

        Assert.True(result.Found);
        Assert.Equal("sess-1", result.SessionId);
        Assert.Equal("done", result.Message);
    }

    [Fact]
    public async Task ArchiveSessionAsync_InvalidatesCachesAndBroadcasts_WhenFound()
    {
        await using var orchestrator = CreateOrchestrator();
        var session = new JsonObject { ["id"] = "sess-1", ["directory"] = "C:/Work/Alpha" };
        var invalidated = 0;
        var broadcasted = 0;

        var sut = CreateUseCases(
            orchestrator,
            findSessionInfo: _ => Task.FromResult<JsonObject?>(session),
            archiveSessionOnOpenCode: (_, _) => Task.FromResult<string?>("2026-01-01T00:00:00.0000000+00:00"),
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

        var session = new JsonObject
        {
            ["id"] = "sess-1",
            ["title"] = "Session One",
            ["directory"] = "C:/Work/Alpha",
            ["project"] = new JsonObject { ["worktree"] = "C:/Work/Alpha" },
            ["time"] = new JsonObject
            {
                ["created"] = "2026-01-01T00:00:00.0000000+00:00",
                ["updated"] = "2026-01-01T00:00:01.0000000+00:00"
            }
        };

        var sut = CreateUseCases(
            orchestrator,
            listGlobalSessions: limit =>
            {
                capturedLimit = limit;
                return Task.FromResult(new List<JsonObject> { session });
            },
            getHasAssistantResponse: _ => Task.FromResult<bool?>(false));

        var result = await sut.ListSessionsAsync(null);

        Assert.Equal("20", capturedLimit);
        Assert.Single(result);
    }

    private static SessionsUseCases CreateUseCases(
        SonarOrchestrator orchestrator,
        Func<string, Task<List<JsonObject>>>? listGlobalSessions = null,
        Func<string?, Task<Dictionary<string, JsonObject>>>? getStatusMapForDirectory = null,
        Func<JsonObject?, string?>? getSessionDirectory = null,
        Func<JsonObject?, string?>? getProjectDisplayPath = null,
        Func<string?, string, Dictionary<string, JsonObject>, string>? normalizeRuntimeStatus = null,
        Func<string, Task<bool?>>? getHasAssistantResponse = null,
        Func<string, string, bool?, string>? deriveSessionKanbanStatus = null,
        Func<string, string?, string?>? buildOpenCodeSessionUrl = null,
        Func<string?, string?>? parseTime = null,
        Func<QueueItemRecord, object?>? mapQueueItemToSessionSummary = null,
        Func<string, Task<JsonObject?>>? findSessionInfo = null,
        Func<string, string?, Task<List<JsonObject>>>? getTodosForSession = null,
        Func<List<JsonObject>, List<object>>? mapTodosToViewerTasks = null,
        Func<string, Task<LastAssistantMessage?>>? getLastAssistantMessage = null,
        Func<string, string?, Task<string?>>? archiveSessionOnOpenCode = null,
        Action? invalidateAllCaches = null,
        Func<Task>? broadcastUpdate = null)
    {
        return new SessionsUseCases(
            listGlobalSessions: listGlobalSessions ?? (_ => Task.FromResult(new List<JsonObject>())),
            getStatusMapForDirectory: getStatusMapForDirectory ?? (_ => Task.FromResult(new Dictionary<string, JsonObject>(StringComparer.Ordinal))),
            getSessionDirectory: getSessionDirectory ?? (session => session?["directory"]?.ToString()),
            getProjectDisplayPath: getProjectDisplayPath ?? (session => session?["project"]?["worktree"]?.ToString()),
            normalizeRuntimeStatus: normalizeRuntimeStatus ?? ((_, _, _) => "idle"),
            getHasAssistantResponse: getHasAssistantResponse ?? (_ => Task.FromResult<bool?>(false)),
            deriveSessionKanbanStatus: deriveSessionKanbanStatus ?? ((_, _, _) => "pending"),
            buildOpenCodeSessionUrl: buildOpenCodeSessionUrl ?? ((sessionId, _) => $"http://localhost:4096/session/{sessionId}"),
            parseTime: parseTime ?? (value => value),
            orchestrator: orchestrator,
            mapQueueItemToSessionSummary: mapQueueItemToSessionSummary ?? (_ => null),
            findSessionInfo: findSessionInfo ?? (_ => Task.FromResult<JsonObject?>(new JsonObject { ["directory"] = "C:/Work/Alpha" })),
            getTodosForSession: getTodosForSession ?? ((_, _) => Task.FromResult(new List<JsonObject>())),
            mapTodosToViewerTasks: mapTodosToViewerTasks ?? (_ => new List<object>()),
            getLastAssistantMessage: getLastAssistantMessage ?? (_ => Task.FromResult<LastAssistantMessage?>(null)),
            archiveSessionOnOpenCode: archiveSessionOnOpenCode ?? ((_, _) => Task.FromResult<string?>("2026-01-01T00:00:00.0000000+00:00")),
            invalidateAllCaches: invalidateAllCaches ?? (() => { }),
            broadcastUpdate: broadcastUpdate ?? (() => Task.CompletedTask));
    }

    private static SonarOrchestrator CreateOrchestrator()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-usecases-{Guid.NewGuid():N}.sqlite");

        return new SonarOrchestrator(
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
                OpenCodeFetch = (_, _) => Task.FromResult<JsonNode?>(null),
                NormalizeDirectory = value => value,
                BuildOpenCodeSessionUrl = (_, _) => null,
                OnChange = () => { }
            });
    }
}
