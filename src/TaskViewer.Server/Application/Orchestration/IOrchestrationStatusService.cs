namespace TaskViewer.Server.Application.Orchestration;

internal interface IOrchestrationStatusService
{
    bool IsConfigured(ISonarGateway? sonarGateway, string sonarUrl, string sonarToken);
    object BuildPublicConfig(bool configured, int maxActive, int pollMs, int maxAttempts, int maxWorkingGlobal, int workingResumeBelow);
    object BuildWorkerState(int inFlightDispatches, int maxActiveDispatches, WorkloadBackpressureState backpressure);
}

internal sealed class OrchestrationStatusService : IOrchestrationStatusService
{
    public bool IsConfigured(ISonarGateway? sonarGateway, string sonarUrl, string sonarToken)
    {
        return sonarGateway is not null || (!string.IsNullOrWhiteSpace(sonarUrl) && !string.IsNullOrWhiteSpace(sonarToken));
    }

    public object BuildPublicConfig(bool configured, int maxActive, int pollMs, int maxAttempts, int maxWorkingGlobal, int workingResumeBelow)
    {
        return new
        {
            configured,
            maxActive,
            pollMs,
            maxAttempts,
            maxWorkingGlobal,
            workingResumeBelow
        };
    }

    public object BuildWorkerState(int inFlightDispatches, int maxActiveDispatches, WorkloadBackpressureState backpressure)
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
