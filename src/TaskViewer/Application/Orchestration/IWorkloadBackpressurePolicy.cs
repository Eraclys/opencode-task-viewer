namespace TaskViewer.Application.Orchestration;

public interface IWorkloadBackpressurePolicy
{
    BackpressureTransition Evaluate(
        bool currentlyPaused,
        int workingCount,
        int maxWorkingGlobal,
        int workingResumeBelow);
}
