namespace TaskViewer.Server.Configuration;

public sealed record OrchestrationRuntimeSettings(
    string DbPath,
    int MaxActive,
    int PerProjectMaxActive,
    int PollMs,
    int LeaseSeconds,
    int MaxAttempts,
    int MaxWorkingGlobal,
    int WorkingResumeBelow);
