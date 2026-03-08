using Dapper;
using Microsoft.Data.Sqlite;
using TaskViewer.OpenCode;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server;

public sealed class SonarOrchestrator : IOrchestrationGateway, IAsyncDisposable
{
    const int MaxEnqueueBatch = 1000;
    const int MaxRuleScanIssues = 5000;
    const int MaxEnqueueAllScanIssues = 20_000;
    readonly SemaphoreSlim _dbLock = new(1, 1);
    readonly ISonarEnqueueAllIssuesReadService _enqueueAllIssuesReadService;
    readonly IEnqueueContextResolver _enqueueContextResolver;
    readonly HashSet<string> _inFlight = [];
    readonly ISonarIssuesReadService _issuesReadService;
    readonly IMappingRepository _mappingRepository;

    readonly SonarOrchestratorOptions _options;
    readonly IOrchestrationInputNormalizer _orchestrationInputNormalizer;
    readonly IOrchestrationMappingService _orchestrationMappingService;
    readonly IOrchestrationStatusService _orchestrationStatusService;
    readonly IOrchestratorRuntime _orchestratorRuntime;
    readonly IQueueCommandsService _queueCommandsService;
    readonly IQueueEnqueueService _queueEnqueueService;
    readonly IQueueDispatchOrchestrationService _queueDispatchOrchestrationService;
    readonly IQueueQueryService _queueQueryService;
    readonly IQueueRepository _queueRepository;
    readonly IQueueWorkerCoordinator _queueWorkerCoordinator;
    readonly ISonarRuleReadService _ruleReadService;
    readonly ISonarRulesReadService _rulesReadService;
    readonly IWorkloadBackpressureStateService _workloadBackpressureStateService;
    volatile bool _disposed;

