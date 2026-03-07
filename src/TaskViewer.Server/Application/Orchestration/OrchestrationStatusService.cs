using TaskViewer.SonarQube;

namespace TaskViewer.Server.Application.Orchestration;

internal sealed class OrchestrationStatusService : IOrchestrationStatusService
{
    public bool IsConfigured(ISonarQubeService? sonarQubeService, string sonarUrl, string sonarToken)
    {
        return sonarQubeService is not null || (!string.IsNullOrWhiteSpace(sonarUrl) && !string.IsNullOrWhiteSpace(sonarToken));
    }

    public OrchestrationConfigDto BuildPublicConfig(bool configured, int maxActive, int pollMs, int maxAttempts, int maxWorkingGlobal, int workingResumeBelow)
    {
        return new OrchestrationConfigDto
        {
            Configured = configured,
            MaxActive = maxActive,
            PollMs = pollMs,
            MaxAttempts = maxAttempts,
            MaxWorkingGlobal = maxWorkingGlobal,
            WorkingResumeBelow = workingResumeBelow
        };
    }

    public OrchestrationWorkerStateDto BuildWorkerState(int inFlightDispatches, int maxActiveDispatches, WorkloadBackpressureState backpressure)
    {
        return OrchestrationResponseMapper.BuildWorkerState(
            inFlightDispatches,
            maxActiveDispatches,
            backpressure.Paused,
            backpressure.WorkingCount,
            backpressure.MaxWorkingGlobal,
            backpressure.WorkingResumeBelow,
            backpressure.SampleAt);
    }
}
