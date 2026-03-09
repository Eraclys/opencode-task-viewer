using System.Globalization;
using TaskViewer.Application.Sessions;
using TaskViewer.Domain;
using TaskViewer.OpenCode;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeSessionSearchService
{
    readonly IOpenCodeService _openCodeService;
    readonly OpenCodeViewerCacheCoordinator _cacheCoordinator;

    public OpenCodeSessionSearchService(
        IOpenCodeService openCodeService,
        OpenCodeViewerCacheCoordinator cacheCoordinator)
    {
        _openCodeService = openCodeService;
        _cacheCoordinator = cacheCoordinator;
    }

    public string? BuildOpenCodeSessionUrl(string sessionId, string? directory) => _openCodeService.BuildSessionUrl(sessionId, directory);

    public async Task<List<OpenCodeSessionDto>> ListGlobalSessionsAsync(
        string limitParam,
        int maxAllSessions,
        int maxSessionsPerProject)
    {
        var cachedSessions = _cacheCoordinator.GetFreshSessions();

        if (cachedSessions is not null)
            return cachedSessions;

        var limit = string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase)
            ? maxAllSessions
            : Math.Clamp(ParseIntSafe(limitParam, 20), 1, maxAllSessions);

        var projects = await ListProjectsAsync();
        var projectSearchEntries = BuildProjectSearchEntries(projects);
        var perDirectoryLimit = string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase)
            ? maxSessionsPerProject
            : Math.Clamp(limit * 8, 120, maxSessionsPerProject);

        var sessions = new List<OpenCodeSessionDto>();

        foreach (var entry in projectSearchEntries)
        {
            var listed = await ListSessionsForDirectoryAsync(entry.Directory, entry.ProjectWorktree, perDirectoryLimit, maxSessionsPerProject);
            sessions.AddRange(listed);
        }

        sessions = sessions
            .OrderByDescending(session => session.UpdatedAt ?? session.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        if (!string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase))
            sessions = sessions.Take(limit).ToList();

        _cacheCoordinator.StoreSessions(sessions, DateTimeOffset.UtcNow);
        return sessions;
    }

    public async Task<Dictionary<string, SessionRuntimeStatus>> GetStatusMapForDirectoryAsync(string? directory)
    {
        var normalizedDirectory = DirectoryPath.Normalize(directory);

        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

        var directoryKey = OpenCodeCacheKeys.Directory(normalizedDirectory);

        if (string.IsNullOrWhiteSpace(directoryKey))
            return new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

        if (_cacheCoordinator.TryGetFreshStatusMap(directoryKey, out var cachedStatusMap))
            return cachedStatusMap;

        Dictionary<string, SessionRuntimeStatus> statusMap = new(StringComparer.Ordinal);

        foreach (var candidateDirectory in DirectoryPath.GetVariants(normalizedDirectory))
        {
            try
            {
                var result = await _openCodeService.ReadWorkingStatusMapAsync(candidateDirectory);
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

        _cacheCoordinator.StoreStatusMap(directoryKey, statusMap, DateTimeOffset.UtcNow);
        return statusMap;
    }

    public async Task<List<SessionTodoDto>> GetTodosForSessionAsync(string sessionId, string? directory)
    {
        if (_cacheCoordinator.TryGetFreshTodos(directory, sessionId, out var cachedTodos))
            return cachedTodos;

        var rawTodos = await _openCodeService.ReadTodosAsync(sessionId, directory);
        var normalized = rawTodos.Select(todo => new SessionTodoDto(todo.Content, todo.Status, todo.Priority)).ToList();
        _cacheCoordinator.StoreTodos(directory, sessionId, normalized, DateTimeOffset.UtcNow);
        return normalized;
    }

    public async Task<OpenCodeSessionDto?> FindSessionInfoAsync(string sessionId, int maxAllSessions, int maxSessionsPerProject)
    {
        if (_cacheCoordinator.TryGetSessionInfo(sessionId, out var cached))
            return cached;

        try
        {
            await ListGlobalSessionsAsync("200", maxAllSessions, maxSessionsPerProject);
        }
        catch
        {
        }

        return _cacheCoordinator.TryGetSessionInfo(sessionId, out var refreshed)
            ? refreshed
            : null;
    }

    public async Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory)
    {
        return await _openCodeService.ArchiveSessionAsync(sessionId, directory);
    }

    public async Task<LastAssistantMessage?> GetLastAssistantMessageAsync(string sessionId)
    {
        var tailMessages = await _openCodeService.ReadMessagesAsync(sessionId, 400);
        var match = tailMessages.LastOrDefault(message => string.Equals(message.Role, "assistant", StringComparison.Ordinal));

        if (match is not null)
            return new LastAssistantMessage(match.Text, match.CreatedAt);

        if (tailMessages.Count < 400)
            return null;

        var allMessages = await _openCodeService.ReadMessagesAsync(sessionId);
        var allMatch = allMessages.LastOrDefault(message => string.Equals(message.Role, "assistant", StringComparison.Ordinal));
        return allMatch is null ? null : new LastAssistantMessage(allMatch.Text, allMatch.CreatedAt);
    }

    static int ParseIntSafe(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    static List<(string Directory, string ProjectWorktree)> BuildProjectSearchEntries(List<OpenCodeProjectTransport> projects)
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

    async Task<List<OpenCodeProjectTransport>> ListProjectsAsync()
    {
        var cachedProjects = _cacheCoordinator.GetFreshProjects();

        if (cachedProjects is not null)
            return cachedProjects;

        var projects = await _openCodeService.ReadProjectsAsync();
        _cacheCoordinator.StoreProjects(projects, DateTimeOffset.UtcNow);
        return projects;
    }

    async Task<List<OpenCodeSessionDto>> ListSessionsForDirectoryAsync(string directory, string? projectWorktree, int limit, int maxSessionsPerProject)
    {
        var normalizedDirectory = DirectoryPath.Normalize(directory);

        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return [];

        var directoryKey = OpenCodeCacheKeys.Directory(normalizedDirectory);

        if (string.IsNullOrWhiteSpace(directoryKey))
            return [];

        if (_cacheCoordinator.TryGetFreshSessionsForDirectory(directoryKey, out var cachedSessions))
            return cachedSessions;

        var perRequestLimit = Math.Clamp(limit, 1, maxSessionsPerProject);
        var mergedById = new Dictionary<string, OpenCodeSessionDto>(StringComparer.Ordinal);
        var hadSuccess = false;
        Exception? lastError = null;

        foreach (var candidateDirectory in DirectoryPath.GetVariants(normalizedDirectory))
        {
            try
            {
                var sessionsForDirectory = await _openCodeService.ReadSessionsAsync(candidateDirectory, perRequestLimit);
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
        _cacheCoordinator.StoreSessionsForDirectory(directoryKey, sessions, DateTimeOffset.UtcNow);
        return sessions;
    }
}
