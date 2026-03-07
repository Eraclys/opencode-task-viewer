using System.Collections.Concurrent;
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
    readonly ConcurrentDictionary<string, string> _ruleNameCache = new(StringComparer.OrdinalIgnoreCase);
    (DateTimeOffset Ts, int Count) _cachedWorkingSample = (DateTimeOffset.MinValue, 0);
    volatile bool _disposed;
    volatile int _latestWorkingCount;
    DateTimeOffset? _latestWorkingSampleAt;
    Task? _loopTask;
    volatile bool _tickRunning;
    PeriodicTimer? _timer;
    volatile bool _workloadPaused;

    public SonarOrchestrator(SonarOrchestratorOptions options)
    {
        _options = options;
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DbPath) ?? ".");
        InitializeSchema();
        _queueRepository = new SqliteQueueRepository(_dbLock, OpenConnection, _options.OnChange);
        _mappingRepository = new SqliteMappingRepository(_dbLock, OpenConnection, _options.OnChange);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_timer is not null)
        {
            _timer.Dispose();
            _timer = null;
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch
            {
            }
        }

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

    static int? ParseIntNullable(object? value)
    {
        if (value is null)
            return null;

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
            : null;
    }

    static string? NormalizeIssueType(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(v) ? null : v;
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

    static List<string> NormalizeQueueStateList(object? states)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "queued",
            "dispatching",
            "session_created",
            "done",
            "failed",
            "cancelled"
        };

        var result = new HashSet<string>(StringComparer.Ordinal);

        if (states is JsonArray a)
        {
            foreach (var n in a)
            {
                var v = n?.ToString()?.Trim().ToLowerInvariant();

                if (!string.IsNullOrWhiteSpace(v) &&
                    allowed.Contains(v))
                    result.Add(v);
            }

            return [.. result];
        }

        var csv = states?.ToString() ?? string.Empty;

        foreach (var p in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var v = p.ToLowerInvariant();

            if (allowed.Contains(v))
                result.Add(v);
        }

        return [.. result];
    }

    static bool IsRunningStatusType(string? value)
    {
        var t = (value ?? string.Empty).Trim().ToLowerInvariant();

        return t is "busy" or "retry" or "running";
    }

    static int MakeBackoffMs(int attempt)
    {
        var n = Math.Max(1, attempt);
        var backoff = 2500 * Math.Pow(2, n - 1);

        return (int)Math.Min(60_000, backoff);
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
        return await _mappingRepository.ListMappings();
    }

    public async Task<MappingRecord?> GetMappingById(object? mappingId)
    {
        var id = ParseIntSafe(mappingId, -1);

        if (id <= 0)
            return null;

        return await _mappingRepository.GetMappingById(id);
    }

    public async Task<MappingRecord> UpsertMapping(JsonNode? payload)
    {
        var sonarProjectKey = payload?["sonarProjectKey"]?.ToString()?.Trim() ?? payload?["sonar_project_key"]?.ToString()?.Trim() ?? string.Empty;
        var directory = payload?["directory"]?.ToString()?.Trim() ?? string.Empty;
        var branch = payload?["branch"]?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(branch))
            branch = null;

        var enabled = payload?["enabled"] is null || payload?["enabled"]?.GetValue<bool>() != false;

        if (string.IsNullOrWhiteSpace(sonarProjectKey))
            throw new InvalidOperationException("Missing sonarProjectKey");

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Missing directory");

        directory = _options.NormalizeDirectory(directory) ?? directory.Replace('\\', '/');
        var id = ParseIntSafe(payload?["id"]?.ToString(), -1);
        var result = await _mappingRepository.UpsertMapping(
            id > 0 ? id : null,
            sonarProjectKey,
            directory,
            branch,
            enabled,
            NowIso());

        return result;
    }

    public async Task<JsonObject?> GetInstructionProfile(object? mappingId, string? issueType)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null)
            return null;

        var type = NormalizeIssueType(issueType);

        if (type is null)
            return null;

        return await _mappingRepository.GetInstructionProfile(mapping.Id, type);
    }

    public async Task<JsonObject> UpsertInstructionProfile(object? mappingId, string? issueType, string? instructions)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null)
            throw new InvalidOperationException("Mapping not found");

        var type = NormalizeIssueType(issueType);

        if (type is null)
            throw new InvalidOperationException("Missing issueType");

        var text = (instructions ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Missing instructions");

        return await _mappingRepository.UpsertInstructionProfile(mapping.Id, type, text, NowIso());
    }

    async Task<JsonNode?> SonarFetch(string endpointPath, Dictionary<string, string?> query)
    {
        if (_options.SonarGateway is not null)
            return await _options.SonarGateway.Fetch(endpointPath, query);

        throw new InvalidOperationException("SonarQube is not configured");
    }

    async Task<string> GetRuleDisplayName(string key)
    {
        var ruleKey = (key ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(ruleKey))
            return string.Empty;

        if (_ruleNameCache.TryGetValue(ruleKey, out var cached))
            return cached;

        try
        {
            var data = await SonarFetch(
                "/api/rules/show",
                new Dictionary<string, string?>
                {
                    ["key"] = ruleKey
                });

            var name = data?["rule"]?["name"]?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(name))
                name = ruleKey;

            _ruleNameCache[ruleKey] = name;

            return name;
        }
        catch
        {
            _ruleNameCache[ruleKey] = ruleKey;

            return ruleKey;
        }
    }

    static NormalizedIssue? NormalizeIssueForQueue(JsonNode? rawNode, MappingRecord mapping)
    {
        if (rawNode is not JsonObject raw)
            return null;

        var key = raw["key"]?.ToString()?.Trim() ?? raw["issueKey"]?.ToString()?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(key))
            return null;

        var type = NormalizeIssueType(raw["type"]?.ToString() ?? raw["issueType"]?.ToString() ?? "CODE_SMELL") ?? "CODE_SMELL";
        var severity = raw["severity"]?.ToString()?.Trim()?.ToUpperInvariant();
        var rule = raw["rule"]?.ToString()?.Trim();
        var message = raw["message"]?.ToString()?.Trim();
        var line = ParseIntNullable(raw["line"]?.ToString());
        var status = raw["status"]?.ToString()?.Trim();
        var component = raw["component"]?.ToString()?.Trim() ?? raw["file"]?.ToString()?.Trim();

        var projectKey = mapping.SonarProjectKey?.Trim() ?? string.Empty;
        string? relativePath = null;

        if (!string.IsNullOrWhiteSpace(component))
        {
            if (!string.IsNullOrWhiteSpace(projectKey) &&
                component.StartsWith(projectKey + ":", StringComparison.Ordinal))
                relativePath = component[(projectKey.Length + 1)..];
            else
            {
                var idx = component.IndexOf(':');
                relativePath = idx >= 0 ? component[(idx + 1)..] : component;
            }
        }

        relativePath = relativePath?.Replace('\\', '/').TrimStart('/');

        var absolutePath = !string.IsNullOrWhiteSpace(relativePath)
            ? $"{mapping.Directory.TrimEnd('/')}/{relativePath}"
            : null;

        return new NormalizedIssue
        {
            Key = key,
            Type = type,
            Severity = string.IsNullOrWhiteSpace(severity) ? null : severity,
            Rule = string.IsNullOrWhiteSpace(rule) ? null : rule,
            Message = string.IsNullOrWhiteSpace(message) ? null : message,
            Line = line,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            Component = string.IsNullOrWhiteSpace(component) ? null : component,
            RelativePath = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath,
            AbsolutePath = string.IsNullOrWhiteSpace(absolutePath) ? null : absolutePath
        };
    }

    public async Task<object> ListRules(object? mappingId, string? issueType, string? issueStatus)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var type = NormalizeIssueType(issueType);
        var status = (issueStatus ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(status))
            status = string.Empty;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var pageSize = 500;
        var page = 1;
        var scanned = 0;
        int? total = null;

        while (true)
        {
            var query = new Dictionary<string, string?>
            {
                ["componentKeys"] = mapping.SonarProjectKey,
                ["p"] = page.ToString(CultureInfo.InvariantCulture),
                ["ps"] = pageSize.ToString(CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrWhiteSpace(type))
                query["types"] = type;

            if (!string.IsNullOrWhiteSpace(status))
                query["statuses"] = status;

            if (!string.IsNullOrWhiteSpace(mapping.Branch))
                query["branch"] = mapping.Branch;

            var data = await SonarFetch("/api/issues/search", query);
            var issues = data?["issues"] as JsonArray ?? [];
            total ??= ParseIntNullable(data?["paging"]?["total"]?.ToString());

            foreach (var issueNode in issues)
            {
                var key = issueNode?["rule"]?.ToString()?.Trim();

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                counts.TryGetValue(key, out var current);
                counts[key] = current + 1;
                scanned += 1;
            }

            var endReached = issues.Count < pageSize || (total.HasValue && page * pageSize >= total.Value) || scanned >= MaxRuleScanIssues;

            if (endReached)
                break;

            page += 1;
        }

        var rules = new List<object>();

        foreach (var key in counts.Keys)
        {
            var name = await GetRuleDisplayName(key);

            rules.Add(
                new
                {
                    key,
                    name = string.IsNullOrWhiteSpace(name) ? key : name,
                    count = counts[key]
                });
        }

        rules = rules
            .OrderByDescending(x => (int)x.GetType().GetProperty("count")!.GetValue(x)!)
            .ThenBy(x => (string)x.GetType().GetProperty("name")!.GetValue(x)!, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => (string)x.GetType().GetProperty("key")!.GetValue(x)!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new
        {
            mapping,
            issueType = string.IsNullOrWhiteSpace(type) ? null : type,
            issueStatus = string.IsNullOrWhiteSpace(status) ? null : status,
            scannedIssues = scanned,
            truncated = scanned >= MaxRuleScanIssues,
            rules
        };
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

        var type = NormalizeIssueType(issueType);
        var sev = (severity ?? string.Empty).Trim().ToUpperInvariant();
        var status = (issueStatus ?? string.Empty).Trim().ToUpperInvariant();
        var rules = NormalizeRuleKeys(ruleKeys);
        var p = Math.Clamp(ParseIntSafe(page, 1), 1, int.MaxValue);
        var ps = Math.Clamp(ParseIntSafe(pageSize, 100), 1, 500);

        var query = SonarIssuesQueryBuilder.Build(mapping, p, ps, type, sev, status, rules);

        var data = await SonarFetch("/api/issues/search", query);
        var rawIssues = data?["issues"] as JsonArray ?? [];
        var issues = new List<object>();

        foreach (var raw in rawIssues)
        {
            var issue = NormalizeIssueForQueue(raw, mapping);

            if (issue is null)
                continue;

            issues.Add(
                new
                {
                    key = issue.Key,
                    type = issue.Type,
                    severity = issue.Severity,
                    rule = issue.Rule,
                    message = issue.Message,
                    component = issue.Component,
                    line = issue.Line,
                    status = issue.Status,
                    relativePath = issue.RelativePath,
                    absolutePath = issue.AbsolutePath
                });
        }

        var pageIndex = ParseIntSafe(data?["paging"]?["pageIndex"]?.ToString(), p);
        var psize = ParseIntSafe(data?["paging"]?["pageSize"]?.ToString(), ps);
        var total = ParseIntSafe(data?["paging"]?["total"]?.ToString(), issues.Count);

        return new
        {
            mapping,
            paging = new
            {
                pageIndex,
                pageSize = psize,
                total
            },
            issues
        };
    }

    async Task<(MappingRecord Mapping, string? Type, string InstructionText)> ResolveEnqueueContext(object? mappingId, string? issueType, string? instructions)
    {
        var mapping = await GetMappingById(mappingId);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var type = NormalizeIssueType(issueType);
        var profile = await GetInstructionProfile(mapping.Id, type ?? string.Empty);
        var defaultInstruction = profile?["instructions"]?.ToString();
        var instructionText = EnqueueContextPolicy.ResolveInstructionText(instructions, defaultInstruction);

        if (EnqueueContextPolicy.ShouldPersistInstructionProfile(type, instructionText))
            await UpsertInstructionProfile(mapping.Id, type, instructionText);

        return (mapping, type, instructionText);
    }

    async Task<(List<QueueItemRecord> CreatedItems, List<object> Skipped)> EnqueueRawIssues(
        MappingRecord mapping,
        string? type,
        string instructionText,
        List<JsonNode?> rawIssues)
    {
        var normalizedIssues = new List<NormalizedIssue>();
        var skipped = new List<object>();
        var now = NowIso();

        foreach (var rawIssue in rawIssues)
        {
            var issue = NormalizeIssueForQueue(rawIssue, mapping);

            if (issue is null)
            {
                skipped.Add(
                    new
                    {
                        issueKey = (string?)null,
                        reason = "invalid-issue"
                    });

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
        {
            skipped.Add(
                new
                {
                    issueKey = item.IssueKey,
                    reason = item.Reason
                });
        }

        return (createdItems, skipped);
    }

    async Task<(List<JsonNode?> Issues, int Matched, bool Truncated)> CollectIssuesForEnqueueAll(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        List<string> ruleKeys)
    {
        var type = NormalizeIssueType(issueType);
        var sev = (severity ?? string.Empty).Trim().ToUpperInvariant();
        var status = (issueStatus ?? string.Empty).Trim().ToUpperInvariant();

        var pageSize = 500;
        var page = 1;
        int? total = null;
        var allIssues = new List<JsonNode?>();

        while (allIssues.Count < MaxEnqueueAllScanIssues)
        {
            var query = SonarIssuesQueryBuilder.Build(mapping, page, pageSize, type, sev, status, ruleKeys);

            var data = await SonarFetch("/api/issues/search", query);
            total ??= ParseIntNullable(data?["paging"]?["total"]?.ToString());
            var issuesRaw = data?["issues"] as JsonArray ?? [];

            foreach (var issue in issuesRaw)
            {
                if (allIssues.Count >= MaxEnqueueAllScanIssues)
                    break;

                allIssues.Add(issue);
            }

            var endReached = issuesRaw.Count < pageSize || (total.HasValue && page * pageSize >= total.Value) || allIssues.Count >= MaxEnqueueAllScanIssues;

            if (endReached)
                break;

            page += 1;
        }

        return (allIssues, total ?? allIssues.Count, allIssues.Count >= MaxEnqueueAllScanIssues);
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

        var context = await ResolveEnqueueContext(mappingId, issueType, instructions);

        var (createdItems, skipped) = await EnqueueRawIssues(
            context.Mapping,
            context.Type,
            context.InstructionText,
            rawIssues);

        if (createdItems.Count > 0)
            _options.OnChange();

        return new
        {
            created = createdItems.Count,
            skipped,
            items = createdItems
        };
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

        var context = await ResolveEnqueueContext(mappingId, issueType, instructions);

        var collected = await CollectIssuesForEnqueueAll(
            context.Mapping,
            context.Type,
            severity,
            issueStatus,
            rules);

        var (createdItems, skipped) = await EnqueueRawIssues(
            context.Mapping,
            context.Type,
            context.InstructionText,
            collected.Issues);

        if (createdItems.Count > 0)
            _options.OnChange();

        return new
        {
            matched = collected.Matched,
            created = createdItems.Count,
            skipped,
            truncated = collected.Truncated,
            items = createdItems
        };
    }

    public async Task<List<QueueItemRecord>> ListQueue(object? states, object? limit)
    {
        var selectedStates = NormalizeQueueStateList(states);
        var n = Math.Clamp(ParseIntSafe(limit, 250), 1, 5000);
        return await _queueRepository.ListQueue(selectedStates, n);
    }

    public async Task<object> GetQueueStats()
    {
        var stats = await _queueRepository.GetQueueStats();

        return new
        {
            queued = stats.Queued,
            dispatching = stats.Dispatching,
            session_created = stats.SessionCreated,
            done = stats.Done,
            failed = stats.Failed,
            cancelled = stats.Cancelled
        };
    }

    public async Task<object> GetWorkerState()
    {
        var backpressure = await EvaluateWorkloadBackpressure(false);

        return new
        {
            inFlightDispatches = _inFlight.Count,
            maxActiveDispatches = _options.MaxActive,
            pausedByWorking = backpressure.Paused,
            workingCount = backpressure.WorkingCount,
            maxWorkingGlobal = _options.MaxWorkingGlobal,
            workingResumeBelow = _options.WorkingResumeBelow,
            workingSampleAt = backpressure.SampleAt
        };
    }

    public async Task<bool> CancelQueueItem(object? queueId)
    {
        var id = ParseIntSafe(queueId, -1);

        if (id <= 0)
            throw new InvalidOperationException("Invalid queue id");

        return await _queueRepository.CancelQueueItem(id, NowIso());
    }

    public async Task<int> RetryFailed()
    {
        return await _queueRepository.RetryFailed(NowIso());
    }

    public async Task<int> ClearQueued()
    {
        return await _queueRepository.ClearQueued(NowIso());
    }

    async Task<List<string>> ListEnabledMappingDirectories()
    {
        return await _mappingRepository.ListEnabledMappingDirectories();
    }

    static List<string> GetDirectoryVariants(string? directory)
    {
        var dir = (directory ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(dir))
            return [];

        if (dir.Length > 1 &&
            (dir.EndsWith('/') || dir.EndsWith('\\')))
            dir = dir.TrimEnd('/', '\\');

        var variants = new List<string>
        {
            dir
        };

        var forward = dir.Replace('\\', '/');
        var backward = dir.Replace('/', '\\');

        if (!variants.Contains(forward, StringComparer.Ordinal))
            variants.Add(forward);

        if (!variants.Contains(backward, StringComparer.Ordinal))
            variants.Add(backward);

        return variants;
    }

    async Task<Dictionary<string, string>> FetchStatusMapForDirectory(string directory)
    {
        var variants = GetDirectoryVariants(directory);

        if (variants.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        Dictionary<string, string> fallback = new(StringComparer.Ordinal);

        foreach (var variant in variants)
        {
            try
            {
                var data = await _options.OpenCodeFetch(
                    "/session/status",
                    new OpenCodeRequest
                    {
                        Directory = variant
                    });

                var map = new Dictionary<string, string>(StringComparer.Ordinal);

                if (data is JsonObject obj)
                {
                    foreach (var kv in obj)
                    {
                        var statusType = kv.Value?["type"]?.ToString()?.Trim()?.ToLowerInvariant();

                        if (string.IsNullOrWhiteSpace(statusType))
                            continue;

                        map[kv.Key] = statusType;
                    }
                }

                if (map.Count > 0)
                    return map;

                if (fallback.Count == 0)
                    fallback = map;
            }
            catch
            {
            }
        }

        return fallback;
    }

    async Task<(bool Paused, int WorkingCount, int MaxWorkingGlobal, int WorkingResumeBelow, string? SampleAt)> EvaluateWorkloadBackpressure(bool forceRefresh)
    {
        if (_options.MaxWorkingGlobal <= 0)
        {
            _workloadPaused = false;

            return (false, 0, _options.MaxWorkingGlobal, _options.WorkingResumeBelow, _latestWorkingSampleAt?.ToString("O"));
        }

        var sample = await GetWorkingSessionsCount(forceRefresh);
        var count = sample.Count;
        var nextPaused = _workloadPaused;

        if (!nextPaused &&
            count >= _options.MaxWorkingGlobal)
            nextPaused = true;
        else if (nextPaused && count < _options.WorkingResumeBelow)
            nextPaused = false;

        if (nextPaused != _workloadPaused)
        {
            _workloadPaused = nextPaused;
            _options.OnChange();
        }

        return (_workloadPaused, count, _options.MaxWorkingGlobal, _options.WorkingResumeBelow, _latestWorkingSampleAt?.ToString("O"));
    }

    async Task<(DateTimeOffset Ts, int Count)> GetWorkingSessionsCount(bool forceRefresh)
    {
        var now = DateTimeOffset.UtcNow;
        var cacheTtlMs = Math.Clamp(_options.PollMs, 500, 5000);

        if (!forceRefresh &&
            (now - _cachedWorkingSample.Ts).TotalMilliseconds < cacheTtlMs)
            return _cachedWorkingSample;

        var dirs = await ListEnabledMappingDirectories();
        var totalRunning = 0;

        foreach (var dir in dirs)
        {
            var map = await FetchStatusMapForDirectory(dir);
            totalRunning += map.Values.Count(IsRunningStatusType);
        }

        _cachedWorkingSample = (now, totalRunning);
        _latestWorkingCount = totalRunning;
        _latestWorkingSampleAt = now;

        return _cachedWorkingSample;
    }

    string ComposePrompt(QueueItemRecord item)
    {
        var lines = new List<string>
        {
            "Resolve the following SonarQube warning with a minimal, targeted change.",
            string.Empty,
            $"Issue key: {item.IssueKey}"
        };

        if (!string.IsNullOrWhiteSpace(item.IssueType))
            lines.Add($"Issue type: {item.IssueType}");

        if (!string.IsNullOrWhiteSpace(item.Severity))
            lines.Add($"Severity: {item.Severity}");

        if (!string.IsNullOrWhiteSpace(item.Rule))
            lines.Add($"Rule: {item.Rule}");

        if (!string.IsNullOrWhiteSpace(item.IssueStatus))
            lines.Add($"Issue status: {item.IssueStatus}");

        if (!string.IsNullOrWhiteSpace(item.RelativePath))
            lines.Add($"File: {item.RelativePath}");

        if (item.Line.HasValue)
            lines.Add($"Line: {item.Line.Value}");

        if (!string.IsNullOrWhiteSpace(item.Message))
            lines.Add($"Message: {item.Message}");

        lines.Add(string.Empty);
        lines.Add("Constraints:");
        lines.Add("- Fix only this issue; avoid unrelated refactors.");
        lines.Add("- Preserve behavior and public contracts.");
        lines.Add("- If the issue is not actionable, explain why and propose the safest alternative.");

        var extra = (item.Instructions ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(extra))
        {
            lines.Add(string.Empty);
            lines.Add("Additional instructions:");
            lines.Add(extra);
        }

        return string.Join('\n', lines);
    }

    async Task<QueueItemRecord?> ClaimNextQueuedItem()
    {
        return await _queueRepository.ClaimNextQueuedItem(NowIso());
    }

    async Task DispatchQueueItem(QueueItemRecord item)
    {
        try
        {
            var title = $"[{item.IssueType ?? "ISSUE"}] {item.IssueKey}";

            var created = await _options.OpenCodeFetch(
                "/session",
                new OpenCodeRequest
                {
                    Method = "POST",
                    Directory = item.Directory,
                    JsonBody = new JsonObject
                    {
                        ["title"] = title
                    }
                });

            var sessionId = created?["id"]?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(sessionId))
                throw new InvalidOperationException("OpenCode did not return a session id");

            var prompt = ComposePrompt(item);

            await _options.OpenCodeFetch(
                $"/session/{Uri.EscapeDataString(sessionId)}/prompt_async",
                new OpenCodeRequest
                {
                    Method = "POST",
                    Directory = item.Directory,
                    JsonBody = new JsonObject
                    {
                        ["parts"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = prompt
                            }
                        }
                    }
                });

            var ts = NowIso();
            var openCodeUrl = _options.BuildOpenCodeSessionUrl(sessionId, item.Directory);
            await _queueRepository.MarkSessionCreated(item.Id, sessionId, openCodeUrl, ts);
        }
        catch (Exception ex)
        {
            var (attemptCount, maxAttempts) = await _queueRepository.GetAttemptInfo(item.Id, item.AttemptCount, item.MaxAttempts);
            var exhausted = attemptCount >= maxAttempts;
            var nextAttemptAt = exhausted ? null : DateTimeOffset.UtcNow.AddMilliseconds(MakeBackoffMs(attemptCount)).ToString("O");
            var state = exhausted ? "failed" : "queued";
            await _queueRepository.MarkDispatchFailure(item.Id, state, nextAttemptAt, ex.Message, NowIso());
        }
    }

    public async Task Tick()
    {
        if (_tickRunning || _disposed)
            return;

        _tickRunning = true;

        try
        {
            if (!IsConfigured())
                return;

            var workload = await EvaluateWorkloadBackpressure(true);

            if (workload.Paused)
                return;

            while (_inFlight.Count < _options.MaxActive)
            {
                var claim = await ClaimNextQueuedItem();

                if (claim is null)
                    break;

                var key = claim.Id.ToString(CultureInfo.InvariantCulture);
                _inFlight.Add(key);

                _ = DispatchQueueItem(claim)
                    .ContinueWith(_ =>
                    {
                        _inFlight.Remove(key);
                        _options.OnChange();
                    });
            }
        }
        finally
        {
            _tickRunning = false;
        }
    }

    public void Start(CancellationToken stoppingToken)
    {
        if (_timer is not null)
            return;

        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollMs));

        _loopTask = Task.Run(
            async () =>
            {
                await Tick();

                while (!stoppingToken.IsCancellationRequested &&
                       _timer is not null)
                {
                    try
                    {
                        if (!await _timer.WaitForNextTickAsync(stoppingToken))
                            break;

                        await Tick();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                    }
                }
            },
            stoppingToken);
    }
}
