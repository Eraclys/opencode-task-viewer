using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server;

public sealed class SonarOrchestrator : IAsyncDisposable
{
    const int MaxEnqueueBatch = 1000;
    const int MaxRuleScanIssues = 5000;
    const int MaxEnqueueAllScanIssues = 20_000;
    readonly SemaphoreSlim _dbLock = new(1, 1);
    readonly HashSet<string> _inFlight = [];

    readonly SonarOrchestratorOptions _options;
    readonly IQueueRepository _queueRepository;
    readonly IMappingRepository _mappingRepository;
    readonly IOrchestrationMappingService _orchestrationMappingService;
    readonly IEnqueueContextResolver _enqueueContextResolver;
    readonly ISonarRuleReadService _ruleReadService;
    readonly ISonarRulesReadService _rulesReadService;
    readonly ISonarIssuesReadService _issuesReadService;
    readonly ISonarEnqueueAllIssuesReadService _enqueueAllIssuesReadService;
    readonly IWorkingSessionsReadService _workingSessionsReadService;
    readonly IQueueDispatchOrchestrationService _queueDispatchOrchestrationService;
    readonly IQueueWorkerCoordinator _queueWorkerCoordinator;
    readonly IOrchestratorRuntime _orchestratorRuntime;
    readonly IWorkloadBackpressurePolicy _workloadBackpressurePolicy;
    readonly IQueueCommandsService _queueCommandsService;
    readonly IQueueQueryService _queueQueryService;
    volatile bool _disposed;
    DateTimeOffset? _latestWorkingSampleAt;
    volatile bool _workloadPaused;

