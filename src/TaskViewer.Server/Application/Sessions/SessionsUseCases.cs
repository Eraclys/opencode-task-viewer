using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Domain;

namespace TaskViewer.Server.Application.Sessions;

public sealed class SessionsUseCases : ISessionsUseCases
{
    private readonly Func<string, Task<List<JsonObject>>> _listGlobalSessions;
    private readonly Func<string?, Task<Dictionary<string, JsonObject>>> _getStatusMapForDirectory;
    private readonly Func<JsonObject?, string?> _getSessionDirectory;
    private readonly Func<JsonObject?, string?> _getProjectDisplayPath;
    private readonly Func<string?, string, Dictionary<string, JsonObject>, string> _normalizeRuntimeStatus;
    private readonly Func<string, Task<bool?>> _getHasAssistantResponse;
    private readonly Func<string, string, bool?, string> _deriveSessionKanbanStatus;
    private readonly Func<string, string?, string?> _buildOpenCodeSessionUrl;
    private readonly Func<string?, string?> _parseTime;
    private readonly SonarOrchestrator _orchestrator;
    private readonly Func<QueueItemRecord, object?> _mapQueueItemToSessionSummary;
    private readonly Func<string, Task<JsonObject?>> _findSessionInfo;
    private readonly Func<string, string?, Task<List<JsonObject>>> _getTodosForSession;
    private readonly Func<List<JsonObject>, List<object>> _mapTodosToViewerTasks;
    private readonly Func<string, Task<LastAssistantMessage?>> _getLastAssistantMessage;
    private readonly Func<string, string?, Task<string?>> _archiveSessionOnOpenCode;
    private readonly Action _invalidateAllCaches;
    private readonly Func<Task> _broadcastUpdate;

    public SessionsUseCases(
        Func<string, Task<List<JsonObject>>> listGlobalSessions,
        Func<string?, Task<Dictionary<string, JsonObject>>> getStatusMapForDirectory,
        Func<JsonObject?, string?> getSessionDirectory,
        Func<JsonObject?, string?> getProjectDisplayPath,
        Func<string?, string, Dictionary<string, JsonObject>, string> normalizeRuntimeStatus,
        Func<string, Task<bool?>> getHasAssistantResponse,
        Func<string, string, bool?, string> deriveSessionKanbanStatus,
        Func<string, string?, string?> buildOpenCodeSessionUrl,
        Func<string?, string?> parseTime,
        SonarOrchestrator orchestrator,
        Func<QueueItemRecord, object?> mapQueueItemToSessionSummary,
        Func<string, Task<JsonObject?>> findSessionInfo,
        Func<string, string?, Task<List<JsonObject>>> getTodosForSession,
        Func<List<JsonObject>, List<object>> mapTodosToViewerTasks,
        Func<string, Task<LastAssistantMessage?>> getLastAssistantMessage,
        Func<string, string?, Task<string?>> archiveSessionOnOpenCode,
        Action invalidateAllCaches,
        Func<Task> broadcastUpdate)
    {
        _listGlobalSessions = listGlobalSessions;
        _getStatusMapForDirectory = getStatusMapForDirectory;
        _getSessionDirectory = getSessionDirectory;
        _getProjectDisplayPath = getProjectDisplayPath;
        _normalizeRuntimeStatus = normalizeRuntimeStatus;
        _getHasAssistantResponse = getHasAssistantResponse;
        _deriveSessionKanbanStatus = deriveSessionKanbanStatus;
        _buildOpenCodeSessionUrl = buildOpenCodeSessionUrl;
        _parseTime = parseTime;
        _orchestrator = orchestrator;
        _mapQueueItemToSessionSummary = mapQueueItemToSessionSummary;
        _findSessionInfo = findSessionInfo;
        _getTodosForSession = getTodosForSession;
        _mapTodosToViewerTasks = mapTodosToViewerTasks;
        _getLastAssistantMessage = getLastAssistantMessage;
        _archiveSessionOnOpenCode = archiveSessionOnOpenCode;
        _invalidateAllCaches = invalidateAllCaches;
        _broadcastUpdate = broadcastUpdate;
    }

