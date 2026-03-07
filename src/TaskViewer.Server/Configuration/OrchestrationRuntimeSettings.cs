namespace TaskViewer.Server.Configuration;

public sealed record OrchestrationRuntimeSettings(
    string DbPath,
    int MaxActive,
    int PollMs,
    int MaxAttempts,
    int MaxWorkingGlobal,
    int WorkingResumeBelow);
