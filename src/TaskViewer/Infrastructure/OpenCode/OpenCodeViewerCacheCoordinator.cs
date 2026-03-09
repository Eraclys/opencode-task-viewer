using TaskViewer.Application.Sessions;
using TaskViewer.OpenCode;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeViewerCacheCoordinator
{
    readonly OpenCodeViewerCachePolicy _cachePolicy;
    readonly OpenCodeViewerState _viewerState;

    public OpenCodeViewerCacheCoordinator(OpenCodeViewerState viewerState, OpenCodeViewerCachePolicy cachePolicy)
    {
        _cachePolicy = cachePolicy;
        _viewerState = viewerState;
    }

    public List<OpenCodeSessionDto>? GetFreshSessions()
        => _viewerState.GetFreshSessions();

    public List<OpenCodeSessionDto> GetSessionSnapshot()
        => _viewerState.GetSessionSnapshot();

    public void StoreSessions(List<OpenCodeSessionDto> sessions, DateTimeOffset timestamp)
        => _viewerState.StoreSessions(sessions, _cachePolicy.SessionsCacheTtlMs);

    public bool TryGetSessionInfo(string sessionId, out OpenCodeSessionDto? session)
        => _viewerState.TryGetSessionInfo(sessionId, out session);

    public void InvalidateSessionsList()
        => _viewerState.InvalidateSessionsList();

    public List<OpenCodeProject>? GetFreshProjects()
        => _viewerState.GetFreshProjects();

    public void StoreProjects(List<OpenCodeProject> projects, DateTimeOffset timestamp)
        => _viewerState.StoreProjects(projects, _cachePolicy.ProjectsCacheTtlMs);

    public bool TryGetFreshStatusMap(string directoryKey, out Dictionary<string, SessionRuntimeStatus> statusMap)
        => _viewerState.TryGetFreshStatusMap(directoryKey, out statusMap);

    public void StoreStatusMap(string directoryKey, Dictionary<string, SessionRuntimeStatus> statusMap, DateTimeOffset timestamp)
        => _viewerState.StoreStatusMap(directoryKey, statusMap, _cachePolicy.StatusCacheTtlMs);

    public bool TryGetFreshTodos(string? directory, string sessionId, out List<SessionTodoDto> todos)
        => _viewerState.TryGetFreshTodos(directory, sessionId, out todos);

    public void StoreTodos(string? directory, string sessionId, List<SessionTodoDto> todos, DateTimeOffset timestamp)
        => _viewerState.StoreTodos(directory, sessionId, todos, _cachePolicy.TodoCacheTtlMs);

    public bool TryGetFreshSessionsForDirectory(string directoryKey, out List<OpenCodeSessionDto> sessions)
        => _viewerState.TryGetFreshSessionsForDirectory(directoryKey, out sessions);

    public void StoreSessionsForDirectory(string directoryKey, List<OpenCodeSessionDto> sessions, DateTimeOffset timestamp)
        => _viewerState.StoreSessionsForDirectory(directoryKey, sessions, _cachePolicy.DirectorySessionsCacheTtlMs);

    public bool TryGetFreshAssistantPresence(string sessionId, out bool? hasAssistantResponse)
        => _viewerState.TryGetFreshAssistantPresence(sessionId, out hasAssistantResponse);

    public Task<bool?> GetOrAddAssistantPresenceInFlight(string sessionId, Func<string, Task<bool?>> valueFactory)
        => _viewerState.GetOrAddAssistantPresenceInFlight(sessionId, valueFactory);

    public void CompleteAssistantPresenceLookup(string sessionId, bool? result, DateTimeOffset timestamp)
        => _viewerState.CompleteAssistantPresenceLookup(sessionId, result, _cachePolicy.MessagePresenceCacheTtlMs);

    public List<GlobalViewerTaskDto>? GetFreshAllTasks()
        => _viewerState.GetFreshAllTasks();

    public bool HasFreshTaskOverview()
        => _viewerState.HasFreshTaskOverview();

    public void StoreAllTasks(List<GlobalViewerTaskDto> tasks, DateTimeOffset timestamp)
        => _viewerState.StoreAllTasks(tasks, _cachePolicy.TasksAllCacheTtlMs);

    public void InvalidateTaskOverview()
        => _viewerState.InvalidateTaskOverview();

    public void InvalidateAllCaches()
        => _viewerState.InvalidateAllCaches();

    public void InvalidateTodos(string? directory, string sessionId)
        => _viewerState.InvalidateTodos(directory, sessionId);

    public void ClearAssistantPresence()
        => _viewerState.ClearAssistantPresence();

    public void NoteStatusOverride(string? directory, string sessionId, string type)
        => _viewerState.NoteStatusOverride(directory, sessionId, type, _cachePolicy.StatusOverrideTtlMs);

    public bool TryGetRecentStatusOverride(string? directory, string sessionId, out string type)
        => _viewerState.TryGetRecentStatusOverride(directory, sessionId, out type);
}
