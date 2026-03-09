using TaskViewer.Infrastructure.OpenCode;

namespace TaskViewer.Application.Sessions;

public sealed class SessionsUseCases : ISessionsUseCases
{
    readonly Func<string, string?, Task<DateTimeOffset?>> _archiveSessionOnOpenCode;
    readonly Func<Task> _broadcastUpdate;
    readonly Func<string, Task<OpenCodeSessionDto?>> _findSessionInfo;
    readonly Func<string, Task<LastAssistantMessage?>> _getLastAssistantMessage;
    readonly Func<string, string?, Task<List<SessionTodoDto>>> _getTodosForSession;
    readonly Action _invalidateAllCaches;
    readonly Func<QueueItemRecord, SessionSummaryDto?> _mapQueueItemToSessionSummary;
    readonly Func<List<SessionTodoDto>, List<ViewerTaskDto>> _mapTodosToViewerTasks;
    readonly SonarOrchestrator _orchestrator;

    public SessionsUseCases(
        OpenCodeSessionSearchService sessionSearchService,
        SonarOrchestrator orchestrator,
        QueueItemSessionSummaryMapper queueItemSessionSummaryMapper,
        SessionTodoViewService sessionTodoViewService,
        OpenCodeViewerUpdateNotifier updateNotifier)
        : this(
            orchestrator,
            queueItemSessionSummaryMapper.Map,
            sessionId => sessionSearchService.FindSessionInfoAsync(
                sessionId,
                TaskViewerRuntimeDefaults.MaxAllSessions,
                TaskViewerRuntimeDefaults.MaxSessionsPerProject),
            sessionSearchService.GetTodosForSessionAsync,
            sessionTodoViewService.MapTodosToViewerTasks,
            sessionSearchService.GetLastAssistantMessageAsync,
            sessionSearchService.ArchiveSessionAsync,
            updateNotifier.InvalidateAllCaches,
            updateNotifier.BroadcastUpdateAsync)
    {
    }

    public SessionsUseCases(
        SonarOrchestrator orchestrator,
        Func<QueueItemRecord, SessionSummaryDto?> mapQueueItemToSessionSummary,
        Func<string, Task<OpenCodeSessionDto?>> findSessionInfo,
        Func<string, string?, Task<List<SessionTodoDto>>> getTodosForSession,
        Func<List<SessionTodoDto>, List<ViewerTaskDto>> mapTodosToViewerTasks,
        Func<string, Task<LastAssistantMessage?>> getLastAssistantMessage,
        Func<string, string?, Task<DateTimeOffset?>> archiveSessionOnOpenCode,
        Action invalidateAllCaches,
        Func<Task> broadcastUpdate)
    {
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
        var requestedLimit = string.IsNullOrWhiteSpace(limitParam) ? "1000" : limitParam;
        var summaries = new List<SessionSummaryDto>();

        try
        {
            var queueItems = await _orchestrator.ListQueue("queued,dispatching,leased,running,awaiting_review,rejected,failed,cancelled", requestedLimit);
            var queueSummaries = queueItems
                .Select(item => _mapQueueItemToSessionSummary(item))
                .Where(x => x is not null)
                .Cast<SessionSummaryDto>();
            summaries.AddRange(queueSummaries);
        }
        catch (Exception queueError)
        {
            Console.Error.WriteLine($"Error loading orchestrator queue for session list: {queueError}");
        }

        var sorted = summaries
            .OrderByDescending(s => s.ModifiedAt)
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

    public async Task<LastAssistantMessageResult> GetTaskLastAssistantMessageAsync(string taskId)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.StartsWith("queue-", StringComparison.OrdinalIgnoreCase))
            normalizedTaskId = normalizedTaskId[6..];

        var taskKey = $"queue-{normalizedTaskId}";
        var summaries = await ListSessionsAsync("1000");
        var task = summaries.FirstOrDefault(summary => string.Equals(summary.Id, taskKey, StringComparison.Ordinal));

        var sessionId = ExtractSessionIdFromOpenCodeUrl(task?.OpenCodeUrl);

        if (task is null || string.IsNullOrWhiteSpace(sessionId))
            return new LastAssistantMessageResult(false, taskKey, null, null);

        return await GetLastAssistantMessageAsync(sessionId);
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

    static string? ExtractSessionIdFromOpenCodeUrl(string? openCodeUrl)
    {
        if (string.IsNullOrWhiteSpace(openCodeUrl))
            return null;

        var marker = "/session/";
        var index = openCodeUrl.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        var sessionId = openCodeUrl[(index + marker.Length)..].Trim();
        return string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
    }
}
