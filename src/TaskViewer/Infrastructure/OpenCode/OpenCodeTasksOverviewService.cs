using TaskViewer.Domain.Sessions;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeTasksOverviewService
{
    readonly OpenCodeSessionSearchService _sessionSearchService;
    readonly OpenCodeViewerCachePolicy _cachePolicy;
    readonly SessionTodoViewService _sessionTodoViewService;
    readonly OpenCodeViewerState _viewerState;

    public OpenCodeTasksOverviewService(
        OpenCodeSessionSearchService sessionSearchService,
        OpenCodeViewerState viewerState,
        OpenCodeViewerCachePolicy cachePolicy,
        SessionTodoViewService sessionTodoViewService)
    {
        _sessionSearchService = sessionSearchService;
        _viewerState = viewerState;
        _cachePolicy = cachePolicy;
        _sessionTodoViewService = sessionTodoViewService;
    }

    public async Task<List<GlobalViewerTaskDto>> GetAllTasksAsync()
    {
        var cachedTasks = _viewerState.GetFreshAllTasks();

        if (cachedTasks is not null)
            return cachedTasks;

        var sessions = _viewerState.GetSessionSnapshot();

        if (sessions.Count == 0)
        {
            sessions = await _sessionSearchService.ListGlobalSessionsAsync(
                "all",
                TaskViewerRuntimeDefaults.MaxAllSessions,
                TaskViewerRuntimeDefaults.MaxSessionsPerProject);
        }

        var directoriesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            var key = OpenCodeCacheKeys.Directory(session.Directory);

            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(session.Directory) ||
                directoriesByKey.ContainsKey(key))
                continue;

            directoriesByKey[key] = session.Directory;
        }

        var statusByDirectory = new Dictionary<string, Dictionary<string, SessionRuntimeStatus>>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directoriesByKey.Values)
        {
            var directoryKey = OpenCodeCacheKeys.Directory(directory);

            if (!string.IsNullOrWhiteSpace(directoryKey))
                statusByDirectory[directoryKey] = await _sessionSearchService.GetStatusMapForDirectoryAsync(directory);
        }

        var tasks = new List<GlobalViewerTaskDto>();

        foreach (var session in sessions)
        {
            var directoryKey = OpenCodeCacheKeys.Directory(session.Directory);
            var statusMap = !string.IsNullOrWhiteSpace(directoryKey) && statusByDirectory.TryGetValue(directoryKey, out var cachedStatusMap)
                ? cachedStatusMap
                : new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);
            var runtimeStatus = NormalizeRuntimeStatus(session.Directory, session.Id, statusMap);

            List<SessionTodoDto> todos;

            try
            {
                todos = await _sessionSearchService.GetTodosForSessionAsync(session.Id, session.Directory);
            }
            catch
            {
                todos = [];
            }

            var inferred = _sessionTodoViewService.InferInProgressTodoFromRuntime(todos, runtimeStatus);
            tasks.AddRange(_sessionTodoViewService.MapTodosToGlobalViewerTasks(inferred, session.Id, session.Name, session.Project));
        }

        _viewerState.StoreAllTasks(tasks, _cachePolicy.TasksAllCacheTtlMs);
        return tasks;
    }

    string NormalizeRuntimeStatus(string? directory, string sessionId, Dictionary<string, SessionRuntimeStatus> statusMap)
    {
        if (_viewerState.TryGetRecentStatusOverride(directory, sessionId, out var overrideType))
            return overrideType.Type;

        if (statusMap.TryGetValue(sessionId, out var status) &&
            !string.IsNullOrWhiteSpace(status.Type))
            return status.Type;

        return SessionRuntimeStatus.Idle.Type;
    }
}
