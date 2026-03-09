using TaskViewer.Application.Orchestration;
using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.OpenCode;

namespace TaskViewer;

public sealed class SonarOrchestrator : IOrchestrationGateway, IAsyncDisposable
{
    const int MaxEnqueueBatch = 1000;
    const int MaxRuleScanIssues = 5000;
    const int MaxEnqueueAllScanIssues = 20_000;
    readonly ISonarEnqueueAllIssuesReadService _enqueueAllIssuesReadService;
    readonly IEnqueueContextResolver _enqueueContextResolver;
    readonly ISonarIssuesReadService _issuesReadService;
    readonly SonarOrchestratorOptions _options;
    readonly IOrchestrationInputNormalizer _orchestrationInputNormalizer;
    readonly IOrchestrationMappingService _orchestrationMappingService;
    readonly IOrchestrationStatusService _orchestrationStatusService;
    readonly IOrchestratorRuntime _orchestratorRuntime;
    readonly IQueueCommandsService _queueCommandsService;
    readonly IQueueEnqueueService _queueEnqueueService;
    readonly IQueueQueryService _queueQueryService;
    readonly IOrchestrationPersistence _persistence;
    readonly IQueueRepository _queueRepository;
    readonly ITaskReadinessGate _taskReadinessGate;
    readonly ITaskReconcilerService _taskReconcilerService;
    readonly ITaskSchedulerService _taskSchedulerService;
    readonly ISonarRulesReadService _rulesReadService;
    readonly IWorkloadBackpressureStateService _workloadBackpressureStateService;
    readonly string _leaseOwner;
    volatile bool _disposed;

    public SonarOrchestrator(SonarOrchestratorOptions options)
    {
        _options = options;
        _leaseOwner = $"worker-{Environment.ProcessId}";
        _persistence = _options.Persistence ?? throw new InvalidOperationException("SonarOrchestrator requires a configured persistence provider.");
        _queueRepository = _persistence.QueueRepository;
        IMappingRepository mappingRepository = _persistence.MappingRepository;
        _orchestrationInputNormalizer = _options.OrchestrationInputNormalizer ?? new OrchestrationInputNormalizer();
        _orchestrationMappingService = _options.OrchestrationMappingService ?? new OrchestrationMappingService(mappingRepository, _options.NormalizeDirectory, NowUtc);
        _orchestrationStatusService = _options.OrchestrationStatusService ?? new OrchestrationStatusService();
        _enqueueContextResolver = new EnqueueContextResolver(mappingRepository);

        var ruleReadService = _options.SonarRuleReadService ??
                              (_options.SonarQubeService is not null
                                  ? new CachedSonarRuleReadService(_options.SonarQubeService)
                                  : new FallbackSonarRuleReadService());

        _rulesReadService = _options.SonarRulesReadService ??
                            (_options.SonarQubeService is not null
                                ? new SonarRulesReadService(_options.SonarQubeService, ruleReadService)
                                : new DisabledSonarRulesReadService());

        _issuesReadService = _options.SonarIssuesReadService ??
                             (_options.SonarQubeService is not null
                                 ? new SonarIssuesReadService(_options.SonarQubeService)
                                 : new DisabledSonarIssuesReadService());

        _enqueueAllIssuesReadService = _options.SonarEnqueueAllIssuesReadService ??
                                       (_options.SonarQubeService is not null
                                           ? new SonarEnqueueAllIssuesReadService(_options.SonarQubeService)
                                           : new DisabledSonarEnqueueAllIssuesReadService());

        var openCodeService = _options.OpenCodeApiClient ?? new DisabledOpenCodeService();
        var workingSessionsReadService = _options.WorkingSessionsReadService ?? new WorkingSessionsReadService(mappingRepository, openCodeService);
        var queueDispatchService = _options.QueueDispatchService ?? new QueueDispatchService(openCodeService, _options.BuildOpenCodeSessionUrl);
        var dispatchFailurePolicy = _options.DispatchFailurePolicy ?? new DispatchFailurePolicy();
        var workloadBackpressurePolicy = _options.WorkloadBackpressurePolicy ?? new WorkloadBackpressurePolicy();

        _taskSchedulerService = _options.TaskSchedulerService ?? new TaskSchedulerService(_queueRepository, NowUtc);
        _taskReadinessGate = _options.TaskReadinessGate ?? new TaskReadinessGate(_options.SonarQubeService);
        _taskReconcilerService = _options.TaskReconcilerService ?? new TaskReconcilerService(_queueRepository, openCodeService, dispatchFailurePolicy, NowUtc);
        _orchestratorRuntime = _options.OrchestratorRuntime ?? new OrchestratorRuntime();
        _workloadBackpressureStateService = _options.WorkloadBackpressureStateService ?? new WorkloadBackpressureStateService(workingSessionsReadService, workloadBackpressurePolicy);
        _queueEnqueueService = _options.QueueEnqueueService ?? new QueueEnqueueService(_queueRepository, _options.MaxAttempts, NowUtc);
        _queueCommandsService = _options.QueueCommandsService ?? new QueueCommandsService(_queueRepository, ArchiveSessionAsync, NowUtc);
        _queueQueryService = _options.QueueQueryService ?? new QueueQueryService(_queueRepository);

        QueueDispatchService = queueDispatchService;
    }

