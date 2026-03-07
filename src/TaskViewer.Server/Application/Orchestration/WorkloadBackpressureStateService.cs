namespace TaskViewer.Server.Application.Orchestration;

internal sealed class WorkloadBackpressureStateService : IWorkloadBackpressureStateService
{
    private readonly IWorkingSessionsReadService _workingSessionsReadService;
    private readonly IWorkloadBackpressurePolicy _workloadBackpressurePolicy;
    private bool _workloadPaused;
    private DateTimeOffset? _latestWorkingSampleAt;

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
                Paused: false,
                WorkingCount: 0,
                MaxWorkingGlobal: maxWorkingGlobal,
                WorkingResumeBelow: workingResumeBelow,
                SampleAt: _latestWorkingSampleAt);
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
            Paused: _workloadPaused,
            WorkingCount: sample.Count,
            MaxWorkingGlobal: maxWorkingGlobal,
            WorkingResumeBelow: workingResumeBelow,
            SampleAt: _latestWorkingSampleAt);
    }
}
