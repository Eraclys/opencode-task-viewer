namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface IWorkloadBackpressureStateService
{
    Task<WorkloadBackpressureState> EvaluateAsync(
        bool forceRefresh,
        int maxWorkingGlobal,
        int workingResumeBelow,
        int pollMs,
        Action onStateChanged);
}
