using OpenCode.Client;
using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class SonarOrchestrator : IOrchestrationGateway, IAsyncDisposable
{
    const int MaxEnqueueBatch = 1000;
    const int MaxRuleScanIssues = 5000;
    const int MaxEnqueueAllScanIssues = 20_000;
    readonly ISonarEnqueueAllIssuesReadService _enqueueAllIssuesReadService;
    readonly IEnqueueContextResolver _enqueueContextResolver;
    readonly ISonarIssuesReadService _issuesReadService;
    readonly string _leaseOwner;
    readonly SonarOrchestratorOptions _options;
    readonly IOrchestrationInputNormalizer _orchestrationInputNormalizer;
    readonly IOrchestrationMappingService _orchestrationMappingService;
    readonly IOrchestrationStatusService _orchestrationStatusService;
    readonly IOrchestrationPersistence _persistence;
    readonly IQueueRepository _queueRepository;
    readonly ISonarRulesReadService _rulesReadService;
    readonly ITaskReadinessGate _taskReadinessGate;
    readonly ITaskReconcilerService _taskReconcilerService;
    readonly ITaskSchedulerService _taskSchedulerService;
    readonly IWorkloadBackpressureStateService _workloadBackpressureStateService;
    volatile bool _disposed;
    int _tickRunning;

    public SonarOrchestrator(SonarOrchestratorOptions options)
    {
        _options = options;
        _leaseOwner = $"worker-{Environment.ProcessId}";
        _persistence = _options.Persistence ?? throw new InvalidOperationException("SonarOrchestrator requires a configured persistence provider.");
        _queueRepository = _persistence.QueueRepository;
        var mappingRepository = _persistence.MappingRepository;
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

        _taskReconcilerService = _options.TaskReconcilerService ??
        new TaskReconcilerService(
            _queueRepository,
            openCodeService,
            dispatchFailurePolicy,
            NowUtc);

        _workloadBackpressureStateService = _options.WorkloadBackpressureStateService ?? new WorkloadBackpressureStateService(workingSessionsReadService, workloadBackpressurePolicy);

        QueueDispatchService = queueDispatchService;
    }

    IQueueDispatchService QueueDispatchService { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await Task.CompletedTask;
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

    public async Task<List<MappingRecord>> ListMappings(CancellationToken cancellationToken = default) => await _orchestrationMappingService.ListMappingsAsync(cancellationToken);
    public async Task<bool> DeleteMapping(int mappingId, CancellationToken cancellationToken = default) => await _orchestrationMappingService.DeleteMappingAsync(mappingId, cancellationToken);
    public async Task<MappingRecord> UpsertMapping(UpsertMappingRequest request, CancellationToken cancellationToken = default) => await _orchestrationMappingService.UpsertMappingAsync(request, cancellationToken);
    public async Task<InstructionProfileRecord?> GetInstructionProfile(int? mappingId, SonarIssueType issueType, CancellationToken cancellationToken = default) => await _orchestrationMappingService.GetInstructionProfileAsync(mappingId, issueType, cancellationToken);
    public async Task<InstructionProfileRecord> UpsertInstructionProfile(UpsertInstructionProfileRequest request, CancellationToken cancellationToken = default) => await _orchestrationMappingService.UpsertInstructionProfileAsync(request, cancellationToken);

    public async Task<RulesListDto> ListRules(
        int mappingId,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        CancellationToken cancellationToken = default)
    {
        var mapping = await GetMappingById(mappingId, cancellationToken);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var summary = await _rulesReadService.SummarizeRulesAsync(
            mapping,
            issueTypes,
            issueStatuses,
            MaxRuleScanIssues,
            cancellationToken);

        return OrchestrationResponseMapper.BuildRulesList(mapping, summary);
    }

    public async Task<IssuesListDto> ListIssues(
        int mappingId,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueSeverity> severities,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        int? page,
        int? pageSize,
        string? ruleKeys,
        CancellationToken cancellationToken = default)
    {
        var mapping = await GetMappingById(mappingId, cancellationToken);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var rules = _orchestrationInputNormalizer.NormalizeRuleKeys(ruleKeys);
        var (p, ps) = _orchestrationInputNormalizer.ParseIssuePaging(page, pageSize);

        var result = await _issuesReadService.ListIssuesAsync(
            mapping,
            issueTypes,
            severities,
            issueStatuses,
            p,
            ps,
            rules,
            cancellationToken);

        return OrchestrationResponseMapper.BuildIssuesList(mapping, result);
    }

    public async Task<EnqueueIssuesResultDto> EnqueueIssues(EnqueueIssuesRequest request, CancellationToken cancellationToken = default)
    {
        var context = await _enqueueContextResolver.ResolveAsync(
            request.MappingId,
            request.IssueType,
            request.Instructions,
            cancellationToken);

        var requestedCount = request.Issues?.Count ?? 0;

        var normalizedIssues = request
            .Issues?
            .Take(MaxEnqueueBatch)
            .Select(issue => SonarIssueNormalizer.NormalizeForQueue(issue, context.Mapping))
            .Where(issue => issue is not null)
            .Cast<NormalizedIssue>()
            .ToList() ?? [];

        var invalidCount = Math.Max(0, Math.Min(requestedCount, MaxEnqueueBatch) - normalizedIssues.Count);

        if (normalizedIssues.Count == 0)
            throw new InvalidOperationException("No issues provided");

        var enqueueResult = await EnqueueRawIssuesAsync(
            context.Mapping,
            context.Type,
            context.InstructionText,
            normalizedIssues,
            cancellationToken);

        var skipped = enqueueResult.Skipped.ToList();

        for (var i = 0; i < invalidCount; i++)
        {
            skipped.Add(OrchestrationResponseMapper.BuildInvalidIssueSkip());
        }

        var result = OrchestrationResponseMapper.BuildEnqueueIssuesResult(enqueueResult.CreatedItems, skipped);
        result.Requested = requestedCount;

        return result;
    }

    public async Task<EnqueueAllResultDto> EnqueueAllMatching(EnqueueAllRequest request, CancellationToken cancellationToken = default)
    {
        var rules = _orchestrationInputNormalizer.NormalizeRuleKeys(request.RuleKeys);

        if (!_orchestrationInputNormalizer.HasSingleSpecificRule(rules))
            throw new InvalidOperationException("A specific rule key is required to queue all matching issues");

        var context = await _enqueueContextResolver.ResolveAsync(
            request.MappingId,
            request.IssueType,
            request.Instructions,
            cancellationToken);

        var collected = await _enqueueAllIssuesReadService.CollectMatchingIssuesAsync(
            context.Mapping,
            context.Type.ToFilterList(),
            request.Severities,
            request.IssueStatuses,
            rules,
            MaxEnqueueAllScanIssues,
            cancellationToken);

        var enqueueResult = await EnqueueRawIssuesAsync(
            context.Mapping,
            context.Type,
            context.InstructionText,
            collected.Issues,
            cancellationToken);

        var result = OrchestrationResponseMapper.BuildEnqueueAllResult(
            collected.Matched,
            collected.Truncated,
            enqueueResult.CreatedItems,
            enqueueResult.Skipped);

        result.Requested = collected.Issues.Count;

        return result;
    }

    public async Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<QueueState> states, int? limit, CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Math.Clamp(limit.GetValueOrDefault(250), 1, 5000);

        return await _queueRepository.ListQueue(states, normalizedLimit, cancellationToken);
    }

    public async Task<QueueStatsDto> GetQueueStats(CancellationToken cancellationToken = default)
    {
        var stats = await _queueRepository.GetQueueStats(cancellationToken);

        return OrchestrationResponseMapper.BuildQueueStats(stats);
    }

    public async Task<OrchestrationWorkerStateDto> GetWorkerState(CancellationToken cancellationToken = default)
    {
        var backpressure = await EvaluateWorkloadBackpressure(false);

        var tasks = await _queueRepository.ListQueue(
            [
                QueueState.Leased,
                QueueState.Running
            ],
            5000,
            cancellationToken);

        var inFlightLeases = tasks.Count(task => task.QueueState == QueueState.Leased);
        var runningTasks = tasks.Count(task => task.QueueState == QueueState.Running);

        return _orchestrationStatusService.BuildWorkerState(
            inFlightLeases,
            runningTasks,
            _options.MaxActive,
            _options.PerProjectMaxActive,
            _options.LeaseSeconds,
            backpressure);
    }

    public async Task<bool> CancelQueueItem(int queueId, CancellationToken cancellationToken = default)
    {
        if (queueId <= 0)
            throw new InvalidOperationException("Invalid queue id");

        return await _queueRepository.CancelQueueItem(queueId, NowUtc(), cancellationToken);
    }

    public Task<int> RetryFailed(CancellationToken cancellationToken = default)
        => _queueRepository.RetryFailed(NowUtc(), cancellationToken);

    public Task<int> ClearQueued(CancellationToken cancellationToken = default)
        => _queueRepository.ClearQueued(NowUtc(), cancellationToken);

    public async Task<bool> ApproveTask(int taskId, CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            throw new InvalidOperationException("Invalid task id");

        var task = (await _queueRepository.ListQueue([QueueState.AwaitingReview], 5000, cancellationToken)).FirstOrDefault(item => item.Id == taskId);

        if (task is null)
            return false;

        if (!string.IsNullOrWhiteSpace(task.SessionId))
            await ArchiveSessionAsync(task.SessionId, task.Directory);

        cancellationToken.ThrowIfCancellationRequested();

        return await _queueRepository.ApproveTask(taskId, NowUtc(), cancellationToken);
    }

    public async Task<bool> RejectTask(int taskId, string? reason, CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            throw new InvalidOperationException("Invalid task id");

        return await _queueRepository.RejectTask(
            taskId,
            reason,
            NowUtc(),
            cancellationToken);
    }

    public async Task<bool> RequeueTask(int taskId, string? reason, CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            throw new InvalidOperationException("Invalid task id");

        return await _queueRepository.RequeueTask(
            taskId,
            reason,
            NowUtc(),
            cancellationToken);
    }

    public async Task<bool> RepromptTask(
        int taskId,
        string instructions,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            throw new InvalidOperationException("Invalid task id");

        var updatedInstructions = instructions?.Trim();

        if (string.IsNullOrWhiteSpace(updatedInstructions))
            throw new InvalidOperationException("Missing instructions");

        return await _queueRepository.RepromptTask(
            taskId,
            updatedInstructions,
            reason,
            NowUtc(),
            cancellationToken);
    }

    public async Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistory(int taskId, CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            throw new InvalidOperationException("Invalid task id");

        var history = await _queueRepository.GetTaskReviewHistory(taskId, cancellationToken);

        return history
            .Select(entry => new TaskReviewHistoryDto
            {
                Action = entry.ParsedAction,
                Reason = entry.Reason,
                CreatedAt = entry.CreatedAt
            })
            .ToList();
    }

    public async Task ResetState(CancellationToken cancellationToken = default)
    {
        await _persistence.ResetStateAsync();
        cancellationToken.ThrowIfCancellationRequested();
        _options.OnChange();
    }

    static DateTimeOffset NowUtc() => DateTimeOffset.UtcNow;

    public bool IsConfigured() => _orchestrationStatusService.IsConfigured(_options.SonarQubeService, _options.SonarUrl, _options.SonarToken);
    public async Task<MappingRecord?> GetMappingById(int? mappingId, CancellationToken cancellationToken = default) => await _orchestrationMappingService.GetMappingByIdAsync(mappingId, cancellationToken);

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

    async Task<QueueEnqueueBatchResult> EnqueueRawIssuesAsync(
        MappingRecord mapping,
        SonarIssueType type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues,
        CancellationToken cancellationToken)
    {
        var skipped = new List<QueueEnqueueSkipView>();

        var (createdItems, repoSkipped) = await _queueRepository.EnqueueIssuesBatch(
            mapping,
            type.OrNull(),
            instructionText,
            issues,
            _options.MaxAttempts,
            NowUtc(),
            cancellationToken);

        foreach (var item in repoSkipped)
        {
            skipped.Add(OrchestrationResponseMapper.BuildRepoSkip(item));
        }

        return new QueueEnqueueBatchResult(createdItems, skipped);
    }

    public static IReadOnlyList<QueueState> ParseQueueStates(string? statesCsv)
    {
        var result = new List<QueueState>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var csv = statesCsv ?? string.Empty;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!QueueState.TryParse(part, out var state) ||
                !seen.Add(state.Value))
                continue;

            result.Add(state);
        }

        return result;
    }

    async Task ProcessNextTaskAsync()
    {
        var leased = await _taskSchedulerService.LeaseNextTaskAsync(
            _leaseOwner,
            _options.MaxActive,
            _options.PerProjectMaxActive,
            _options.LeaseSeconds);

        if (leased is null)
            return;

        var issues = await _queueRepository.GetTaskIssues(leased.Id);
        var readiness = await _taskReadinessGate.EvaluateAsync(leased, issues);

        if (!readiness.IsReady)
        {
            await _queueRepository.MarkDispatchFailure(
                leased.Id,
                QueueState.Failed,
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
        if (Interlocked.CompareExchange(ref _tickRunning, 1, 0) != 0)
            return;

        try
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
        }
        finally
        {
            Interlocked.Exchange(ref _tickRunning, 0);
        }
    }
}
