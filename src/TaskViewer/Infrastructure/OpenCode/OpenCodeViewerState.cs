using System.Collections.Concurrent;
using TaskViewer.Application.Sessions;
using TaskViewer.OpenCode;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeViewerState
{
    readonly CacheLock _projectsSync = new();
    readonly CacheLock _sessionsSync = new();
    readonly CacheLock _tasksSync = new();
    SessionCache _sessions = new();
    ProjectsCache _projects = new();

    readonly ConcurrentDictionary<string, Task<bool?>> _assistantPresenceInFlight = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, TimestampedValue<bool>> _assistantPresenceCache = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, TimestampedValue<Dictionary<string, SessionRuntimeStatus>>> _statusByDirectory = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, (string Type, DateTimeOffset Timestamp)> _statusOverrides = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, TimestampedValue<List<OpenCodeSessionDto>>> _sessionsByDirectory = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, TimestampedValue<List<SessionTodoDto>>> _todoBySession = new(StringComparer.OrdinalIgnoreCase);
    TimestampedValue<List<GlobalViewerTaskDto>> _tasksAll = new(DateTimeOffset.MinValue, []);

    public List<OpenCodeSessionDto>? GetFreshSessions(int sessionsCacheTtlMs)
    {
        lock (_sessionsSync)
        {
            if (_sessions.Data.Count == 0 ||
                (DateTimeOffset.UtcNow - _sessions.Timestamp).TotalMilliseconds >= sessionsCacheTtlMs)
                return null;

            return [.. _sessions.Data];
        }
    }

    public List<OpenCodeSessionDto> GetSessionSnapshot()
    {
        lock (_sessionsSync)
            return [.. _sessions.Data];
    }

    public void StoreSessions(List<OpenCodeSessionDto> sessions, DateTimeOffset timestamp)
    {
        lock (_sessionsSync)
        {
            _sessions = new SessionCache
            {
                Timestamp = timestamp,
                Data = sessions,
                ById = sessions.ToDictionary(session => session.Id, session => session, StringComparer.Ordinal)
            };
        }
    }

    public bool TryGetSessionInfo(string sessionId, out OpenCodeSessionDto? session)
    {
        lock (_sessionsSync)
        {
            if (_sessions.ById.TryGetValue(sessionId, out var cached))
            {
                session = cached;
                return true;
            }
        }

        session = null;
        return false;
    }

    public void InvalidateSessionsList()
    {
        lock (_sessionsSync)
            _sessions.Timestamp = DateTimeOffset.MinValue;
    }

    public List<OpenCodeProjectTransport>? GetFreshProjects(int projectsCacheTtlMs)
    {
        lock (_projectsSync)
        {
            if (_projects.Data.Count == 0 ||
                (DateTimeOffset.UtcNow - _projects.Timestamp).TotalMilliseconds >= projectsCacheTtlMs)
                return null;

            return [.. _projects.Data];
        }
    }

    public void StoreProjects(List<OpenCodeProjectTransport> projects, DateTimeOffset timestamp)
    {
        lock (_projectsSync)
        {
            _projects = new ProjectsCache
            {
                Timestamp = timestamp,
                Data = projects
            };
        }
    }

    public bool TryGetFreshStatusMap(string directoryKey, int statusCacheTtlMs, out Dictionary<string, SessionRuntimeStatus> statusMap)
    {
        if (_statusByDirectory.TryGetValue(directoryKey, out var cached) &&
            (DateTimeOffset.UtcNow - cached.Timestamp).TotalMilliseconds < statusCacheTtlMs)
        {
            statusMap = new Dictionary<string, SessionRuntimeStatus>(cached.Value, StringComparer.Ordinal);
            return true;
        }

        statusMap = new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);
        return false;
    }

    public void StoreStatusMap(string directoryKey, Dictionary<string, SessionRuntimeStatus> statusMap, DateTimeOffset timestamp)
        => _statusByDirectory[directoryKey] = new TimestampedValue<Dictionary<string, SessionRuntimeStatus>>(timestamp, statusMap);

    public bool TryGetFreshTodos(string? directory, string sessionId, int todoCacheTtlMs, out List<SessionTodoDto> todos)
    {
        var cacheKey = OpenCodeCacheKeys.DirectorySession(directory, sessionId);

        if (_todoBySession.TryGetValue(cacheKey, out var cached) &&
            (DateTimeOffset.UtcNow - cached.Timestamp).TotalMilliseconds < todoCacheTtlMs)
        {
            todos = [.. cached.Value];
            return true;
        }

        todos = [];
        return false;
    }

    public void StoreTodos(string? directory, string sessionId, List<SessionTodoDto> todos, DateTimeOffset timestamp)
    {
        var cacheKey = OpenCodeCacheKeys.DirectorySession(directory, sessionId);
        _todoBySession[cacheKey] = new TimestampedValue<List<SessionTodoDto>>(timestamp, todos);
    }

    public bool TryGetFreshSessionsForDirectory(string directoryKey, int directorySessionsCacheTtlMs, out List<OpenCodeSessionDto> sessions)
    {
        if (_sessionsByDirectory.TryGetValue(directoryKey, out var cached) &&
            (DateTimeOffset.UtcNow - cached.Timestamp).TotalMilliseconds < directorySessionsCacheTtlMs)
        {
            sessions = [.. cached.Value];
            return true;
        }

        sessions = [];
        return false;
    }

    public void StoreSessionsForDirectory(string directoryKey, List<OpenCodeSessionDto> sessions, DateTimeOffset timestamp)
        => _sessionsByDirectory[directoryKey] = new TimestampedValue<List<OpenCodeSessionDto>>(timestamp, sessions);

    public bool TryGetFreshAssistantPresence(string sessionId, int messagePresenceCacheTtlMs, out bool? hasAssistantResponse)
    {
        if (_assistantPresenceCache.TryGetValue(sessionId, out var cached) &&
            (DateTimeOffset.UtcNow - cached.Timestamp).TotalMilliseconds < messagePresenceCacheTtlMs)
        {
            hasAssistantResponse = cached.Value;
            return true;
        }

        hasAssistantResponse = null;
        return false;
    }

    public Task<bool?> GetOrAddAssistantPresenceInFlight(string sessionId, Func<string, Task<bool?>> valueFactory)
        => _assistantPresenceInFlight.GetOrAdd(sessionId, valueFactory);

    public void CompleteAssistantPresenceLookup(string sessionId, bool? result, DateTimeOffset timestamp)
    {
        _assistantPresenceInFlight.TryRemove(sessionId, out _);

        if (result.HasValue)
            _assistantPresenceCache[sessionId] = new TimestampedValue<bool>(timestamp, result.Value);
    }

    public List<GlobalViewerTaskDto>? GetFreshAllTasks(int tasksAllCacheTtlMs)
    {
        lock (_tasksSync)
        {
            if (_tasksAll.Value.Count == 0 ||
                (DateTimeOffset.UtcNow - _tasksAll.Timestamp).TotalMilliseconds >= tasksAllCacheTtlMs)
                return null;

            return [.. _tasksAll.Value];
        }
    }

    public bool HasFreshTaskOverview(int tasksAllCacheTtlMs)
        => GetFreshAllTasks(tasksAllCacheTtlMs) is not null;

    public void StoreAllTasks(List<GlobalViewerTaskDto> tasks, DateTimeOffset timestamp)
    {
        lock (_tasksSync)
            _tasksAll = new TimestampedValue<List<GlobalViewerTaskDto>>(timestamp, tasks);
    }

    public void InvalidateTaskOverview()
    {
        lock (_tasksSync)
            _tasksAll = new TimestampedValue<List<GlobalViewerTaskDto>>(DateTimeOffset.MinValue, []);
    }

    public void InvalidateAllCaches()
    {
        lock (_sessionsSync)
            _sessions = new SessionCache();

        lock (_projectsSync)
            _projects = new ProjectsCache();

        _sessionsByDirectory.Clear();
        _statusByDirectory.Clear();
        _todoBySession.Clear();
        _assistantPresenceCache.Clear();
        _assistantPresenceInFlight.Clear();
        _statusOverrides.Clear();
        InvalidateTaskOverview();
    }

    public void InvalidateTodos(string? directory, string sessionId)
    {
        InvalidateTaskOverview();
        var key = OpenCodeCacheKeys.DirectorySession(directory, sessionId);
        _todoBySession.TryRemove(key, out _);
    }

    public void ClearAssistantPresence()
    {
        _assistantPresenceCache.Clear();
        _assistantPresenceInFlight.Clear();
    }

    public void NoteStatusOverride(string? directory, string sessionId, string type)
    {
        var key = OpenCodeCacheKeys.DirectorySession(directory, sessionId);
        _statusOverrides[key] = (type, DateTimeOffset.UtcNow);
    }

    public bool TryGetRecentStatusOverride(string? directory, string sessionId, int ttlMs, out string type)
    {
        var key = OpenCodeCacheKeys.DirectorySession(directory, sessionId);

        if (_statusOverrides.TryGetValue(key, out var value) &&
            (DateTimeOffset.UtcNow - value.Timestamp).TotalMilliseconds < ttlMs)
        {
            type = value.Type;
            return true;
        }

        type = string.Empty;
        return false;
    }
}