    IQueueDispatchService QueueDispatchService { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _orchestratorRuntime.DisposeAsync();
        await _persistence.DisposeAsync();
    }

    static DateTimeOffset NowUtc() => DateTimeOffset.UtcNow;

    public bool IsConfigured()
    {
        return _orchestrationStatusService.IsConfigured(_options.SonarQubeService, _options.SonarUrl, _options.SonarToken);
    }

    public OrchestrationConfigDto GetPublicConfig()
    {
        return _orchestrationStatusService.BuildPublicConfig(
            IsConfigured(),
            _options.MaxActive,
            _options.PerProjectMaxActive,
            _options.PollMs,
            _options.LeaseSeconds,
            _options.MaxAttempts,
            _options.MaxWorkingGlobal,
            _options.WorkingResumeBelow);
    }

    public async Task<List<MappingRecord>> ListMappings() => await _orchestrationMappingService.ListMappingsAsync();
    public async Task<MappingRecord?> GetMappingById(int? mappingId) => await _orchestrationMappingService.GetMappingByIdAsync(mappingId);
    public async Task<bool> DeleteMapping(int? mappingId) => await _orchestrationMappingService.DeleteMappingAsync(mappingId);
    public async Task<MappingRecord> UpsertMapping(UpsertMappingRequest request) => await _orchestrationMappingService.UpsertMappingAsync(request);
    public async Task<InstructionProfileRecord?> GetInstructionProfile(int? mappingId, string? issueType) => await _orchestrationMappingService.GetInstructionProfileAsync(mappingId, issueType);
    public async Task<InstructionProfileRecord> UpsertInstructionProfile(UpsertInstructionProfileRequest request) => await _orchestrationMappingService.UpsertInstructionProfileAsync(request);

