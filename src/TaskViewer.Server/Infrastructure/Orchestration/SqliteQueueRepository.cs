using Dapper;
using Microsoft.Data.Sqlite;

namespace TaskViewer.Server.Infrastructure.Orchestration;

sealed class SqliteQueueRepository : IQueueRepository
{
    readonly SemaphoreSlim _dbLock;
    readonly Action _onChange;
    readonly Func<SqliteConnection> _openConnection;

    public SqliteQueueRepository(SemaphoreSlim dbLock, Func<SqliteConnection> openConnection, Action onChange)
    {
        _dbLock = dbLock;
        _openConnection = openConnection;
        _onChange = onChange;
    }

    public async Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<string> states, int limit)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var sql = states.Count > 0
                ? SelectTaskSql + @"
WHERE state IN @States
ORDER BY priority_score DESC, datetime(created_at) ASC, id ASC
LIMIT @Limit"
                : SelectTaskSql + @"
ORDER BY priority_score DESC, datetime(created_at) ASC, id ASC
LIMIT @Limit";

            var parameters = new DynamicParameters();
            parameters.Add("Limit", limit);

            if (states.Count > 0)
                parameters.Add("States", states.ToArray());

            var rows = await conn.QueryAsync<QueueItemRow>(sql, parameters);
            return rows.Select(MapTask).ToList();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues,
        int maxAttempts,
        DateTimeOffset now)
    {
        var createdItems = new List<QueueItemRecord>();
        var skipped = new List<QueueSkip>();
        var grouped = issues
            .GroupBy(issue => OrchestrationTaskBatchingPolicy.BuildTaskKey(mapping, issue.RelativePath ?? issue.AbsolutePath, issue.Rule), StringComparer.Ordinal)
            .ToList();

        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();

            foreach (var group in grouped)
            {
                var groupedIssues = group.ToList();
                var representative = groupedIssues[0];
                var taskKey = group.Key;
                var rule = representative.Rule;
                var path = representative.RelativePath ?? representative.AbsolutePath;
                var existing = await conn.QuerySingleOrDefaultAsync<QueuedStateRow>(@"
SELECT
    id AS Id,
    state AS State
FROM queue_items
WHERE task_key = @TaskKey
  AND state IN ('queued', 'leased', 'running', 'awaiting_review')
LIMIT 1", new { TaskKey = taskKey });

                if (existing is not null)
                {
                    foreach (var issue in groupedIssues)
                        skipped.Add(new QueueSkip(issue.Key, $"already-{existing.State}"));

                    continue;
                }

                var nowIso = now.ToString("O");
                var issueKey = groupedIssues.Count == 1
                    ? representative.Key
                    : $"group:{mapping.SonarProjectKey}:{OrchestrationTaskBatchingPolicy.NormalizePath(path)}:{OrchestrationTaskBatchingPolicy.NormalizeRule(rule)}";
                var taskId = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO queue_items (
    task_key, task_unit, issue_key, issue_count, mapping_id, sonar_project_key, directory, branch,
    issue_type, severity, rule, message, component, relative_path, absolute_path, lock_key,
    line, issue_status, instructions_snapshot, state, priority_score,
    attempt_count, max_attempts, next_attempt_at, created_at, updated_at
) VALUES (
    @TaskKey, @TaskUnit, @IssueKey, @IssueCount, @MappingId, @SonarProjectKey, @Directory, @Branch,
    @IssueType, @Severity, @Rule, @Message, @Component, @RelativePath, @AbsolutePath, @LockKey,
    @Line, @IssueStatus, @Instructions, 'queued', @PriorityScore,
    0, @MaxAttempts, @NextAttemptAt, @CreatedAt, @UpdatedAt
);
SELECT last_insert_rowid();", new
                {
                    TaskKey = taskKey,
                    TaskUnit = OrchestrationTaskBatchingPolicy.TaskUnit,
                    IssueKey = issueKey,
                    IssueCount = groupedIssues.Count,
                    MappingId = mapping.Id,
                    SonarProjectKey = mapping.SonarProjectKey,
                    Directory = mapping.Directory,
                    Branch = SqliteOrchestrationDataMapper.NullIfWhiteSpace(mapping.Branch),
                    IssueType = SqliteOrchestrationDataMapper.NullIfWhiteSpace(type ?? representative.Type),
                    Severity = representative.Severity,
                    Rule = rule,
                    Message = OrchestrationTaskBatchingPolicy.BuildRepresentativeMessage(groupedIssues, path, rule),
                    Component = representative.Component,
                    RelativePath = representative.RelativePath,
                    AbsolutePath = representative.AbsolutePath,
                    LockKey = OrchestrationTaskBatchingPolicy.BuildLockKey(mapping, path),
                    Line = representative.Line,
                    IssueStatus = SqliteOrchestrationDataMapper.NullIfWhiteSpace(representative.Status),
                    Instructions = SqliteOrchestrationDataMapper.NullIfWhiteSpace(instructionText),
                    PriorityScore = OrchestrationTaskBatchingPolicy.ComputePriorityScore(groupedIssues, mapping.Branch),
                    MaxAttempts = maxAttempts,
                    NextAttemptAt = nowIso,
                    CreatedAt = nowIso,
                    UpdatedAt = nowIso
                });

                foreach (var issue in groupedIssues)
                {
                    await conn.ExecuteAsync(@"
INSERT INTO task_issue_links (
    task_id, issue_key, issue_type, severity, rule, message, component,
    relative_path, absolute_path, line, issue_status, created_at
) VALUES (
    @TaskId, @IssueKey, @IssueType, @Severity, @Rule, @Message, @Component,
    @RelativePath, @AbsolutePath, @Line, @IssueStatus, @CreatedAt
)", new
                    {
                        TaskId = taskId,
                        IssueKey = issue.Key,
                        IssueType = issue.Type,
                        issue.Severity,
                        issue.Rule,
                        issue.Message,
                        issue.Component,
                        issue.RelativePath,
                        issue.AbsolutePath,
                        issue.Line,
                        IssueStatus = issue.Status,
                        CreatedAt = nowIso
                    });
                }

                var inserted = await conn.QuerySingleOrDefaultAsync<QueueItemRow>(SelectTaskByIdSql, new { Id = taskId });

                if (inserted is not null)
                    createdItems.Add(MapTask(inserted));
            }

            if (createdItems.Count > 0)
                _onChange();
        }
        finally
        {
            _dbLock.Release();
        }

        return (createdItems, skipped);
    }

    public async Task<QueueStats> GetQueueStats()
    {
        var stats = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["queued"] = 0,
            ["leased"] = 0,
            ["running"] = 0,
            ["awaiting_review"] = 0,
            ["approved"] = 0,
            ["done"] = 0,
            ["failed"] = 0,
            ["cancelled"] = 0
        };

        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var rows = await conn.QueryAsync<QueueStateCountRow>(@"
SELECT state AS State, COUNT(*) AS Count
FROM queue_items
GROUP BY state");

            foreach (var row in rows)
            {
                if (stats.ContainsKey(row.State))
                    stats[row.State] = row.Count;
            }
        }
        finally
        {
            _dbLock.Release();
        }

        return new QueueStats(
            stats["queued"],
            stats["leased"] + stats["running"],
            stats["awaiting_review"] + stats["approved"],
            stats["done"],
            stats["failed"],
            stats["cancelled"]);
    }

    public async Task<bool> CancelQueueItem(int id, DateTimeOffset now)
    {
        var nowIso = now.ToString("O");
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'cancelled', cancelled_at = @Now, updated_at = @Now,
    lease_owner = NULL, lease_heartbeat_at = NULL, lease_expires_at = NULL
WHERE id = @Id AND state IN ('queued', 'leased', 'running')", new { Now = nowIso, Id = id });

            if (changed > 0)
                _onChange();

            return changed > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> RetryFailed(DateTimeOffset now)
    {
        var nowIso = now.ToString("O");
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'queued', next_attempt_at = @Now, updated_at = @Now, last_error = NULL,
    lease_owner = NULL, lease_heartbeat_at = NULL, lease_expires_at = NULL,
    session_id = NULL, open_code_url = NULL
WHERE state = 'failed'", new { Now = nowIso });

            if (changed > 0)
                _onChange();

            return changed;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> ClearQueued(DateTimeOffset now)
    {
        var nowIso = now.ToString("O");
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'cancelled', cancelled_at = @Now, updated_at = @Now
WHERE state = 'queued'", new { Now = nowIso });

            if (changed > 0)
                _onChange();

            return changed;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<QueueItemRecord?> TryLeaseTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'leased', lease_owner = @LeaseOwner, lease_heartbeat_at = @HeartbeatAt,
    lease_expires_at = @ExpiresAt, updated_at = @HeartbeatAt,
    attempt_count = attempt_count + 1,
    dispatched_at = COALESCE(dispatched_at, @HeartbeatAt),
    last_error = NULL
WHERE id = @Id AND state = 'queued'", new
            {
                LeaseOwner = leaseOwner,
                HeartbeatAt = heartbeatAt.ToString("O"),
                ExpiresAt = expiresAt.ToString("O"),
                Id = id
            });

            if (changed == 0)
                return null;

            _onChange();
            var row = await conn.QuerySingleOrDefaultAsync<QueueItemRow>(SelectTaskByIdSql, new { Id = id });
            return row is null ? null : MapTask(row);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> HeartbeatTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET lease_heartbeat_at = @HeartbeatAt,
    lease_expires_at = @ExpiresAt,
    updated_at = @HeartbeatAt
WHERE id = @Id AND lease_owner = @LeaseOwner AND state IN ('leased', 'running')", new
            {
                HeartbeatAt = heartbeatAt.ToString("O"),
                ExpiresAt = expiresAt.ToString("O"),
                Id = id,
                LeaseOwner = leaseOwner
            });

            if (changed > 0)
                _onChange();

            return changed > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<NormalizedIssue>> GetTaskIssues(int id)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var rows = await conn.QueryAsync<TaskIssueRow>(@"
SELECT
    issue_key AS IssueKey,
    issue_type AS IssueType,
    severity AS Severity,
    rule AS Rule,
    message AS Message,
    component AS Component,
    relative_path AS RelativePath,
    absolute_path AS AbsolutePath,
    line AS Line,
    issue_status AS IssueStatus
FROM task_issue_links
WHERE task_id = @TaskId
ORDER BY issue_key ASC", new { TaskId = id });

            return rows.Select(
                    row => new NormalizedIssue
                    {
                        Key = row.IssueKey,
                        Type = row.IssueType,
                        Severity = row.Severity,
                        Rule = row.Rule,
                        Message = row.Message,
                        Component = row.Component,
                        RelativePath = row.RelativePath,
                        AbsolutePath = row.AbsolutePath,
                        Line = row.Line,
                        Status = row.IssueStatus
                    })
                .ToList();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> MarkTaskRunning(
        int id,
        string sessionId,
        string? openCodeUrl,
        string leaseOwner,
        DateTimeOffset timestamp,
        DateTimeOffset leaseExpiresAt)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'running',
    session_id = @SessionId,
    open_code_url = @OpenCodeUrl,
    lease_owner = @LeaseOwner,
    lease_heartbeat_at = @Timestamp,
    lease_expires_at = @LeaseExpiresAt,
    completed_at = NULL,
    updated_at = @Timestamp,
    dispatched_at = COALESCE(dispatched_at, @Timestamp),
    next_attempt_at = NULL,
    last_error = NULL
WHERE id = @Id AND state = 'leased'", new
            {
                Id = id,
                SessionId = sessionId,
                OpenCodeUrl = SqliteOrchestrationDataMapper.NullIfWhiteSpace(openCodeUrl),
                LeaseOwner = leaseOwner,
                Timestamp = timestamp.ToString("O"),
                LeaseExpiresAt = leaseExpiresAt.ToString("O")
            });

            if (changed > 0)
                _onChange();

            return changed > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> MarkTaskAwaitingReview(int id, DateTimeOffset timestamp)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'awaiting_review',
    completed_at = @Timestamp,
    updated_at = @Timestamp,
    lease_owner = NULL,
    lease_heartbeat_at = NULL,
    lease_expires_at = NULL
WHERE id = @Id AND state = 'running'", new { Id = id, Timestamp = timestamp.ToString("O") });

            if (changed > 0)
                _onChange();

            return changed > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var row = await conn.QuerySingleOrDefaultAsync<AttemptInfoRow>(@"
SELECT attempt_count AS AttemptCount, max_attempts AS MaxAttempts
FROM queue_items
WHERE id = @Id", new { Id = id });

            if (row is null)
                return (fallbackAttemptCount, fallbackMaxAttempts);

            return (row.AttemptCount ?? fallbackAttemptCount, row.MaxAttempts ?? fallbackMaxAttempts);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> MarkDispatchFailure(
        int id,
        string state,
        DateTimeOffset? nextAttemptAt,
        string lastError,
        DateTimeOffset updatedAt)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = @State,
    next_attempt_at = @NextAttemptAt,
    last_error = @LastError,
    updated_at = @UpdatedAt,
    lease_owner = NULL,
    lease_heartbeat_at = NULL,
    lease_expires_at = NULL
WHERE id = @Id AND state IN ('leased', 'running')", new
            {
                Id = id,
                State = state,
                NextAttemptAt = nextAttemptAt?.ToString("O"),
                LastError = lastError,
                UpdatedAt = updatedAt.ToString("O")
            });

            if (changed > 0)
                _onChange();

            return changed > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    static QueueItemRecord MapTask(QueueItemRow row)
    {
        return new QueueItemRecord
        {
            Id = row.Id,
            TaskKey = row.TaskKey,
            TaskUnit = row.TaskUnit,
            IssueKey = row.IssueKey,
            IssueCount = row.IssueCount,
            MappingId = row.MappingId,
            SonarProjectKey = row.SonarProjectKey,
            Directory = row.Directory,
            Branch = row.Branch,
            IssueType = row.IssueType,
            Severity = row.Severity,
            Rule = row.Rule,
            Message = row.Message,
            Component = row.Component,
            RelativePath = row.RelativePath,
            AbsolutePath = row.AbsolutePath,
            LockKey = row.LockKey,
            Line = row.Line,
            IssueStatus = row.IssueStatus,
            Instructions = row.Instructions,
            State = row.State,
            PriorityScore = row.PriorityScore,
            AttemptCount = row.AttemptCount,
            MaxAttempts = row.MaxAttempts,
            LeaseOwner = row.LeaseOwner,
            LeaseHeartbeatAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.LeaseHeartbeatAt),
            LeaseExpiresAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.LeaseExpiresAt),
            NextAttemptAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.NextAttemptAt),
            SessionId = row.SessionId,
            OpenCodeUrl = row.OpenCodeUrl,
            LastError = row.LastError,
            CreatedAt = SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.CreatedAt),
            UpdatedAt = SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.UpdatedAt),
            DispatchedAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.DispatchedAt),
            CompletedAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.CompletedAt),
            CancelledAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.CancelledAt)
        };
    }

    const string SelectTaskSql = @"
