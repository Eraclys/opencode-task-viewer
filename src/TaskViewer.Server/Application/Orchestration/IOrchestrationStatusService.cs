using TaskViewer.SonarQube;

namespace TaskViewer.Server.Application.Orchestration;

internal interface IOrchestrationStatusService
{
    bool IsConfigured(ISonarQubeService? sonarQubeService, string sonarUrl, string sonarToken);
    OrchestrationConfigDto BuildPublicConfig(bool configured, int maxActive, int pollMs, int maxAttempts, int maxWorkingGlobal, int workingResumeBelow);
    OrchestrationConfigDto BuildPublicConfig(bool configured, int maxActive, int perProjectMaxActive, int pollMs, int leaseSeconds, int maxAttempts, int maxWorkingGlobal, int workingResumeBelow);
    OrchestrationWorkerStateDto BuildWorkerState(int inFlightDispatches, int maxActiveDispatches, WorkloadBackpressureState backpressure);
    OrchestrationWorkerStateDto BuildWorkerState(int inFlightLeases, int runningTasks, int maxActiveDispatches, int perProjectMaxActive, int leaseSeconds, WorkloadBackpressureState backpressure);
}
