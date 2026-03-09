using System.Globalization;
using TaskViewer.Domain;
using TaskViewer.OpenCode;

namespace TaskViewer.Application.Sessions;

public sealed class SessionTodoViewService
{
    public SessionTodoDto NormalizeTodo(OpenCodeTodo todo)
    {
        return new SessionTodoDto(
            todo.Content,
            todo.Status,
            todo.Priority);
    }

    public List<SessionTodoDto> InferInProgressTodoFromRuntime(List<SessionTodoDto> todos, string runtimeType)
    {
        if (!SessionStatusPolicy.IsRuntimeRunning(runtimeType))
            return todos;

        if (todos.Any(t => string.Equals(t.Status, "in_progress", StringComparison.Ordinal)))
            return todos;

        var idx = todos.FindIndex(t => string.Equals(t.Status, "pending", StringComparison.Ordinal));

        if (idx < 0)
            return todos;

        var copy = todos.ToList();
        copy[idx] = copy[idx] with
        {
            Status = "in_progress"
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
                    Status = todo.Status,
                    Priority = todo.Priority
                });
        }

        return tasks;
    }

    public List<GlobalViewerTaskDto> MapTodosToGlobalViewerTasks(List<SessionTodoDto> todos, string sessionId, string? sessionName, string? project)
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
                    Status = todo.Status,
                    Priority = todo.Priority,
                    SessionId = sessionId,
                    SessionName = sessionName,
                    Project = project
                });
        }

        return tasks;
    }
}
