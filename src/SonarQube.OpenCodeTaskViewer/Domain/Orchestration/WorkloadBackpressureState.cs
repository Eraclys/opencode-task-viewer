namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record WorkloadBackpressureState(
    bool Paused,
    int WorkingCount,
    int MaxWorkingGlobal,
    int WorkingResumeBelow,
    DateTimeOffset? SampleAt);
