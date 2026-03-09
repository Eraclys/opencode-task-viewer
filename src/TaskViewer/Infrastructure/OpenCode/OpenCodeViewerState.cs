using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TaskViewer.Domain.Sessions;
using TaskViewer.OpenCode;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeViewerState
{
    const string SessionsCacheKey = "opencode-viewer:sessions";
    const string ProjectsCacheKey = "opencode-viewer:projects";
    const string TasksAllCacheKey = "opencode-viewer:tasks-all";

    readonly IMemoryCache _cache;
    readonly ConcurrentDictionary<string, Task<bool?>> _assistantPresenceInFlight = new(StringComparer.Ordinal);
    readonly Lock _tokenSync = new();

    CancellationTokenSource _globalInvalidation = new();
    CancellationTokenSource _assistantPresenceInvalidation = new();

    public OpenCodeViewerState()
        : this(new MemoryCache(new MemoryCacheOptions()))
    {
    }

    public OpenCodeViewerState(IMemoryCache cache)
    {
        _cache = cache;
    }

    public List<OpenCodeSessionDto>? GetFreshSessions()
    {
        if (!_cache.TryGetValue<SessionSnapshot>(SessionsCacheKey, out var snapshot) || snapshot is null || snapshot.Data.Count == 0)
            return null;

        return [.. snapshot.Data];
    }

    public List<OpenCodeSessionDto> GetSessionSnapshot()
    {
        return _cache.TryGetValue<SessionSnapshot>(SessionsCacheKey, out var snapshot) && snapshot is not null
            ? [.. snapshot.Data]
            : [];
    }

    public void StoreSessions(List<OpenCodeSessionDto> sessions, int ttlMs)
    {
        if (sessions.Count == 0 || ttlMs <= 0)
        {
            _cache.Remove(SessionsCacheKey);
            return;
        }

        var snapshot = new SessionSnapshot(
            [.. sessions],
            sessions.ToDictionary(session => session.Id, session => session, StringComparer.Ordinal));

        _cache.Set(SessionsCacheKey, snapshot, CreateCacheOptions(ttlMs));
    }

    public bool TryGetSessionInfo(string sessionId, out OpenCodeSessionDto? session)
    {
        if (_cache.TryGetValue<SessionSnapshot>(SessionsCacheKey, out var snapshot) &&
            snapshot is not null &&
            snapshot.ById.TryGetValue(sessionId, out var cached))
        {
            session = cached;
            return true;
        }

        session = null;
        return false;
    }

    public void InvalidateSessionsList() => _cache.Remove(SessionsCacheKey);

    public List<OpenCodeProject>? GetFreshProjects()
    {
        if (!_cache.TryGetValue<List<OpenCodeProject>>(ProjectsCacheKey, out var projects) || projects is null || projects.Count == 0)
            return null;

        return [.. projects];
    }

    public void StoreProjects(List<OpenCodeProject> projects, int ttlMs)
    {
        if (projects.Count == 0 || ttlMs <= 0)
        {
            _cache.Remove(ProjectsCacheKey);
            return;
        }

        _cache.Set<List<OpenCodeProject>>(ProjectsCacheKey, [.. projects], CreateCacheOptions(ttlMs));
    }

    public bool TryGetFreshStatusMap(string directoryKey, out Dictionary<string, SessionRuntimeStatus> statusMap)
    {
        if (_cache.TryGetValue<Dictionary<string, SessionRuntimeStatus>>(StatusMapCacheKey(directoryKey), out var cached) && cached is not null)
        {
            statusMap = new Dictionary<string, SessionRuntimeStatus>(cached, StringComparer.Ordinal);
            return true;
        }

        statusMap = new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);
        return false;
    }

    public void StoreStatusMap(string directoryKey, Dictionary<string, SessionRuntimeStatus> statusMap, int ttlMs)
    {
        if (ttlMs <= 0)
        {
            _cache.Remove(StatusMapCacheKey(directoryKey));
            return;
        }

        _cache.Set(StatusMapCacheKey(directoryKey), statusMap, CreateCacheOptions(ttlMs));
    }

    public bool TryGetFreshTodos(string? directory, string sessionId, out List<SessionTodoDto> todos)
    {
        var cacheKey = TodoCacheKey(directory, sessionId);

        if (_cache.TryGetValue<List<SessionTodoDto>>(cacheKey, out var cached) && cached is not null)
        {
            todos = [.. cached];
            return true;
        }

        todos = [];
        return false;
    }

    public void StoreTodos(string? directory, string sessionId, List<SessionTodoDto> todos, int ttlMs)
    {
        var cacheKey = TodoCacheKey(directory, sessionId);

        if (ttlMs <= 0)
        {
            _cache.Remove(cacheKey);
            return;
        }

        _cache.Set<List<SessionTodoDto>>(cacheKey, [.. todos], CreateCacheOptions(ttlMs));
    }

    public bool TryGetFreshSessionsForDirectory(string directoryKey, out List<OpenCodeSessionDto> sessions)
    {
        if (_cache.TryGetValue<List<OpenCodeSessionDto>>(DirectorySessionsCacheKey(directoryKey), out var cached) && cached is not null)
        {
            sessions = [.. cached];
            return true;
        }

        sessions = [];
        return false;
    }

    public void StoreSessionsForDirectory(string directoryKey, List<OpenCodeSessionDto> sessions, int ttlMs)
    {
        var cacheKey = DirectorySessionsCacheKey(directoryKey);

        if (ttlMs <= 0)
        {
            _cache.Remove(cacheKey);
            return;
        }

        _cache.Set<List<OpenCodeSessionDto>>(cacheKey, [.. sessions], CreateCacheOptions(ttlMs));
    }

    public bool TryGetFreshAssistantPresence(string sessionId, out bool? hasAssistantResponse)
    {
        if (_cache.TryGetValue<bool>(AssistantPresenceCacheKey(sessionId), out var cached))
        {
            hasAssistantResponse = cached;
            return true;
        }

        hasAssistantResponse = null;
        return false;
    }

    public Task<bool?> GetOrAddAssistantPresenceInFlight(string sessionId, Func<string, Task<bool?>> valueFactory)
        => _assistantPresenceInFlight.GetOrAdd(sessionId, valueFactory);

    public void CompleteAssistantPresenceLookup(string sessionId, bool? result, int ttlMs)
    {
        _assistantPresenceInFlight.TryRemove(sessionId, out _);

        if (result.HasValue && ttlMs > 0)
            _cache.Set(AssistantPresenceCacheKey(sessionId), result.Value, CreateCacheOptions(ttlMs, GetAssistantPresenceToken()));
    }

    public List<GlobalViewerTaskDto>? GetFreshAllTasks()
    {
        if (!_cache.TryGetValue<List<GlobalViewerTaskDto>>(TasksAllCacheKey, out var tasks) || tasks is null || tasks.Count == 0)
            return null;

        return [.. tasks];
    }

    public bool HasFreshTaskOverview() => GetFreshAllTasks() is not null;

    public void StoreAllTasks(List<GlobalViewerTaskDto> tasks, int ttlMs)
    {
        if (tasks.Count == 0 || ttlMs <= 0)
        {
            _cache.Remove(TasksAllCacheKey);
            return;
        }

        _cache.Set<List<GlobalViewerTaskDto>>(TasksAllCacheKey, [.. tasks], CreateCacheOptions(ttlMs));
    }

    public void InvalidateTaskOverview() => _cache.Remove(TasksAllCacheKey);

    public void InvalidateAllCaches()
    {
        ResetGlobalInvalidation();
        ResetAssistantPresenceInvalidation();
        _assistantPresenceInFlight.Clear();
    }

    public void InvalidateTodos(string? directory, string sessionId)
    {
        InvalidateTaskOverview();
        _cache.Remove(TodoCacheKey(directory, sessionId));
    }

    public void ClearAssistantPresence()
    {
        ResetAssistantPresenceInvalidation();
        _assistantPresenceInFlight.Clear();
    }

    public void NoteStatusOverride(string? directory, string sessionId, string type, int ttlMs)
    {
        var cacheKey = StatusOverrideCacheKey(directory, sessionId);

        if (ttlMs <= 0)
        {
            _cache.Remove(cacheKey);
            return;
        }

        _cache.Set(cacheKey, type, CreateCacheOptions(ttlMs));
    }

    public bool TryGetRecentStatusOverride(string? directory, string sessionId, out string type)
    {
        if (_cache.TryGetValue<string>(StatusOverrideCacheKey(directory, sessionId), out var cached) && cached is not null)
        {
            type = cached;
            return true;
        }

        type = string.Empty;
        return false;
    }

    MemoryCacheEntryOptions CreateCacheOptions(int ttlMs, IChangeToken? additionalToken = null)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(ttlMs)
        };

        options.AddExpirationToken(GetGlobalToken());

        if (additionalToken is not null)
            options.AddExpirationToken(additionalToken);

        return options;
    }

    IChangeToken GetGlobalToken()
    {
        lock (_tokenSync)
            return new CancellationChangeToken(_globalInvalidation.Token);
    }

    IChangeToken GetAssistantPresenceToken()
    {
        lock (_tokenSync)
            return new CancellationChangeToken(_assistantPresenceInvalidation.Token);
    }

    void ResetGlobalInvalidation()
    {
        CancellationTokenSource toCancel;

        lock (_tokenSync)
        {
            toCancel = _globalInvalidation;
            _globalInvalidation = new CancellationTokenSource();
        }

        toCancel.Cancel();
        toCancel.Dispose();
    }

    void ResetAssistantPresenceInvalidation()
    {
        CancellationTokenSource toCancel;

        lock (_tokenSync)
        {
            toCancel = _assistantPresenceInvalidation;
            _assistantPresenceInvalidation = new CancellationTokenSource();
        }

        toCancel.Cancel();
        toCancel.Dispose();
    }

    static string StatusMapCacheKey(string directoryKey) => $"opencode-viewer:status:{directoryKey}";
    static string TodoCacheKey(string? directory, string sessionId) => $"opencode-viewer:todos:{OpenCodeCacheKeys.DirectorySession(directory, sessionId)}";
    static string DirectorySessionsCacheKey(string directoryKey) => $"opencode-viewer:directory-sessions:{directoryKey}";
    static string AssistantPresenceCacheKey(string sessionId) => $"opencode-viewer:assistant:{sessionId}";
    static string StatusOverrideCacheKey(string? directory, string sessionId) => $"opencode-viewer:status-override:{OpenCodeCacheKeys.DirectorySession(directory, sessionId)}";

    sealed record SessionSnapshot(List<OpenCodeSessionDto> Data, Dictionary<string, OpenCodeSessionDto> ById);
}