    public SonarOrchestrator(SonarOrchestratorOptions options)
    {
        _options = options;
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DbPath) ?? ".");
        InitializeSchema();
        _queueRepository = new SqliteQueueRepository(_dbLock, OpenConnection, _options.OnChange);
        _mappingRepository = new SqliteMappingRepository(_dbLock, OpenConnection, _options.OnChange);
        _orchestrationMappingService = _options.OrchestrationMappingService
            ?? new OrchestrationMappingService(_mappingRepository, _options.NormalizeDirectory, NowIso);
        _enqueueContextResolver = new EnqueueContextResolver(_mappingRepository);
        _ruleReadService = _options.SonarRuleReadService
            ?? (_options.SonarGateway is not null
                ? new CachedSonarRuleReadService(_options.SonarGateway)
                : new FallbackSonarRuleReadService());
        _rulesReadService = _options.SonarRulesReadService
            ?? (_options.SonarGateway is not null
                ? new SonarRulesReadService(_options.SonarGateway, _ruleReadService)
                : new DisabledSonarRulesReadService());
        _issuesReadService = _options.SonarIssuesReadService
            ?? (_options.SonarGateway is not null
                ? new SonarIssuesReadService(_options.SonarGateway)
                : new DisabledSonarIssuesReadService());
        _enqueueAllIssuesReadService = _options.SonarEnqueueAllIssuesReadService
            ?? (_options.SonarGateway is not null
                ? new SonarEnqueueAllIssuesReadService(_options.SonarGateway)
                : new DisabledSonarEnqueueAllIssuesReadService());
        _workingSessionsReadService = _options.WorkingSessionsReadService
            ?? new WorkingSessionsReadService(_mappingRepository, _options.OpenCodeFetch);
        var queueDispatchService = _options.QueueDispatchService
            ?? new QueueDispatchService(_options.OpenCodeFetch, _options.BuildOpenCodeSessionUrl);
        var dispatchFailurePolicy = _options.DispatchFailurePolicy
            ?? new DispatchFailurePolicy();
        _queueDispatchOrchestrationService = _options.QueueDispatchOrchestrationService
            ?? new QueueDispatchOrchestrationService(_queueRepository, queueDispatchService, dispatchFailurePolicy, NowIso);
        _queueWorkerCoordinator = _options.QueueWorkerCoordinator
            ?? new QueueWorkerCoordinator();
        _orchestratorRuntime = _options.OrchestratorRuntime
            ?? new OrchestratorRuntime();
        _workloadBackpressurePolicy = _options.WorkloadBackpressurePolicy
            ?? new WorkloadBackpressurePolicy();
        _queueCommandsService = _options.QueueCommandsService
            ?? new QueueCommandsService(_queueRepository, NowIso);
        _queueQueryService = _options.QueueQueryService
            ?? new QueueQueryService(_queueRepository);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _orchestratorRuntime.DisposeAsync();

        _dbLock.Dispose();
    }

    static string NowIso() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    static int ParseIntSafe(object? value, int fallback)
    {
        if (value is null)
            return fallback;

        if (value is int i)
            return i;

        if (value is long l &&
            l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        var s = Convert.ToString(value, CultureInfo.InvariantCulture);

        return int.TryParse(
            s,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var n)
            ? n
            : fallback;
    }

    static List<string> NormalizeRuleKeys(object? value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        if (value is JsonArray arr)
        {
            foreach (var n in arr)
            {
                var key = n?.ToString()?.Trim();

                if (!string.IsNullOrWhiteSpace(key))
                    set.Add(key);
            }

            return [.. set];
        }

        var csv = value?.ToString() ?? string.Empty;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                set.Add(part);
        }

        return [.. set];
    }

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
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
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
    ";

        cmd.ExecuteNonQuery();
    }

    public bool IsConfigured()
    {
        return _options.SonarGateway is not null
               || (!string.IsNullOrWhiteSpace(_options.SonarUrl) && !string.IsNullOrWhiteSpace(_options.SonarToken));
    }

    public object GetPublicConfig()
    {
        return new
        {
            configured = IsConfigured(),
            maxActive = _options.MaxActive,
            pollMs = _options.PollMs,
            maxAttempts = _options.MaxAttempts,
            maxWorkingGlobal = _options.MaxWorkingGlobal,
            workingResumeBelow = _options.WorkingResumeBelow
        };
    }

    public async Task<List<MappingRecord>> ListMappings()
    {
        return await _orchestrationMappingService.ListMappingsAsync();
    }

    public async Task<MappingRecord?> GetMappingById(object? mappingId)
    {
        return await _orchestrationMappingService.GetMappingByIdAsync(mappingId);
    }

    public async Task<MappingRecord> UpsertMapping(JsonNode? payload)
    {
        return await _orchestrationMappingService.UpsertMappingAsync(payload);
    }

    public async Task<JsonObject?> GetInstructionProfile(object? mappingId, string? issueType)
    {
        return await _orchestrationMappingService.GetInstructionProfileAsync(mappingId, issueType);
    }

    public async Task<JsonObject> UpsertInstructionProfile(object? mappingId, string? issueType, string? instructions)
    {
        return await _orchestrationMappingService.UpsertInstructionProfileAsync(mappingId, issueType, instructions);
    }

    public async Task<object> ListRules(object? mappingId, string? issueType, string? issueStatus)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var summary = await _rulesReadService.SummarizeRulesAsync(mapping, issueType, issueStatus, MaxRuleScanIssues);

        return OrchestrationResponseMapper.BuildRulesList(mapping, summary);
    }

    public async Task<object> ListIssues(
        object? mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        object? page,
        object? pageSize,
        object? ruleKeys)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var rules = NormalizeRuleKeys(ruleKeys);
        var p = Math.Clamp(ParseIntSafe(page, 1), 1, int.MaxValue);
        var ps = Math.Clamp(ParseIntSafe(pageSize, 100), 1, 500);
        var result = await _issuesReadService.ListIssuesAsync(mapping, issueType, severity, issueStatus, p, ps, rules);

        return OrchestrationResponseMapper.BuildIssuesList(mapping, result);
    }

    async Task<(List<QueueItemRecord> CreatedItems, List<QueueEnqueueSkipView> Skipped)> EnqueueRawIssues(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<JsonNode?> rawIssues)
    {
        var normalizedIssues = new List<NormalizedIssue>();
        var skipped = new List<QueueEnqueueSkipView>();
        var now = NowIso();

        foreach (var rawIssue in rawIssues)
        {
            var issue = SonarIssueNormalizer.NormalizeForQueue(rawIssue, mapping);

            if (issue is null)
            {
                skipped.Add(OrchestrationResponseMapper.BuildInvalidIssueSkip());

                continue;
            }

            normalizedIssues.Add(issue);
        }

        var (createdItems, repoSkipped) = await _queueRepository.EnqueueIssuesBatch(
            mapping,
            type,
            instructionText,
            normalizedIssues,
            _options.MaxAttempts,
            now);

        foreach (var item in repoSkipped)
            skipped.Add(OrchestrationResponseMapper.BuildRepoSkip(item));

        return (createdItems, skipped);
    }

    public async Task<object> EnqueueIssues(
        object? mappingId,
        string? issueType,
        string? instructions,
        JsonArray? issues)
    {
        var rawIssues = issues?.Take(MaxEnqueueBatch).ToList() ?? [];

        if (rawIssues.Count == 0)
            throw new InvalidOperationException("No issues provided");

        var context = await _enqueueContextResolver.ResolveAsync(mappingId, issueType, instructions);

        var (createdItems, skipped) = await EnqueueRawIssues(
            context.Mapping,
            context.Type,
            context.InstructionText,
            rawIssues);

        if (createdItems.Count > 0)
            _options.OnChange();

        return OrchestrationResponseMapper.BuildEnqueueIssuesResult(createdItems, skipped);
    }

    public async Task<object> EnqueueAllMatching(
        object? mappingId,
        string? issueType,
        object? ruleKeys,
        string? issueStatus,
        string? severity,
        string? instructions)
    {
        var rules = NormalizeRuleKeys(ruleKeys);
        var hasSingleSpecificRule = rules.Count == 1 && !string.Equals(rules[0], "all", StringComparison.OrdinalIgnoreCase);

        if (!hasSingleSpecificRule)
            throw new InvalidOperationException("A specific rule key is required to queue all matching issues");

        var context = await _enqueueContextResolver.ResolveAsync(mappingId, issueType, instructions);

        var collected = await _enqueueAllIssuesReadService.CollectMatchingIssuesAsync(
            context.Mapping,
            context.Type,
            severity,
            issueStatus,
            rules,
            MaxEnqueueAllScanIssues);

        var (createdItems, skipped) = await EnqueueRawIssues(
            context.Mapping,
            context.Type,
            context.InstructionText,
            collected.Issues);

        if (createdItems.Count > 0)
            _options.OnChange();

        return OrchestrationResponseMapper.BuildEnqueueAllResult(collected.Matched, collected.Truncated, createdItems, skipped);
    }

    public async Task<List<QueueItemRecord>> ListQueue(object? states, object? limit)
    {
        return await _queueQueryService.ListQueueAsync(states, limit);
    }

    public async Task<object> GetQueueStats()
    {
        var stats = await _queueQueryService.GetQueueStatsAsync();

        return OrchestrationResponseMapper.BuildQueueStats(stats);
    }

    public async Task<object> GetWorkerState()
    {
        var backpressure = await EvaluateWorkloadBackpressure(false);

        return OrchestrationResponseMapper.BuildWorkerState(
            _inFlight.Count,
            _options.MaxActive,
            backpressure.Paused,
            backpressure.WorkingCount,
            _options.MaxWorkingGlobal,
            _options.WorkingResumeBelow,
            backpressure.SampleAt);
    }

    public async Task<bool> CancelQueueItem(object? queueId)
    {
        return await _queueCommandsService.CancelQueueItemAsync(queueId);
    }

    public async Task<int> RetryFailed()
    {
        return await _queueCommandsService.RetryFailedAsync();
    }

    public async Task<int> ClearQueued()
    {
        return await _queueCommandsService.ClearQueuedAsync();
    }

    async Task<(bool Paused, int WorkingCount, int MaxWorkingGlobal, int WorkingResumeBelow, string? SampleAt)> EvaluateWorkloadBackpressure(bool forceRefresh)
    {
        if (_options.MaxWorkingGlobal <= 0)
        {
            _workloadPaused = false;

            return (false, 0, _options.MaxWorkingGlobal, _options.WorkingResumeBelow, _latestWorkingSampleAt?.ToString("O"));
        }

        var sample = await _workingSessionsReadService.GetWorkingSessionsCountAsync(forceRefresh, _options.PollMs);
        _latestWorkingSampleAt = sample.SampledAt;
        var count = sample.Count;
        var transition = _workloadBackpressurePolicy.Evaluate(
            _workloadPaused,
            count,
            _options.MaxWorkingGlobal,
            _options.WorkingResumeBelow);

        if (transition.Changed)
        {
            _workloadPaused = transition.NextPaused;
            _options.OnChange();
        }
        else
        {
            _workloadPaused = transition.NextPaused;
        }

        return (_workloadPaused, count, _options.MaxWorkingGlobal, _options.WorkingResumeBelow, _latestWorkingSampleAt?.ToString("O"));
    }

    async Task<QueueItemRecord?> ClaimNextQueuedItem()
    {
        return await _queueRepository.ClaimNextQueuedItem(NowIso());
    }

    async Task DispatchQueueItem(QueueItemRecord item)
    {
        await _queueDispatchOrchestrationService.DispatchAndPersistAsync(item);
    }

    public async Task Tick()
    {
        await _orchestratorRuntime.RunOnceAsync(
            async () =>
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

    public void Start(CancellationToken stoppingToken)
    {
        _orchestratorRuntime.Start(_options.PollMs, stoppingToken, Tick);
    }
}
