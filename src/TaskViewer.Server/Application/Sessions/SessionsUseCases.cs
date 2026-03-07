using TaskViewer.Server.Domain;

namespace TaskViewer.Server.Application.Sessions;

public sealed class SessionsUseCases : ISessionsUseCases
{
    readonly Func<string, string?, Task<string?>> _archiveSessionOnOpenCode;
    readonly Func<Task> _broadcastUpdate;
    readonly Func<string, string?, string?> _buildOpenCodeSessionUrl;
    readonly Func<string, string, bool?, string> _deriveSessionKanbanStatus;
    readonly Func<string, Task<OpenCodeSessionDto?>> _findSessionInfo;
    readonly Func<string, Task<bool?>> _getHasAssistantResponse;
    readonly Func<string, Task<LastAssistantMessage?>> _getLastAssistantMessage;
    readonly Func<string?, Task<Dictionary<string, SessionRuntimeStatus>>> _getStatusMapForDirectory;
    readonly Func<string, string?, Task<List<SessionTodoDto>>> _getTodosForSession;
    readonly Action _invalidateAllCaches;
    readonly Func<string, Task<List<OpenCodeSessionDto>>> _listGlobalSessions;
    readonly Func<QueueItemRecord, SessionSummaryDto?> _mapQueueItemToSessionSummary;
    readonly Func<List<SessionTodoDto>, List<ViewerTaskDto>> _mapTodosToViewerTasks;
    readonly Func<string?, string, Dictionary<string, SessionRuntimeStatus>, string> _normalizeRuntimeStatus;
    readonly SonarOrchestrator _orchestrator;
    readonly Func<string?, string?> _parseTime;

    public SessionsUseCases(
        Func<string, Task<List<OpenCodeSessionDto>>> listGlobalSessions,
        Func<string?, Task<Dictionary<string, SessionRuntimeStatus>>> getStatusMapForDirectory,
        Func<string?, string, Dictionary<string, SessionRuntimeStatus>, string> normalizeRuntimeStatus,
        Func<string, Task<bool?>> getHasAssistantResponse,
        Func<string, string, bool?, string> deriveSessionKanbanStatus,
        Func<string, string?, string?> buildOpenCodeSessionUrl,
        Func<string?, string?> parseTime,
        SonarOrchestrator orchestrator,
        Func<QueueItemRecord, SessionSummaryDto?> mapQueueItemToSessionSummary,
        Func<string, Task<OpenCodeSessionDto?>> findSessionInfo,
        Func<string, string?, Task<List<SessionTodoDto>>> getTodosForSession,
        Func<List<SessionTodoDto>, List<ViewerTaskDto>> mapTodosToViewerTasks,
        Func<string, Task<LastAssistantMessage?>> getLastAssistantMessage,
        Func<string, string?, Task<string?>> archiveSessionOnOpenCode,
        Action invalidateAllCaches,
        Func<Task> broadcastUpdate)
    {
        _listGlobalSessions = listGlobalSessions;
        _getStatusMapForDirectory = getStatusMapForDirectory;
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

    public async Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(string? limitParam)
    {
        var requestedLimit = string.IsNullOrWhiteSpace(limitParam) ? "20" : limitParam;
        var globalSessions = await _listGlobalSessions(requestedLimit);

        var directoriesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in globalSessions)
        {
            var key = DirectoryPath.GetCacheKey(session.Directory);

            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!directoriesByKey.ContainsKey(key))
                directoriesByKey[key] = session.Directory!;
        }

        var statusByDirectory = new Dictionary<string, Dictionary<string, SessionRuntimeStatus>>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directoriesByKey.Values)
        {
            statusByDirectory[DirectoryPath.GetCacheKey(directory)] = await _getStatusMapForDirectory(directory);
        }

        var summaries = new List<SessionSummaryDto>();
        var semaphore = new SemaphoreSlim(6);

        var tasks = globalSessions.Select(async session =>
        {
            await semaphore.WaitAsync();

            try
            {
                var statusMap = statusByDirectory.GetValueOrDefault(DirectoryPath.GetCacheKey(session.Directory)) ?? new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

                var runtimeStatus = _normalizeRuntimeStatus(session.Directory, session.Id, statusMap);
                var createdAt = _parseTime(session.CreatedAt);
                var modifiedAt = _parseTime(session.UpdatedAt) ?? createdAt ?? DateTimeOffset.UtcNow.ToString("O");

                var hasAssistantResponse = SessionStatusPolicy.IsRuntimeRunning(runtimeStatus)
                    ? null
                    : await _getHasAssistantResponse(session.Id);

                var status = _deriveSessionKanbanStatus(runtimeStatus, modifiedAt, hasAssistantResponse);

                lock (summaries)
                {
                    summaries.Add(
                        new SessionSummaryDto
                        {
                            Id = session.Id,
                            Name = session.Name,
                            Project = session.Project,
                            Description = null,
                            GitBranch = null,
                            CreatedAt = createdAt,
                            ModifiedAt = modifiedAt,
                            RuntimeStatus = new SessionRuntimeStatus(runtimeStatus),
                            Status = status,
                            HasAssistantResponse = hasAssistantResponse,
                            OpenCodeUrl = _buildOpenCodeSessionUrl(session.Id, session.Directory)
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
            var queueSummaries = queueItems.Select(_mapQueueItemToSessionSummary).Where(x => x is not null).Cast<SessionSummaryDto>();
            summaries.AddRange(queueSummaries);
        }
        catch (Exception queueError)
        {
            Console.Error.WriteLine($"Error loading orchestrator queue for session list: {queueError}");
        }

        var sorted = summaries
            .OrderByDescending(s => _parseTime(s.ModifiedAt) ?? string.Empty)
            .ToList();

        return sorted;
    }

    public async Task<SessionTasksResult> GetSessionTasksAsync(string sessionId)
    {
        var info = await _findSessionInfo(sessionId);

        if (info is null)
            return new SessionTasksResult(false, []);

        var todos = await _getTodosForSession(sessionId, info.Directory);
        var tasks = _mapTodosToViewerTasks(todos);

        return new SessionTasksResult(true, tasks);
    }

    public async Task<LastAssistantMessageResult> GetLastAssistantMessageAsync(string sessionId)
    {
        var info = await _findSessionInfo(sessionId);

        if (info is null)
            return new LastAssistantMessageResult(
                false,
                sessionId,
                null,
                null);

        var last = await _getLastAssistantMessage(sessionId);

        return new LastAssistantMessageResult(
            true,
            sessionId,
            last?.Message,
            last?.CreatedAt);
    }

    public async Task<ArchiveSessionResult> ArchiveSessionAsync(string sessionId)
    {
        var info = await _findSessionInfo(sessionId);

        if (info is null)
            return new ArchiveSessionResult(false, null);

        var archivedAt = await _archiveSessionOnOpenCode(sessionId, info.Directory);
        _invalidateAllCaches();
        await _broadcastUpdate();

        return new ArchiveSessionResult(true, archivedAt);
    }
}
