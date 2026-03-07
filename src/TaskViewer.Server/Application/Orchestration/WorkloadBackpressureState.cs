namespace TaskViewer.Server.Application.Orchestration;

internal sealed record WorkloadBackpressureState(
    bool Paused,
    int WorkingCount,
    int MaxWorkingGlobal,
    int WorkingResumeBelow,
    DateTimeOffset? SampleAt);