    public async Task<RulesListDto> ListRules(int? mappingId, string? issueType, string? issueStatus)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null || !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var summary = await _rulesReadService.SummarizeRulesAsync(mapping, issueType, issueStatus, MaxRuleScanIssues);
        return OrchestrationResponseMapper.BuildRulesList(mapping, summary);
    }

    public async Task<IssuesListDto> ListIssues(int? mappingId, string? issueType, string? severity, string? issueStatus, string? page, string? pageSize, string? ruleKeys)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null || !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var rules = _orchestrationInputNormalizer.NormalizeRuleKeys(ruleKeys);
        var (p, ps) = _orchestrationInputNormalizer.ParseIssuePaging(page, pageSize);
        var result = await _issuesReadService.ListIssuesAsync(mapping, issueType, severity, issueStatus, p, ps, rules);
        return OrchestrationResponseMapper.BuildIssuesList(mapping, result);
    }

    public async Task<EnqueueIssuesResultDto> EnqueueIssues(EnqueueIssuesRequest request)
    {
        var context = await _enqueueContextResolver.ResolveAsync(request.MappingId, request.IssueType, request.Instructions);
        var requestedCount = request.Issues?.Count ?? 0;
        var normalizedIssues = request.Issues?
            .Take(MaxEnqueueBatch)
            .Select(issue => SonarIssueNormalizer.NormalizeForQueue(issue, context.Mapping))
            .Where(issue => issue is not null)
            .Cast<NormalizedIssue>()
            .ToList() ?? [];
        var invalidCount = Math.Max(0, Math.Min(requestedCount, MaxEnqueueBatch) - normalizedIssues.Count);

        if (normalizedIssues.Count == 0)
            throw new InvalidOperationException("No issues provided");

        var enqueueResult = await _queueEnqueueService.EnqueueRawIssuesAsync(context.Mapping, context.Type, context.InstructionText, normalizedIssues);
        var skipped = enqueueResult.Skipped.ToList();

        for (var i = 0; i < invalidCount; i++)
            skipped.Add(OrchestrationResponseMapper.BuildInvalidIssueSkip());

        var result = OrchestrationResponseMapper.BuildEnqueueIssuesResult(enqueueResult.CreatedItems, skipped);
        result.Requested = requestedCount;
        return result;
    }

    public async Task<EnqueueAllResultDto> EnqueueAllMatching(EnqueueAllRequest request)
    {
        var rules = _orchestrationInputNormalizer.NormalizeRuleKeys(request.RuleKeys);

        if (!_orchestrationInputNormalizer.HasSingleSpecificRule(rules))
            throw new InvalidOperationException("A specific rule key is required to queue all matching issues");

        var context = await _enqueueContextResolver.ResolveAsync(request.MappingId, request.IssueType, request.Instructions);
        var collected = await _enqueueAllIssuesReadService.CollectMatchingIssuesAsync(
            context.Mapping,
            context.Type,
            request.Severity,
            request.IssueStatus,
            rules,
            MaxEnqueueAllScanIssues);

        var enqueueResult = await _queueEnqueueService.EnqueueRawIssuesAsync(context.Mapping, context.Type, context.InstructionText, collected.Issues);
        var result = OrchestrationResponseMapper.BuildEnqueueAllResult(collected.Matched, collected.Truncated, enqueueResult.CreatedItems, enqueueResult.Skipped);
        result.Requested = collected.Issues.Count;
        return result;
    }

    public async Task<List<QueueItemRecord>> ListQueue(string? states, string? limit) => await _queueQueryService.ListQueueAsync(states, limit);

    public async Task<QueueStatsDto> GetQueueStats()
    {
        var stats = await _queueQueryService.GetQueueStatsAsync();
        return OrchestrationResponseMapper.BuildQueueStats(stats);
    }

    public async Task<OrchestrationWorkerStateDto> GetWorkerState()
    {
        var backpressure = await EvaluateWorkloadBackpressure(false);
        var tasks = await _queueRepository.ListQueue(["leased", "running"], 5000);
        var inFlightLeases = tasks.Count(task => string.Equals(task.State, "leased", StringComparison.Ordinal));
        var runningTasks = tasks.Count(task => string.Equals(task.State, "running", StringComparison.Ordinal));

        return _orchestrationStatusService.BuildWorkerState(
            inFlightLeases,
            runningTasks,
            _options.MaxActive,
            _options.PerProjectMaxActive,
            _options.LeaseSeconds,
            backpressure);
    }

    public async Task<bool> CancelQueueItem(int? queueId) => await _queueCommandsService.CancelQueueItemAsync(queueId);
    public async Task<int> RetryFailed() => await _queueCommandsService.RetryFailedAsync();
    public async Task<int> ClearQueued() => await _queueCommandsService.ClearQueuedAsync();
    public async Task<bool> ApproveTask(int? taskId) => await _queueCommandsService.ApproveTaskAsync(taskId);
    public async Task<bool> RejectTask(int? taskId, string? reason) => await _queueCommandsService.RejectTaskAsync(taskId, reason);
    public async Task<bool> RequeueTask(int? taskId, string? reason) => await _queueCommandsService.RequeueTaskAsync(taskId, reason);
    public async Task<bool> RepromptTask(int? taskId, string instructions, string? reason) => await _queueCommandsService.RepromptTaskAsync(taskId, instructions, reason);
    public async Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistory(int? taskId)
    {
        var id = taskId.GetValueOrDefault(-1);

        if (id <= 0)
            throw new InvalidOperationException("Invalid task id");

        var history = await _queueRepository.GetTaskReviewHistory(id);
        return history
            .Select(entry => new TaskReviewHistoryDto
            {
                Action = entry.Action,
                Reason = entry.Reason,
                CreatedAt = entry.CreatedAt
            })
            .ToList();
    }

    public async Task ResetState()
    {
        await _persistence.ResetStateAsync();
        _options.OnChange();
    }

    async Task<WorkloadBackpressureState> EvaluateWorkloadBackpressure(bool forceRefresh)
    {
        return await _workloadBackpressureStateService.EvaluateAsync(
            forceRefresh,
            _options.MaxWorkingGlobal,
            _options.WorkingResumeBelow,
            _options.PollMs,
            _options.OnChange);
    }

    async Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory)
    {
        if (_options.OpenCodeApiClient is null)
            return null;

        return await _options.OpenCodeApiClient.ArchiveSessionAsync(sessionId, directory);
    }

    async Task ProcessNextTaskAsync()
    {
        var leased = await _taskSchedulerService.LeaseNextTaskAsync(_leaseOwner, _options.MaxActive, _options.PerProjectMaxActive, _options.LeaseSeconds);

        if (leased is null)
            return;

        var issues = await _queueRepository.GetTaskIssues(leased.Id);
        var readiness = await _taskReadinessGate.EvaluateAsync(leased, issues);

        if (!readiness.IsReady)
        {
            await _queueRepository.MarkDispatchFailure(
                leased.Id,
                "failed",
                null,
                readiness.Reason ?? "Task failed readiness checks.",
                NowUtc());
            return;
        }

        try
        {
            var dispatched = await QueueDispatchService.DispatchAsync(leased, issues);
            var now = NowUtc();
            await _queueRepository.MarkTaskRunning(
                leased.Id,
                dispatched.SessionId,
                dispatched.OpenCodeUrl,
                _leaseOwner,
                now,
                now.AddSeconds(_options.LeaseSeconds));
        }
        catch (Exception ex)
        {
            var (attemptCount, maxAttempts) = await _queueRepository.GetAttemptInfo(leased.Id, leased.AttemptCount, leased.MaxAttempts);
            var now = NowUtc();
            var decision = (_options.DispatchFailurePolicy ?? new DispatchFailurePolicy()).Decide(attemptCount, maxAttempts, now);

            await _queueRepository.MarkDispatchFailure(
                leased.Id,
                decision.State,
                decision.NextAttemptAt,
                ex.Message,
                now);
        }
    }

    public async Task Tick()
    {
        await _orchestratorRuntime.RunOnceAsync(async () =>
        {
            if (_disposed || !IsConfigured())
                return;

            var workload = await EvaluateWorkloadBackpressure(true);

            if (workload.Paused)
                return;

            await _taskReconcilerService.ReconcileAsync(_options.LeaseSeconds);

            for (var i = 0; i < _options.MaxActive; i++)
            {
                await ProcessNextTaskAsync();
            }
        });
    }

    public void Start(CancellationToken stoppingToken) => _orchestratorRuntime.Start(_options.PollMs, stoppingToken, Tick);

}
