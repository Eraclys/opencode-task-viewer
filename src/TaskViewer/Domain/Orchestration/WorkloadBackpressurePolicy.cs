namespace TaskViewer.Domain.Orchestration;

public sealed class WorkloadBackpressurePolicy : IWorkloadBackpressurePolicy
{
    public BackpressureTransition Evaluate(
        bool currentlyPaused,
        int workingCount,
        int maxWorkingGlobal,
        int workingResumeBelow)
    {
        var nextPaused = currentlyPaused;

        if (!nextPaused &&
            workingCount >= maxWorkingGlobal)
            nextPaused = true;
        else if (nextPaused && workingCount < workingResumeBelow)
            nextPaused = false;

        return new BackpressureTransition(nextPaused, nextPaused != currentlyPaused);
    }
}
