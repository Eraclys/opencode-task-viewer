namespace TaskViewer.Application.Orchestration;

public sealed class OrchestrationConfigDto
{
    public required bool Configured { get; init; }
    public required int MaxActive { get; init; }
    public required int PollMs { get; init; }
    public required int MaxAttempts { get; init; }
    public required int MaxWorkingGlobal { get; init; }
    public required int WorkingResumeBelow { get; init; }
    public int? PerProjectMaxActive { get; init; }
    public int? LeaseSeconds { get; init; }
}
