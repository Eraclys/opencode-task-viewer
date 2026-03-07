namespace TaskViewer.Server.Application.Orchestration;

internal interface IWorkloadBackpressureStateService
{
    Task<WorkloadBackpressureState> EvaluateAsync(
        bool forceRefresh,
        int maxWorkingGlobal,
        int workingResumeBelow,
        int pollMs,
        Action onStateChanged);
}
