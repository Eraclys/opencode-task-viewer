using System.Globalization;
using OpenCode.Client;

namespace SonarQube.OpenCodeTaskViewer.Domain.Sessions;

public sealed class SessionTodoViewService
{
    public SessionTodoDto NormalizeTodo(OpenCodeTodo todo)
    {
        var status = ViewerTaskStatus.FromRaw(todo.Status);

        return new SessionTodoDto(
            todo.Content,
            status,
            todo.Priority);
    }

    public List<SessionTodoDto> InferInProgressTodoFromRuntime(List<SessionTodoDto> todos, string runtimeType)
    {
        if (!SessionStatusPolicy.IsRuntimeRunning(runtimeType))
            return todos;

        if (todos.Any(t => t.TaskStatus.IsInProgress))
            return todos;

        var idx = todos.FindIndex(t => t.TaskStatus.IsPending);

        if (idx < 0)
            return todos;

        var copy = todos.ToList();

        copy[idx] = copy[idx] with
        {
            Status = ViewerTaskStatus.InProgress
        };

        return copy;
    }

    public List<ViewerTaskDto> MapTodosToViewerTasks(List<SessionTodoDto> todos)
    {
        var tasks = new List<ViewerTaskDto>();

        for (var i = 0; i < todos.Count; i++)
        {
            var todo = todos[i];

            tasks.Add(
                new ViewerTaskDto
                {
                    Id = (i + 1).ToString(CultureInfo.InvariantCulture),
                    Subject = todo.Content,
                    Status = todo.TaskStatus.Value,
                    Priority = todo.Priority
                });
        }

        return tasks;
    }

    public List<GlobalViewerTaskDto> MapTodosToGlobalViewerTasks(
        List<SessionTodoDto> todos,
        string sessionId,
        string? sessionName,
        string? project)
    {
        var tasks = new List<GlobalViewerTaskDto>();

        for (var i = 0; i < todos.Count; i++)
        {
            var todo = todos[i];

            tasks.Add(
                new GlobalViewerTaskDto
                {
                    Id = (i + 1).ToString(CultureInfo.InvariantCulture),
                    Subject = todo.Content,
                    Status = todo.TaskStatus.Value,
                    Priority = todo.Priority,
                    SessionId = sessionId,
                    SessionName = sessionName,
                    Project = project
                });
        }

        return tasks;
    }
}
