namespace TaskViewer.Domain.Orchestration;

public sealed class OrchestrationWorkerStateDto
{
    public required int InFlightDispatches { get; init; }
    public required int MaxActiveDispatches { get; init; }
    public required bool PausedByWorking { get; init; }
    public required int WorkingCount { get; init; }
    public required int MaxWorkingGlobal { get; init; }
    public required int WorkingResumeBelow { get; init; }
    public DateTimeOffset? WorkingSampleAt { get; init; }
    public int? RunningTasks { get; init; }
    public int? PerProjectMaxActive { get; init; }
    public int? LeaseSeconds { get; init; }
}