SELECT
    id AS Id,
    task_key AS TaskKey,
    task_unit AS TaskUnit,
    issue_key AS IssueKey,
    issue_count AS IssueCount,
    mapping_id AS MappingId,
    sonar_project_key AS SonarProjectKey,
    directory AS Directory,
    branch AS Branch,
    issue_type AS IssueType,
    severity AS Severity,
    rule AS Rule,
    message AS Message,
    component AS Component,
    relative_path AS RelativePath,
    absolute_path AS AbsolutePath,
    lock_key AS LockKey,
    line AS Line,
    issue_status AS IssueStatus,
    instructions_snapshot AS Instructions,
    state AS State,
    priority_score AS PriorityScore,
    attempt_count AS AttemptCount,
    max_attempts AS MaxAttempts,
    lease_owner AS LeaseOwner,
    lease_heartbeat_at AS LeaseHeartbeatAt,
    lease_expires_at AS LeaseExpiresAt,
    next_attempt_at AS NextAttemptAt,
    session_id AS SessionId,
    open_code_url AS OpenCodeUrl,
    last_error AS LastError,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt,
    dispatched_at AS DispatchedAt,
    completed_at AS CompletedAt,
    cancelled_at AS CancelledAt
FROM queue_items";

    const string SelectTaskByIdSql = SelectTaskSql + @"
