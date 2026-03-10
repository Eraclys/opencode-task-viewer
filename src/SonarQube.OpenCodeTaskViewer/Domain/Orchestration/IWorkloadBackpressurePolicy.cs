namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface IWorkloadBackpressurePolicy
{
    BackpressureTransition Evaluate(
        bool currentlyPaused,
        int workingCount,
        int maxWorkingGlobal,
        int workingResumeBelow);
}