    public async Task<IReadOnlyList<object>> ListSessionsAsync(string? limitParam)
    {
        var requestedLimit = string.IsNullOrWhiteSpace(limitParam) ? "20" : limitParam;
        var globalSessions = await _listGlobalSessions(requestedLimit);

        var directoriesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in globalSessions)
        {
            var directory = _getSessionDirectory(session);
            var key = DirectoryPath.GetCacheKey(directory);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!directoriesByKey.ContainsKey(key))
                directoriesByKey[key] = directory!;
        }

        var statusByDirectory = new Dictionary<string, Dictionary<string, JsonObject>>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directoriesByKey.Values)
            statusByDirectory[DirectoryPath.GetCacheKey(directory)] = await _getStatusMapForDirectory(directory);

        var summaries = new List<object>();
        var semaphore = new SemaphoreSlim(6);

        var tasks = globalSessions.Select(async session =>
        {
            await semaphore.WaitAsync();
            try
            {
                var sessionId = session["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(sessionId))
                    return;

                var directory = _getSessionDirectory(session);
                var statusMap = statusByDirectory.GetValueOrDefault(DirectoryPath.GetCacheKey(directory))
                    ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal);

                var runtimeStatus = _normalizeRuntimeStatus(directory, sessionId, statusMap);
                var createdAt = _parseTime(session["time"]?["created"]?.ToString());
                var modifiedAt = _parseTime(session["time"]?["updated"]?.ToString())
                    ?? createdAt
                    ?? DateTimeOffset.UtcNow.ToString("O");

                var hasAssistantResponse = SessionStatusPolicy.IsRuntimeRunning(runtimeStatus)
                    ? null
                    : await _getHasAssistantResponse(sessionId);

                var status = _deriveSessionKanbanStatus(runtimeStatus, modifiedAt, hasAssistantResponse);

                lock (summaries)
                {
                    summaries.Add(new
                    {
                        id = sessionId,
                        name = session["title"]?.ToString() ?? session["name"]?.ToString(),
                        project = _getProjectDisplayPath(session),
                        description = (string?)null,
                        gitBranch = (string?)null,
                        createdAt,
                        modifiedAt,
                        runtimeStatus = new { type = runtimeStatus },
                        status,
                        hasAssistantResponse,
                        openCodeUrl = _buildOpenCodeSessionUrl(sessionId, directory)
                    });
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        try
        {
            var queueItems = await _orchestrator.ListQueue("queued,dispatching", 1000);
            var queueSummaries = queueItems.Select(_mapQueueItemToSessionSummary).Where(x => x is not null).Cast<object>();
            summaries.AddRange(queueSummaries);
        }
        catch (Exception queueError)
        {
            Console.Error.WriteLine($"Error loading orchestrator queue for session list: {queueError}");
        }

        var sorted = summaries
            .OrderByDescending(s => _parseTime((string?)s.GetType().GetProperty("modifiedAt")?.GetValue(s) ?? string.Empty))
            .ToList();

        return sorted;
    }

    public async Task<SessionTasksResult> GetSessionTasksAsync(string sessionId)
    {
        var info = await _findSessionInfo(sessionId);
        if (info is null)
            return new SessionTasksResult(false, []);

        var directory = _getSessionDirectory(info);
        var todos = await _getTodosForSession(sessionId, directory);
        var tasks = _mapTodosToViewerTasks(todos);
        return new SessionTasksResult(true, tasks);
    }

    public async Task<LastAssistantMessageResult> GetLastAssistantMessageAsync(string sessionId)
    {
        var info = await _findSessionInfo(sessionId);
        if (info is null)
            return new LastAssistantMessageResult(false, sessionId, null, null);

        var last = await _getLastAssistantMessage(sessionId);
        return new LastAssistantMessageResult(true, sessionId, last?.Message, last?.CreatedAt);
    }

    public async Task<ArchiveSessionResult> ArchiveSessionAsync(string sessionId)
    {
        var info = await _findSessionInfo(sessionId);
        if (info is null)
            return new ArchiveSessionResult(false, null);

        var directory = _getSessionDirectory(info);
        var archivedAt = await _archiveSessionOnOpenCode(sessionId, directory);
        _invalidateAllCaches();
        await _broadcastUpdate();

        return new ArchiveSessionResult(true, archivedAt);
    }
}
