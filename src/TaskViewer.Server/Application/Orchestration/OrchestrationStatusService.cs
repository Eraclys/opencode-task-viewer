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
        return BuildPublicConfig(configured, maxActive, 2, pollMs, 180, maxAttempts, maxWorkingGlobal, workingResumeBelow);
    }

    public OrchestrationConfigDto BuildPublicConfig(bool configured, int maxActive, int perProjectMaxActive, int pollMs, int leaseSeconds, int maxAttempts, int maxWorkingGlobal, int workingResumeBelow)
    {
        return new OrchestrationConfigDto
        {
            Configured = configured,
            MaxActive = maxActive,
            PollMs = pollMs,
            MaxAttempts = maxAttempts,
            MaxWorkingGlobal = maxWorkingGlobal,
            WorkingResumeBelow = workingResumeBelow,
            PerProjectMaxActive = perProjectMaxActive,
            LeaseSeconds = leaseSeconds
        };
    }

    public OrchestrationWorkerStateDto BuildWorkerState(int inFlightDispatches, int maxActiveDispatches, WorkloadBackpressureState backpressure)
    {
        return BuildWorkerState(inFlightDispatches, 0, maxActiveDispatches, 2, 180, backpressure);
    }

    public OrchestrationWorkerStateDto BuildWorkerState(int inFlightLeases, int runningTasks, int maxActiveDispatches, int perProjectMaxActive, int leaseSeconds, WorkloadBackpressureState backpressure)
    {
        return OrchestrationResponseMapper.BuildWorkerState(
            inFlightLeases,
            runningTasks,
            maxActiveDispatches,
            perProjectMaxActive,
            leaseSeconds,
            backpressure.Paused,
            backpressure.WorkingCount,
            backpressure.MaxWorkingGlobal,
            backpressure.WorkingResumeBelow,
            backpressure.SampleAt);
    }
}