WHERE id = @Id";

    sealed class QueueItemRow
    {
        public int Id { get; init; }
        public string? TaskKey { get; init; }
        public string? TaskUnit { get; init; }
        public string IssueKey { get; init; } = string.Empty;
        public int IssueCount { get; init; }
        public int MappingId { get; init; }
        public string SonarProjectKey { get; init; } = string.Empty;
        public string Directory { get; init; } = string.Empty;
        public string? Branch { get; init; }
        public string? IssueType { get; init; }
        public string? Severity { get; init; }
        public string? Rule { get; init; }
        public string? Message { get; init; }
        public string? Component { get; init; }
        public string? RelativePath { get; init; }
        public string? AbsolutePath { get; init; }
        public string? LockKey { get; init; }
        public int? Line { get; init; }
        public string? IssueStatus { get; init; }
        public string? Instructions { get; init; }
        public string State { get; init; } = string.Empty;
        public int PriorityScore { get; init; }
        public int AttemptCount { get; init; }
        public int MaxAttempts { get; init; }
        public string? LeaseOwner { get; init; }
        public string? LeaseHeartbeatAt { get; init; }
        public string? LeaseExpiresAt { get; init; }
        public string? NextAttemptAt { get; init; }
        public string? SessionId { get; init; }
        public string? OpenCodeUrl { get; init; }
        public string? LastError { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;
        public string? DispatchedAt { get; init; }
        public string? CompletedAt { get; init; }
        public string? CancelledAt { get; init; }
    }

    sealed class QueuedStateRow
    {
        public int Id { get; init; }
        public string State { get; init; } = string.Empty;
    }

    sealed class QueueStateCountRow
    {
        public string State { get; init; } = string.Empty;
        public int Count { get; init; }
    }

    sealed class AttemptInfoRow
    {
        public int? AttemptCount { get; init; }
        public int? MaxAttempts { get; init; }
    }

    sealed class TaskIssueRow
    {
        public string IssueKey { get; init; } = string.Empty;
        public string IssueType { get; init; } = string.Empty;
        public string? Severity { get; init; }
        public string? Rule { get; init; }
        public string? Message { get; init; }
        public string? Component { get; init; }
        public string? RelativePath { get; init; }
        public string? AbsolutePath { get; init; }
        public int? Line { get; init; }
        public string? IssueStatus { get; init; }
    }
}
