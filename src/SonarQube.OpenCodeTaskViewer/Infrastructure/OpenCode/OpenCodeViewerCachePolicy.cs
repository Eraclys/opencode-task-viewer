namespace SonarQube.OpenCodeTaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeViewerCachePolicy
{
    public int SessionsCacheTtlMs { get; init; } = TaskViewerRuntimeDefaults.SessionsCacheTtlMs;
    public int StatusCacheTtlMs { get; init; } = TaskViewerRuntimeDefaults.StatusCacheTtlMs;
    public int TodoCacheTtlMs { get; init; } = TaskViewerRuntimeDefaults.TodoCacheTtlMs;
    public int TasksAllCacheTtlMs { get; init; } = TaskViewerRuntimeDefaults.TasksAllCacheTtlMs;
    public int ProjectsCacheTtlMs { get; init; } = TaskViewerRuntimeDefaults.ProjectsCacheTtlMs;
    public int DirectorySessionsCacheTtlMs { get; init; } = TaskViewerRuntimeDefaults.DirectorySessionsCacheTtlMs;
    public int MessagePresenceCacheTtlMs { get; init; } = TaskViewerRuntimeDefaults.MessagePresenceCacheTtlMs;
    public int StatusOverrideTtlMs { get; init; } = 60_000;
}