    public SonarOrchestrator(SonarOrchestratorOptions options)
    {
        _options = options;
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DbPath) ?? ".");
        InitializeSchema();
        _queueRepository = new SqliteQueueRepository(_dbLock, OpenConnection, _options.OnChange);
        _mappingRepository = new SqliteMappingRepository(_dbLock, OpenConnection, _options.OnChange);
        _orchestrationInputNormalizer = _options.OrchestrationInputNormalizer ?? new OrchestrationInputNormalizer();
        _orchestrationMappingService = _options.OrchestrationMappingService ?? new OrchestrationMappingService(_mappingRepository, _options.NormalizeDirectory, NowUtc);
        _orchestrationStatusService = _options.OrchestrationStatusService ?? new OrchestrationStatusService();
        _enqueueContextResolver = new EnqueueContextResolver(_mappingRepository);

        _ruleReadService = _options.SonarRuleReadService ??
                           (_options.SonarQubeService is not null
                               ? new CachedSonarRuleReadService(_options.SonarQubeService)
                               : new FallbackSonarRuleReadService());

        _rulesReadService = _options.SonarRulesReadService ??
                            (_options.SonarQubeService is not null
                                ? new SonarRulesReadService(_options.SonarQubeService, _ruleReadService)
                                : new DisabledSonarRulesReadService());

        _issuesReadService = _options.SonarIssuesReadService ??
                             (_options.SonarQubeService is not null
                                 ? new SonarIssuesReadService(_options.SonarQubeService)
                                 : new DisabledSonarIssuesReadService());

        _enqueueAllIssuesReadService = _options.SonarEnqueueAllIssuesReadService ??
                                       (_options.SonarQubeService is not null
                                           ? new SonarEnqueueAllIssuesReadService(_options.SonarQubeService)
                                           : new DisabledSonarEnqueueAllIssuesReadService());

        var openCodeStatusReader = _options.OpenCodeStatusReader ?? new DisabledOpenCodeStatusReader();
        var workingSessionsReadService = _options.WorkingSessionsReadService ?? new WorkingSessionsReadService(_mappingRepository, openCodeStatusReader);
        var openCodeDispatchClient = _options.OpenCodeDispatchClient ?? new DisabledOpenCodeDispatchClient();
        var queueDispatchService = _options.QueueDispatchService ?? new QueueDispatchService(openCodeDispatchClient, _options.BuildOpenCodeSessionUrl);
        var dispatchFailurePolicy = _options.DispatchFailurePolicy ?? new DispatchFailurePolicy();
        var workloadBackpressurePolicy = _options.WorkloadBackpressurePolicy ?? new WorkloadBackpressurePolicy();

        _queueDispatchOrchestrationService = _options.QueueDispatchOrchestrationService ??
        new QueueDispatchOrchestrationService(
            _queueRepository,
            queueDispatchService,
            dispatchFailurePolicy,
            NowUtc);

        _queueWorkerCoordinator = _options.QueueWorkerCoordinator ?? new QueueWorkerCoordinator();
        _orchestratorRuntime = _options.OrchestratorRuntime ?? new OrchestratorRuntime();
        _workloadBackpressureStateService = _options.WorkloadBackpressureStateService ?? new WorkloadBackpressureStateService(workingSessionsReadService, workloadBackpressurePolicy);
        _queueEnqueueService = _options.QueueEnqueueService ?? new QueueEnqueueService(_queueRepository, _options.MaxAttempts, NowUtc);
        _queueCommandsService = _options.QueueCommandsService ?? new QueueCommandsService(_queueRepository, NowUtc);
        _queueQueryService = _options.QueueQueryService ?? new QueueQueryService(_queueRepository);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _orchestratorRuntime.DisposeAsync();

        _dbLock.Dispose();
    }

    static DateTimeOffset NowUtc() => DateTimeOffset.UtcNow;

    SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var conn = new SqliteConnection(builder.ConnectionString);
        conn.Open();

        return conn;
    }

    void InitializeSchema()
    {
        using var conn = OpenConnection();
        conn.Execute(@"
      CREATE TABLE IF NOT EXISTS project_mappings (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        sonar_project_key TEXT NOT NULL UNIQUE,
        directory TEXT NOT NULL,
        branch TEXT,
        enabled INTEGER NOT NULL DEFAULT 1,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS instruction_profiles (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        mapping_id INTEGER NOT NULL,
        issue_type TEXT NOT NULL,
        instructions TEXT NOT NULL,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        UNIQUE(mapping_id, issue_type)
      );

      CREATE TABLE IF NOT EXISTS queue_items (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        issue_key TEXT NOT NULL,
        mapping_id INTEGER NOT NULL,
        sonar_project_key TEXT NOT NULL,
        directory TEXT NOT NULL,
        branch TEXT,
        issue_type TEXT,
        severity TEXT,
        rule TEXT,
        message TEXT,
        component TEXT,
        relative_path TEXT,
        absolute_path TEXT,
        line INTEGER,
        issue_status TEXT,
        instructions_snapshot TEXT,
        state TEXT NOT NULL,
        attempt_count INTEGER NOT NULL DEFAULT 0,
        max_attempts INTEGER NOT NULL DEFAULT 3,
        next_attempt_at TEXT,
        session_id TEXT,
        open_code_url TEXT,
        last_error TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        dispatched_at TEXT,
        completed_at TEXT,
        cancelled_at TEXT
      );

      CREATE INDEX IF NOT EXISTS idx_queue_state_next_attempt ON queue_items(state, next_attempt_at, created_at);
      CREATE INDEX IF NOT EXISTS idx_queue_issue_key ON queue_items(issue_key);
      CREATE INDEX IF NOT EXISTS idx_queue_mapping_state ON queue_items(mapping_id, state, created_at);
    ");
    }

    public bool IsConfigured()
    {
        return _orchestrationStatusService.IsConfigured(_options.SonarQubeService, _options.SonarUrl, _options.SonarToken);
    }

    public OrchestrationConfigDto GetPublicConfig()
    {
        return _orchestrationStatusService.BuildPublicConfig(
            IsConfigured(),
            _options.MaxActive,
            _options.PollMs,
            _options.MaxAttempts,
            _options.MaxWorkingGlobal,
            _options.WorkingResumeBelow);
    }

    public async Task<List<MappingRecord>> ListMappings() => await _orchestrationMappingService.ListMappingsAsync();

    public async Task<MappingRecord?> GetMappingById(int? mappingId) => await _orchestrationMappingService.GetMappingByIdAsync(mappingId);

    public async Task<MappingRecord> UpsertMapping(UpsertMappingRequest request) => await _orchestrationMappingService.UpsertMappingAsync(request);

    public async Task<InstructionProfileRecord?> GetInstructionProfile(int? mappingId, string? issueType) => await _orchestrationMappingService.GetInstructionProfileAsync(mappingId, issueType);

    public async Task<InstructionProfileRecord> UpsertInstructionProfile(UpsertInstructionProfileRequest request) => await _orchestrationMappingService.UpsertInstructionProfileAsync(request);

    public async Task<RulesListDto> ListRules(int? mappingId, string? issueType, string? issueStatus)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var summary = await _rulesReadService.SummarizeRulesAsync(
            mapping,
            issueType,
            issueStatus,
            MaxRuleScanIssues);

        return OrchestrationResponseMapper.BuildRulesList(mapping, summary);
    }

    public async Task<IssuesListDto> ListIssues(
        int? mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        string? page,
        string? pageSize,
        string? ruleKeys)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var rules = _orchestrationInputNormalizer.NormalizeRuleKeys(ruleKeys);
        var (p, ps) = _orchestrationInputNormalizer.ParseIssuePaging(page, pageSize);

        var result = await _issuesReadService.ListIssuesAsync(
            mapping,
            issueType,
            severity,
            issueStatus,
            p,
            ps,
            rules);

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

        var enqueueResult = await _queueEnqueueService.EnqueueRawIssuesAsync(
            context.Mapping,
            context.Type,
            context.InstructionText,
            normalizedIssues);

        if (enqueueResult.CreatedItems.Count > 0)
            _options.OnChange();

        var skipped = enqueueResult.Skipped.ToList();

        for (var i = 0; i < invalidCount; i++)
            skipped.Add(OrchestrationResponseMapper.BuildInvalidIssueSkip());

        return OrchestrationResponseMapper.BuildEnqueueIssuesResult(enqueueResult.CreatedItems, skipped);
    }

    public async Task<EnqueueAllResultDto> EnqueueAllMatching(EnqueueAllRequest request)
    {
        var rules = _orchestrationInputNormalizer.NormalizeRuleKeys(request.RuleKeys);
        var hasSingleSpecificRule = _orchestrationInputNormalizer.HasSingleSpecificRule(rules);

        if (!hasSingleSpecificRule)
            throw new InvalidOperationException("A specific rule key is required to queue all matching issues");

        var context = await _enqueueContextResolver.ResolveAsync(request.MappingId, request.IssueType, request.Instructions);

        var collected = await _enqueueAllIssuesReadService.CollectMatchingIssuesAsync(
            context.Mapping,
            context.Type,
            request.Severity,
            request.IssueStatus,
            rules,
            MaxEnqueueAllScanIssues);

        var enqueueResult = await _queueEnqueueService.EnqueueRawIssuesAsync(
            context.Mapping,
            context.Type,
            context.InstructionText,
            collected.Issues);

        if (enqueueResult.CreatedItems.Count > 0)
            _options.OnChange();

        return OrchestrationResponseMapper.BuildEnqueueAllResult(
            collected.Matched,
            collected.Truncated,
            enqueueResult.CreatedItems,
            enqueueResult.Skipped);
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

        return _orchestrationStatusService.BuildWorkerState(_inFlight.Count, _options.MaxActive, backpressure);
    }

    public async Task<bool> CancelQueueItem(int? queueId) => await _queueCommandsService.CancelQueueItemAsync(queueId);

    public async Task<int> RetryFailed() => await _queueCommandsService.RetryFailedAsync();

    public async Task<int> ClearQueued() => await _queueCommandsService.ClearQueuedAsync();

    async Task<WorkloadBackpressureState> EvaluateWorkloadBackpressure(bool forceRefresh)
    {
        return await _workloadBackpressureStateService.EvaluateAsync(
            forceRefresh,
            _options.MaxWorkingGlobal,
            _options.WorkingResumeBelow,
            _options.PollMs,
            _options.OnChange);
    }

    async Task<QueueItemRecord?> ClaimNextQueuedItem() => await _queueRepository.ClaimNextQueuedItem(NowUtc());

    async Task DispatchQueueItem(QueueItemRecord item) => await _queueDispatchOrchestrationService.DispatchAndPersistAsync(item);

    public async Task Tick()
    {
        await _orchestratorRuntime.RunOnceAsync(async () =>
        {
            if (_disposed)
                return;

            if (!IsConfigured())
                return;

            var workload = await EvaluateWorkloadBackpressure(true);

            if (workload.Paused)
                return;

            await _queueWorkerCoordinator.ScheduleAsync(
                _inFlight,
                _options.MaxActive,
                ClaimNextQueuedItem,
                DispatchQueueItem,
                _options.OnChange);
        });
    }

    public void Start(CancellationToken stoppingToken) => _orchestratorRuntime.Start(_options.PollMs, stoppingToken, Tick);
}
