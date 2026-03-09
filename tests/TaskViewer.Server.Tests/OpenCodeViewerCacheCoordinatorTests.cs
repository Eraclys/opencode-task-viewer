using TaskViewer.Application.Sessions;
using TaskViewer.Infrastructure.OpenCode;
using TaskViewer.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class OpenCodeViewerCacheCoordinatorTests
{
    [Fact]
    public void TaskOverviewCache_ReportsFreshnessAndInvalidates()
    {
        var sut = CreateSut();

        sut.StoreAllTasks(
            [
                new GlobalViewerTaskDto
                {
                    Id = "task-1",
                    Subject = "todo",
                    Status = "open",
                    SessionId = "sess-1"
                }
            ],
            DateTimeOffset.UtcNow);

        Assert.True(sut.HasFreshTaskOverview());
        Assert.Single(sut.GetFreshAllTasks()!);

        sut.InvalidateTaskOverview();

        Assert.False(sut.HasFreshTaskOverview());
        Assert.Null(sut.GetFreshAllTasks());
    }

    [Fact]
    public void TodoInvalidation_ClearsSessionTodosAndTaskOverview()
    {
        var sut = CreateSut();

        sut.StoreTodos("C:/Work", "sess-1", [new SessionTodoDto("todo-1", "open", "high")], DateTimeOffset.UtcNow);
        sut.StoreAllTasks(
            [
                new GlobalViewerTaskDto
                {
                    Id = "task-1",
                    Subject = "todo",
                    Status = "open",
                    SessionId = "sess-1"
                }
            ],
            DateTimeOffset.UtcNow);

        sut.InvalidateTodos("C:/Work", "sess-1");

        Assert.False(sut.TryGetFreshTodos("C:/Work", "sess-1", out _));
        Assert.False(sut.HasFreshTaskOverview());
    }

    [Fact]
    public void InvalidateAllCaches_ClearsEachCacheFamily()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        var directoryKey = OpenCodeCacheKeys.Directory("C:/Work");

        Assert.NotNull(directoryKey);

        sut.StoreSessions(
            [
                new OpenCodeSessionDto("sess-1", "Session", "C:/Work", null, now, now)
            ],
            now);
        sut.StoreProjects([new OpenCodeProject("C:/Work", ["C:/Work"])], now);
        sut.StoreStatusMap(
            directoryKey,
            new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal)
            {
                ["sess-1"] = new SessionRuntimeStatus("working")
            },
            now);
        sut.StoreSessionsForDirectory(
            directoryKey,
            [
                new OpenCodeSessionDto("sess-1", "Session", "C:/Work", null, now, now)
            ],
            now);
        sut.StoreTodos("C:/Work", "sess-1", [new SessionTodoDto("todo-1", "open", null)], now);
        sut.CompleteAssistantPresenceLookup("sess-1", true, now);
        sut.StoreAllTasks(
            [
                new GlobalViewerTaskDto
                {
                    Id = "task-1",
                    Subject = "todo",
                    Status = "open",
                    SessionId = "sess-1"
                }
            ],
            now);
        sut.NoteStatusOverride("C:/Work", "sess-1", "working");

        sut.InvalidateAllCaches();

        Assert.Null(sut.GetFreshSessions());
        Assert.Null(sut.GetFreshProjects());
        Assert.False(sut.TryGetFreshStatusMap(directoryKey, out _));
        Assert.False(sut.TryGetFreshSessionsForDirectory(directoryKey, out _));
        Assert.False(sut.TryGetFreshTodos("C:/Work", "sess-1", out _));
        Assert.False(sut.TryGetFreshAssistantPresence("sess-1", out _));
        Assert.False(sut.TryGetRecentStatusOverride("C:/Work", "sess-1", out _));
        Assert.Null(sut.GetFreshAllTasks());
    }

    [Fact]
    public void TaskOverviewCache_UsesCoordinatorPolicyTtl()
    {
        var sut = CreateSut(
            new OpenCodeViewerCachePolicy
            {
                TasksAllCacheTtlMs = 0
            });

        sut.StoreAllTasks(
            [
                new GlobalViewerTaskDto
                {
                    Id = "task-1",
                    Subject = "todo",
                    Status = "open",
                    SessionId = "sess-1"
                }
            ],
            DateTimeOffset.UtcNow);

        Assert.False(sut.HasFreshTaskOverview());
        Assert.Null(sut.GetFreshAllTasks());
    }

    static OpenCodeViewerCacheCoordinator CreateSut(OpenCodeViewerCachePolicy? cachePolicy = null)
        => new(new OpenCodeViewerState(), cachePolicy ?? new OpenCodeViewerCachePolicy());
}
