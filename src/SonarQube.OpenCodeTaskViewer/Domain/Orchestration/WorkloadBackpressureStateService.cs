namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

sealed class WorkloadBackpressureStateService : IWorkloadBackpressureStateService
{
    readonly IWorkingSessionsReadService _workingSessionsReadService;
    readonly IWorkloadBackpressurePolicy _workloadBackpressurePolicy;
    DateTimeOffset? _latestWorkingSampleAt;
    bool _workloadPaused;

    public WorkloadBackpressureStateService(
        IWorkingSessionsReadService workingSessionsReadService,
        IWorkloadBackpressurePolicy workloadBackpressurePolicy)
    {
        _workingSessionsReadService = workingSessionsReadService;
        _workloadBackpressurePolicy = workloadBackpressurePolicy;
    }

    public async Task<WorkloadBackpressureState> EvaluateAsync(
        bool forceRefresh,
        int maxWorkingGlobal,
        int workingResumeBelow,
        int pollMs,
        Action onStateChanged)
    {
        if (maxWorkingGlobal <= 0)
        {
            _workloadPaused = false;

            return new WorkloadBackpressureState(
                false,
                0,
                maxWorkingGlobal,
                workingResumeBelow,
                _latestWorkingSampleAt);
        }

        var sample = await _workingSessionsReadService.GetWorkingSessionsCountAsync(forceRefresh, pollMs);
        _latestWorkingSampleAt = sample.SampledAt;

        var transition = _workloadBackpressurePolicy.Evaluate(
            _workloadPaused,
            sample.Count,
            maxWorkingGlobal,
            workingResumeBelow);

        _workloadPaused = transition.NextPaused;

        if (transition.Changed)
            onStateChanged();

        return new WorkloadBackpressureState(
            _workloadPaused,
            sample.Count,
            maxWorkingGlobal,
            workingResumeBelow,
            _latestWorkingSampleAt);
    }
}
