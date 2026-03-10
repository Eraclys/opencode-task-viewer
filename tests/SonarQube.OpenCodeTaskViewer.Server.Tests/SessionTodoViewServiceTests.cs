using OpenCode.Client;
using SonarQube.OpenCodeTaskViewer.Domain.Sessions;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class SessionTodoViewServiceTests
{
    [Fact]
    public void NormalizeTodo_NormalizesContentStatusAndPriority()
    {
        var sut = new SessionTodoViewService();
        var raw = new OpenCodeTodo("Ship feature", "in_progress", "high");

        var todo = sut.NormalizeTodo(raw);

        Assert.Equal("Ship feature", todo.Content);
        Assert.Equal(ViewerTaskStatus.InProgress, todo.Status);
        Assert.Equal(ViewerTaskStatus.InProgress, todo.TaskStatus);
        Assert.Equal("high", todo.Priority);
    }

    [Fact]
    public void InferInProgressTodoFromRuntime_PromotesFirstPending_WhenRuntimeRunning()
    {
        var sut = new SessionTodoViewService();

        var todos = new List<SessionTodoDto>
        {
            new("Task A", ViewerTaskStatus.Pending, "high"),
            new("Task B", ViewerTaskStatus.Pending, null)
        };

        var inferred = sut.InferInProgressTodoFromRuntime(todos, "busy");

        Assert.Equal(ViewerTaskStatus.InProgress, inferred[0].Status);
        Assert.Equal(ViewerTaskStatus.InProgress, inferred[0].TaskStatus);
        Assert.Equal(ViewerTaskStatus.Pending, inferred[1].Status);
        Assert.Equal(ViewerTaskStatus.Pending, inferred[1].TaskStatus);
    }

    [Fact]
    public void MapTodosToViewerTasks_MapsStableShape()
    {
        var sut = new SessionTodoViewService();

        var todos = new List<SessionTodoDto>
        {
            new("Investigate", ViewerTaskStatus.Pending, "medium")
        };

        var tasks = sut.MapTodosToViewerTasks(todos);

        var task = Assert.Single(tasks);
        Assert.Equal("1", task.Id);
        Assert.Equal("Investigate", task.Subject);
        Assert.Equal("pending", task.Status);
        Assert.Equal("medium", task.Priority);
    }

    [Fact]
    public void MapTodosToGlobalViewerTasks_MapsSessionContext()
    {
        var sut = new SessionTodoViewService();

        var todos = new List<SessionTodoDto>
        {
            new("Fix warning", ViewerTaskStatus.InProgress, null)
        };

        var tasks = sut.MapTodosToGlobalViewerTasks(
            todos,
            "sess-1",
            "My Session",
            "C:/Work/Repo");

        var task = Assert.Single(tasks);
        Assert.Equal("sess-1", task.SessionId);
        Assert.Equal("My Session", task.SessionName);
        Assert.Equal("C:/Work/Repo", task.Project);
    }

    [Theory]
    [InlineData("done", "pending")]
    [InlineData("", "pending")]
    [InlineData("completed", "completed")]
    [InlineData("cancelled", "cancelled")]
    public void ViewerTaskStatus_FromRaw_NormalizesExpectedValues(string raw, string expected)
    {
        var status = ViewerTaskStatus.FromRaw(raw);
        Assert.Equal(expected, status.Value);
    }
}
