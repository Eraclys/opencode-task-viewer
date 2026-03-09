using TaskViewer.SonarQube;

namespace TaskViewer.Server.Application.Orchestration;

sealed class TaskReadinessGate : ITaskReadinessGate
{
    readonly ISonarQubeService? _sonarQubeService;

    public TaskReadinessGate(ISonarQubeService? sonarQubeService)
    {
        _sonarQubeService = sonarQubeService;
    }

    public async Task<TaskReadinessDecision> EvaluateAsync(QueueItemRecord task, IReadOnlyList<NormalizedIssue> issues)
    {
        if (!Directory.Exists(task.Directory))
            return new TaskReadinessDecision(false, "Repository directory is not available.");

        var absolutePath = ResolveAbsolutePath(task);

        if (!string.IsNullOrWhiteSpace(absolutePath) && !File.Exists(absolutePath))
            return new TaskReadinessDecision(false, "Task file no longer exists.");

        if (HasRepositoryConflict(task.Directory))
            return new TaskReadinessDecision(false, "Repository has an active merge or rebase in progress.");

        if (_sonarQubeService is null || issues.Count == 0)
            return new TaskReadinessDecision(true, null);

        var query = new Dictionary<string, string?>
        {
            ["componentKeys"] = task.SonarProjectKey,
            ["issues"] = string.Join(',', issues.Select(issue => issue.Key))
        };

        if (!string.IsNullOrWhiteSpace(task.Branch))
            query["branch"] = task.Branch;

        var response = await _sonarQubeService.SearchIssuesAsync(query, 1, Math.Max(1, issues.Count));

        return response.Issues.Count == 0
            ? new TaskReadinessDecision(false, "SonarQube no longer reports any active issues for this task.")
            : new TaskReadinessDecision(true, null);
    }

    static string? ResolveAbsolutePath(QueueItemRecord task)
    {
        if (!string.IsNullOrWhiteSpace(task.AbsolutePath))
            return task.AbsolutePath;

        if (string.IsNullOrWhiteSpace(task.RelativePath))
            return null;

        var relativePath = task.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(task.Directory, relativePath));
    }

    static bool HasRepositoryConflict(string directory)
    {
        var gitDir = Path.Combine(directory, ".git");

        return File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))
               || Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
               || Directory.Exists(Path.Combine(gitDir, "rebase-apply"));
    }
}

sealed class AlwaysReadyGate : ITaskReadinessGate
{
    public Task<TaskReadinessDecision> EvaluateAsync(QueueItemRecord task, IReadOnlyList<NormalizedIssue> issues)
        => Task.FromResult(new TaskReadinessDecision(true, null));
}
