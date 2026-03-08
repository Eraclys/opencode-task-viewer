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
            var selectClause = @"
SELECT
    id AS Id,
    issue_key AS IssueKey,
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
    line AS Line,
    issue_status AS IssueStatus,
    instructions_snapshot AS Instructions,
    state AS State,
    attempt_count AS AttemptCount,
    max_attempts AS MaxAttempts,
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

            var sql = states.Count > 0
                ? selectClause + @"
WHERE state IN @States
ORDER BY datetime(updated_at) DESC, id DESC
LIMIT @Limit"
                : selectClause + @"
ORDER BY datetime(updated_at) DESC, id DESC
LIMIT @Limit";

            var parameters = new DynamicParameters();
            parameters.Add("Limit", limit);

            if (states.Count > 0)
                parameters.Add("States", states.ToArray());

            var rows = await conn.QueryAsync<QueueItemRow>(sql, parameters);
            return rows.Select(MapQueue).ToList();
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
        var nowIso = now.ToString("O");

        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();

            foreach (var issue in issues)
            {
                var existing = await conn.QuerySingleOrDefaultAsync<QueuedStateRow>(@"
SELECT
    id AS Id,
    state AS State
FROM queue_items
WHERE mapping_id = @MappingId
  AND issue_key = @IssueKey
  AND state IN ('queued', 'dispatching')
LIMIT 1", new { MappingId = mapping.Id, IssueKey = issue.Key });

                if (existing is not null)
                {
                    skipped.Add(new QueueSkip(issue.Key, $"already-{existing.State}"));
                    continue;
                }

                var insertedId = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO queue_items (
    issue_key, mapping_id, sonar_project_key, directory, branch,
    issue_type, severity, rule, message,
    component, relative_path, absolute_path, line, issue_status,
    instructions_snapshot,
    state, attempt_count, max_attempts, next_attempt_at,
    created_at, updated_at
) VALUES (
    @IssueKey, @MappingId, @SonarProjectKey, @Directory, @Branch,
    @IssueType, @Severity, @Rule, @Message,
    @Component, @RelativePath, @AbsolutePath, @Line, @IssueStatus,
    @Instructions,
    'queued', 0, @MaxAttempts, @NextAttemptAt,
    @CreatedAt, @UpdatedAt
);
SELECT last_insert_rowid();", new
                {
                    IssueKey = issue.Key,
                    MappingId = mapping.Id,
                    SonarProjectKey = mapping.SonarProjectKey,
                    Directory = mapping.Directory,
                    Branch = SqliteOrchestrationDataMapper.NullIfWhiteSpace(mapping.Branch),
                    IssueType = SqliteOrchestrationDataMapper.NullIfWhiteSpace(type ?? issue.Type),
                    issue.Severity,
                    issue.Rule,
                    issue.Message,
                    issue.Component,
                    issue.RelativePath,
                    issue.AbsolutePath,
                    issue.Line,
                    IssueStatus = SqliteOrchestrationDataMapper.NullIfWhiteSpace(issue.Status),
                    Instructions = SqliteOrchestrationDataMapper.NullIfWhiteSpace(instructionText),
                    MaxAttempts = maxAttempts,
                    NextAttemptAt = nowIso,
                    CreatedAt = nowIso,
                    UpdatedAt = nowIso
                });

                var inserted = await conn.QuerySingleOrDefaultAsync<QueueItemRow>(SelectQueueItemByIdSql, new { Id = insertedId });

                if (inserted is not null)
                    createdItems.Add(MapQueue(inserted));
            }
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
            ["dispatching"] = 0,
            ["session_created"] = 0,
            ["done"] = 0,
            ["failed"] = 0,
            ["cancelled"] = 0
        };

        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var rows = await conn.QueryAsync<QueueStateCountRow>(@"
SELECT
    state AS State,
    COUNT(*) AS Count
FROM queue_items
GROUP BY state");

            foreach (var row in rows)
            {
                if (!stats.ContainsKey(row.State))
                    continue;

                stats[row.State] = row.Count;
            }
        }
        finally
        {
            _dbLock.Release();
        }

        return new QueueStats(
            stats["queued"],
            stats["dispatching"],
            stats["session_created"],
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
SET state = 'cancelled', cancelled_at = @Now, updated_at = @Now
WHERE id = @Id AND state IN ('queued', 'dispatching')", new { Now = nowIso, Id = id });

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
SET state = 'queued', next_attempt_at = @Now, updated_at = @Now, last_error = NULL
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

    public async Task<QueueItemRecord?> ClaimNextQueuedItem(DateTimeOffset now)
    {
        var nowIso = now.ToString("O");
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            using var transaction = conn.BeginTransaction();

            var next = await conn.QuerySingleOrDefaultAsync<QueuedIdRow>(@"
SELECT
    id AS Id
FROM queue_items
WHERE state = 'queued'
  AND (next_attempt_at IS NULL OR next_attempt_at <= @Now)
ORDER BY datetime(created_at) ASC, id ASC
LIMIT 1", new { Now = nowIso }, transaction);

            if (next is null)
            {
                transaction.Commit();
                return null;
            }

            var claimed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'dispatching',
    attempt_count = attempt_count + 1,
    updated_at = @Now,
    dispatched_at = COALESCE(dispatched_at, @Now),
    last_error = NULL
WHERE id = @Id AND state = 'queued'", new { Now = nowIso, Id = next.Id }, transaction);

            if (claimed == 0)
            {
                transaction.Commit();
                return null;
            }

            var row = await conn.QuerySingleOrDefaultAsync<QueueItemRow>(SelectQueueItemByIdSql, new { Id = next.Id }, transaction);
            transaction.Commit();

            return row is null ? null : MapQueue(row);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> MarkSessionCreated(
        int id,
        string sessionId,
        string? openCodeUrl,
        DateTimeOffset timestamp)
    {
        var timestampIso = timestamp.ToString("O");
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'session_created',
    session_id = @SessionId,
    open_code_url = @OpenCodeUrl,
    completed_at = @Timestamp,
    updated_at = @Timestamp,
    next_attempt_at = NULL,
    last_error = NULL
WHERE id = @Id AND state = 'dispatching'", new
            {
                Id = id,
                SessionId = sessionId,
                OpenCodeUrl = SqliteOrchestrationDataMapper.NullIfWhiteSpace(openCodeUrl),
                Timestamp = timestampIso
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

    public async Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var row = await conn.QuerySingleOrDefaultAsync<AttemptInfoRow>(@"
SELECT
    attempt_count AS AttemptCount,
    max_attempts AS MaxAttempts
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
        var updatedAtIso = updatedAt.ToString("O");
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = @State,
    next_attempt_at = @NextAttemptAt,
    last_error = @LastError,
    updated_at = @UpdatedAt
WHERE id = @Id AND state = 'dispatching'", new
            {
                Id = id,
                State = state,
                NextAttemptAt = nextAttemptAt?.ToString("O"),
                LastError = lastError,
                UpdatedAt = updatedAtIso
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

    static QueueItemRecord MapQueue(QueueItemRow row)
    {
        return new QueueItemRecord
        {
            Id = row.Id,
            IssueKey = row.IssueKey,
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
            Line = row.Line,
            IssueStatus = row.IssueStatus,
            Instructions = row.Instructions,
            State = row.State,
            AttemptCount = row.AttemptCount,
            MaxAttempts = row.MaxAttempts,
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

    const string SelectQueueItemByIdSql = @"
SELECT
    id AS Id,
    issue_key AS IssueKey,
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
    line AS Line,
    issue_status AS IssueStatus,
    instructions_snapshot AS Instructions,
    state AS State,
    attempt_count AS AttemptCount,
    max_attempts AS MaxAttempts,
    next_attempt_at AS NextAttemptAt,
    session_id AS SessionId,
    open_code_url AS OpenCodeUrl,
    last_error AS LastError,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt,
    dispatched_at AS DispatchedAt,
    completed_at AS CompletedAt,
    cancelled_at AS CancelledAt
FROM queue_items
WHERE id = @Id";

    sealed class QueueItemRow
    {
        public int Id { get; init; }
        public string IssueKey { get; init; } = string.Empty;
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
        public int? Line { get; init; }
        public string? IssueStatus { get; init; }
        public string? Instructions { get; init; }
        public string State { get; init; } = string.Empty;
        public int AttemptCount { get; init; }
        public int MaxAttempts { get; init; }
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

    sealed class QueuedIdRow
    {
        public int Id { get; init; }
    }

    sealed class AttemptInfoRow
    {
        public int? AttemptCount { get; init; }
        public int? MaxAttempts { get; init; }
    }
}
