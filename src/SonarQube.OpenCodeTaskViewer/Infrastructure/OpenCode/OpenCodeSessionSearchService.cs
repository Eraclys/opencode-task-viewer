using System.Globalization;
using OpenCode.Client;
using SonarQube.OpenCodeTaskViewer.Domain;
using SonarQube.OpenCodeTaskViewer.Domain.Sessions;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeSessionSearchService
{
    readonly OpenCodeViewerCachePolicy _cachePolicy;
    readonly IOpenCodeService _openCodeService;
    readonly SessionTodoViewService _sessionTodoViewService;
    readonly OpenCodeViewerState _viewerState;

    public OpenCodeSessionSearchService(
        IOpenCodeService openCodeService,
        OpenCodeViewerState viewerState,
        OpenCodeViewerCachePolicy cachePolicy,
        SessionTodoViewService sessionTodoViewService)
    {
        _openCodeService = openCodeService;
        _viewerState = viewerState;
        _cachePolicy = cachePolicy;
        _sessionTodoViewService = sessionTodoViewService;
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
            var listed = await ListSessionsForDirectoryAsync(
                entry.Directory,
                entry.ProjectWorktree,
                perDirectoryLimit,
                maxSessionsPerProject,
                cancellationToken);

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
        var parsedDirectory = DirectoryPath.Parse(directory);

        if (!parsedDirectory.HasValue)
            return new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

        var directoryKey = parsedDirectory.Value.CacheKey;

        if (string.IsNullOrWhiteSpace(directoryKey))
            return new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

        if (_viewerState.TryGetFreshStatusMap(directoryKey, out var cachedStatusMap))
            return cachedStatusMap;

        Dictionary<string, SessionRuntimeStatus> statusMap = new(StringComparer.Ordinal);

        foreach (var candidateDirectory in parsedDirectory.Value.Variants)
        {
            try
            {
                var result = await _openCodeService.ReadWorkingStatusMapAsync(candidateDirectory, cancellationToken);
                var normalized = result.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

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
        var normalized = rawTodos.Select(_sessionTodoViewService.NormalizeTodo).ToList();

        _viewerState.StoreTodos(
            directory,
            sessionId,
            normalized,
            _cachePolicy.TodoCacheTtlMs);

        return normalized;
    }

    public async Task<OpenCodeSessionDto?> FindSessionInfoAsync(
        string sessionId,
        int maxAllSessions,
        int maxSessionsPerProject,
        CancellationToken cancellationToken = default)
    {
        if (_viewerState.TryGetSessionInfo(sessionId, out var cached))
            return cached;

        try
        {
            await ListGlobalSessionsAsync(
                "200",
                maxAllSessions,
                maxSessionsPerProject,
                cancellationToken);
        }
        catch
        {
        }

        return _viewerState.TryGetSessionInfo(sessionId, out var refreshed)
            ? refreshed
            : null;
    }

    public async Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default) => await _openCodeService.ArchiveSessionAsync(sessionId, directory, cancellationToken);

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
        => int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : fallback;

    static List<(string Directory, string ProjectWorktree)> BuildProjectSearchEntries(List<OpenCodeProject> projects)
    {
        var entries = new List<(string Directory, string ProjectWorktree)>();
        var seenDirectoryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            var worktree = DirectoryPath.Parse(project.Worktree);
            var candidates = new List<string>();

            if (worktree is { Value: not "/" and not "\\" })
                candidates.Add(worktree.Value.Value);

            candidates.AddRange(project.SandboxDirectories.Select(DirectoryPath.Parse).Where(value => value.HasValue).Select(value => value!.Value.Value));

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) ||
                    candidate is "/" or "\\")
                    continue;

                var key = OpenCodeCacheKeys.Directory(candidate);

                if (string.IsNullOrWhiteSpace(key) ||
                    !seenDirectoryKeys.Add(key))
                    continue;

                entries.Add((candidate, worktree?.Value ?? candidate));
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

    async Task<List<OpenCodeSessionDto>> ListSessionsForDirectoryAsync(
        string directory,
        string? projectWorktree,
        int limit,
        int maxSessionsPerProject,
        CancellationToken cancellationToken)
    {
        var parsedDirectory = DirectoryPath.Parse(directory);

        if (!parsedDirectory.HasValue)
            return [];

        var directoryKey = parsedDirectory.Value.CacheKey;

        if (string.IsNullOrWhiteSpace(directoryKey))
            return [];

        if (_viewerState.TryGetFreshSessionsForDirectory(directoryKey, out var cachedSessions))
            return cachedSessions;

        var perRequestLimit = Math.Clamp(limit, 1, maxSessionsPerProject);
        var mergedById = new Dictionary<string, OpenCodeSessionDto>(StringComparer.Ordinal);
        var hadSuccess = false;
        Exception? lastError = null;

        foreach (var candidateDirectory in parsedDirectory.Value.Variants)
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
                        session.Project ?? DirectoryPath.Parse(projectWorktree)?.Value ?? session.Directory ?? candidateDirectory,
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
