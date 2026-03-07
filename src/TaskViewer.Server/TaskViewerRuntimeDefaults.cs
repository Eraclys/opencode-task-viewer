namespace TaskViewer.Server;

internal static class TaskViewerRuntimeDefaults
{
    internal const int SessionsCacheTtlMs = 1500;
    internal const int StatusCacheTtlMs = 1000;
    internal const int TodoCacheTtlMs = 3000;
    internal const int TasksAllCacheTtlMs = 1500;
    internal const int MaxAllSessions = 750;
    internal const int ProjectsCacheTtlMs = 10_000;
    internal const int DirectorySessionsCacheTtlMs = 8_000;
    internal const int MaxSessionsPerProject = 500;
    internal const int MessagePresenceCacheTtlMs = 120_000;
    internal const int SessionRecentWindowMs = 5 * 60 * 1000;
}