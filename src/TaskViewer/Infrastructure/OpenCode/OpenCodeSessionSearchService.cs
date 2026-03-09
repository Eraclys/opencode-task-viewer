using System.Globalization;
using TaskViewer.Domain;
using TaskViewer.Domain.Sessions;
using TaskViewer.OpenCode;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeSessionSearchService
{
    readonly IOpenCodeService _openCodeService;
    readonly OpenCodeViewerCachePolicy _cachePolicy;
    readonly OpenCodeViewerState _viewerState;

    public OpenCodeSessionSearchService(
        IOpenCodeService openCodeService,
        OpenCodeViewerState viewerState,
        OpenCodeViewerCachePolicy cachePolicy)
    {
        _openCodeService = openCodeService;
        _viewerState = viewerState;
        _cachePolicy = cachePolicy;
    }

    public string? BuildOpenCodeSessionUrl(string sessionId, string? directory) => _openCodeService.BuildSessionUrl(sessionId, directory);

    public async Task<List<OpenCodeSessionDto>> ListGlobalSessionsAsync(
        string limitParam,
        int maxAllSessions,
        int maxSessionsPerProject,
        CancellationToken cancellationToken = default)
    {
        var cachedSessions = _viewerState.GetFreshSessions();

        if (cachedSessions is not null)
            return cachedSessions;

        var limit = string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase)
            ? maxAllSessions
            : Math.Clamp(ParseIntSafe(limitParam, 20), 1, maxAllSessions);

        var projects = await ListProjectsAsync(cancellationToken);
        var projectSearchEntries = BuildProjectSearchEntries(projects);
        var perDirectoryLimit = string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase)
            ? maxSessionsPerProject
            : Math.Clamp(limit * 8, 120, maxSessionsPerProject);

        var sessions = new List<OpenCodeSessionDto>();

        foreach (var entry in projectSearchEntries)
        {
            var listed = await ListSessionsForDirectoryAsync(entry.Directory, entry.ProjectWorktree, perDirectoryLimit, maxSessionsPerProject, cancellationToken);
            sessions.AddRange(listed);
        }

        sessions = sessions
            .OrderByDescending(session => session.UpdatedAt ?? session.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        if (!string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase))
            sessions = sessions.Take(limit).ToList();

        _viewerState.StoreSessions(sessions, _cachePolicy.SessionsCacheTtlMs);
        return sessions;
    }

    public async Task<Dictionary<string, SessionRuntimeStatus>> GetStatusMapForDirectoryAsync(string? directory, CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = DirectoryPath.Normalize(directory);

        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

        var directoryKey = OpenCodeCacheKeys.Directory(normalizedDirectory);

        if (string.IsNullOrWhiteSpace(directoryKey))
            return new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

        if (_viewerState.TryGetFreshStatusMap(directoryKey, out var cachedStatusMap))
            return cachedStatusMap;

        Dictionary<string, SessionRuntimeStatus> statusMap = new(StringComparer.Ordinal);

        foreach (var candidateDirectory in DirectoryPath.GetVariants(normalizedDirectory))
        {
            try
            {
                var result = await _openCodeService.ReadWorkingStatusMapAsync(candidateDirectory, cancellationToken);
                var normalized = result.ToDictionary(pair => pair.Key, pair => new SessionRuntimeStatus(pair.Value), StringComparer.Ordinal);

                if (normalized.Count > 0)
                {
                    statusMap = normalized;
                    break;
                }

                if (statusMap.Count == 0)
                    statusMap = normalized;
            }
            catch
            {
            }
        }

        _viewerState.StoreStatusMap(directoryKey, statusMap, _cachePolicy.StatusCacheTtlMs);
        return statusMap;
    }

    public async Task<List<SessionTodoDto>> GetTodosForSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
    {
        if (_viewerState.TryGetFreshTodos(directory, sessionId, out var cachedTodos))
            return cachedTodos;

        var rawTodos = await _openCodeService.ReadTodosAsync(sessionId, directory, cancellationToken);
        var normalized = rawTodos.Select(todo => new SessionTodoDto(todo.Content, todo.Status, todo.Priority)).ToList();
        _viewerState.StoreTodos(directory, sessionId, normalized, _cachePolicy.TodoCacheTtlMs);
        return normalized;
    }

    public async Task<OpenCodeSessionDto?> FindSessionInfoAsync(string sessionId, int maxAllSessions, int maxSessionsPerProject, CancellationToken cancellationToken = default)
    {
        if (_viewerState.TryGetSessionInfo(sessionId, out var cached))
            return cached;

        try
        {
            await ListGlobalSessionsAsync("200", maxAllSessions, maxSessionsPerProject, cancellationToken);
        }
        catch
        {
        }

        return _viewerState.TryGetSessionInfo(sessionId, out var refreshed)
            ? refreshed
            : null;
    }

    public async Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
    {
        return await _openCodeService.ArchiveSessionAsync(sessionId, directory, cancellationToken);
    }

    public async Task<LastAssistantMessage?> GetLastAssistantMessageAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var tailMessages = await _openCodeService.ReadMessagesAsync(sessionId, 400, cancellationToken);
        var match = tailMessages.LastOrDefault(message => string.Equals(message.Role, "assistant", StringComparison.Ordinal));

        if (match is not null)
            return new LastAssistantMessage(match.Text, match.CreatedAt);

        if (tailMessages.Count < 400)
            return null;

        var allMessages = await _openCodeService.ReadMessagesAsync(sessionId, cancellationToken: cancellationToken);
        var allMatch = allMessages.LastOrDefault(message => string.Equals(message.Role, "assistant", StringComparison.Ordinal));
        return allMatch is null ? null : new LastAssistantMessage(allMatch.Text, allMatch.CreatedAt);
    }

    static int ParseIntSafe(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    static List<(string Directory, string ProjectWorktree)> BuildProjectSearchEntries(List<OpenCodeProject> projects)
    {
        var entries = new List<(string Directory, string ProjectWorktree)>();
        var seenDirectoryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            var worktree = DirectoryPath.Normalize(project.Worktree);
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(worktree) &&
                worktree is not "/" and not "\\")
                candidates.Add(worktree);

            candidates.AddRange(project.SandboxDirectories.Select(DirectoryPath.Normalize).Where(value => !string.IsNullOrWhiteSpace(value))!);

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) ||
                    candidate is "/" or "\\")
                    continue;

                var key = OpenCodeCacheKeys.Directory(candidate);

                if (string.IsNullOrWhiteSpace(key) ||
                    !seenDirectoryKeys.Add(key))
                    continue;

                entries.Add((candidate, worktree ?? candidate));
            }
        }

        return entries;
    }

    async Task<List<OpenCodeProject>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        var cachedProjects = _viewerState.GetFreshProjects();

        if (cachedProjects is not null)
            return cachedProjects;

        var projects = await _openCodeService.ReadProjectsAsync(cancellationToken);
        _viewerState.StoreProjects(projects, _cachePolicy.ProjectsCacheTtlMs);
        return projects;
    }

    async Task<List<OpenCodeSessionDto>> ListSessionsForDirectoryAsync(string directory, string? projectWorktree, int limit, int maxSessionsPerProject, CancellationToken cancellationToken)
    {
        var normalizedDirectory = DirectoryPath.Normalize(directory);

        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return [];

        var directoryKey = OpenCodeCacheKeys.Directory(normalizedDirectory);

        if (string.IsNullOrWhiteSpace(directoryKey))
            return [];

        if (_viewerState.TryGetFreshSessionsForDirectory(directoryKey, out var cachedSessions))
            return cachedSessions;

        var perRequestLimit = Math.Clamp(limit, 1, maxSessionsPerProject);
        var mergedById = new Dictionary<string, OpenCodeSessionDto>(StringComparer.Ordinal);
        var hadSuccess = false;
        Exception? lastError = null;

        foreach (var candidateDirectory in DirectoryPath.GetVariants(normalizedDirectory))
        {
            try
            {
                var sessionsForDirectory = await _openCodeService.ReadSessionsAsync(candidateDirectory, perRequestLimit, cancellationToken);
                hadSuccess = true;

                var list = sessionsForDirectory
                    .Where(session => !session.ArchivedAt.HasValue)
                    .Select(session => new OpenCodeSessionDto(
                        session.Id,
                        session.Name,
                        session.Directory ?? candidateDirectory,
                        session.Project ?? DirectoryPath.Normalize(projectWorktree) ?? session.Directory ?? candidateDirectory,
                        session.CreatedAt,
                        session.UpdatedAt))
                    .ToList();

                foreach (var session in list)
                {
                    if (!mergedById.ContainsKey(session.Id))
                        mergedById[session.Id] = session;
                }
            }
            catch (Exception error)
            {
                lastError = error;
            }
        }

        if (!hadSuccess &&
            lastError is not null)
            throw lastError;

        var sessions = mergedById.Values.ToList();
        _viewerState.StoreSessionsForDirectory(directoryKey, sessions, _cachePolicy.DirectorySessionsCacheTtlMs);
        return sessions;
    }
}
