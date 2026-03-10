using Dapper;
using Microsoft.Data.Sqlite;
using TaskViewer.Domain.Orchestration;
using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Persistence;

public sealed class SqliteQueueRepository : IQueueRepository
{
    readonly Action _onChange;
    readonly Func<SqliteConnection> _openConnection;

    public SqliteQueueRepository(Func<SqliteConnection> openConnection, Action onChange)
    {
        _openConnection = openConnection;
        _onChange = onChange;
    }

    public async Task<List<QueueItemRecord>> ListQueue(IReadOnlyList<QueueState> states, int limit, CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();
        var sql = states.Count > 0
            ? SelectTaskSql + @"
WHERE state IN @States
ORDER BY queue_items.priority_score DESC, datetime(queue_items.created_at) ASC, queue_items.id ASC
LIMIT @Limit"
            : SelectTaskSql + @"
ORDER BY queue_items.priority_score DESC, datetime(queue_items.created_at) ASC, queue_items.id ASC
LIMIT @Limit";

        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit);

        if (states.Count > 0)
            parameters.Add("States", states.Select(state => state.Value).ToArray());

        var rows = await conn.QueryAsync<QueueItemRow>(Cmd(sql, parameters, cancellationToken: cancellationToken));
        return rows.Select(MapTask).ToList();
    }

    public async Task<(List<QueueItemRecord> CreatedItems, List<QueueSkip> Skipped)> EnqueueIssuesBatch(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var createdItems = new List<QueueItemRecord>();
        var skipped = new List<QueueSkip>();
        var grouped = issues
            .GroupBy(issue => OrchestrationTaskBatchingPolicy.BuildTaskKey(mapping, issue.RelativePath ?? issue.AbsolutePath, issue.Rule), StringComparer.Ordinal)
            .ToList();

        using var conn = _openConnection();
        using var tx = conn.BeginTransaction();

        foreach (var group in grouped)
        {
                var groupedIssues = group.ToList();
                var representative = groupedIssues[0];
                var taskKey = group.Key;
                var rule = representative.Rule;
                var path = representative.RelativePath ?? representative.AbsolutePath;
                var existing = await conn.QuerySingleOrDefaultAsync<QueuedStateRow>(Cmd(@"
SELECT
    id AS Id,
    state AS State
FROM queue_items
WHERE task_key = @TaskKey
  AND state IN ('queued', 'leased', 'running', 'awaiting_review')
 LIMIT 1", new { TaskKey = taskKey }, tx, cancellationToken));

                if (existing is not null)
                {
                    foreach (var issue in groupedIssues)
                        skipped.Add(new QueueSkip(issue.Key, $"already-{QueueState.Parse(existing.State).Value}"));

                    continue;
                }

                var nowIso = now.ToString("O");
                var issueKey = groupedIssues.Count == 1
                    ? representative.Key
                    : $"group:{mapping.SonarProjectKey}:{OrchestrationTaskBatchingPolicy.NormalizePath(path)}:{OrchestrationTaskBatchingPolicy.NormalizeRule(rule)}";
                var taskId = await conn.ExecuteScalarAsync<long>(Cmd(@"
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
                }, tx, cancellationToken));

                foreach (var issue in groupedIssues)
                {
                    await conn.ExecuteAsync(Cmd(@"
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
                    }, tx, cancellationToken));
                }

                var inserted = await conn.QuerySingleOrDefaultAsync<QueueItemRow>(Cmd(SelectTaskByIdSql, new { Id = taskId }, tx, cancellationToken));

                if (inserted is not null)
                    createdItems.Add(MapTask(inserted));
            }

        tx.Commit();

        if (createdItems.Count > 0)
            _onChange();

        return (createdItems, skipped);
    }

    public async Task<QueueStats> GetQueueStats(CancellationToken cancellationToken = default)
    {
        var stats = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["queued"] = 0,
            ["leased"] = 0,
            ["running"] = 0,
            ["awaiting_review"] = 0,
            ["rejected"] = 0,
            ["done"] = 0,
            ["failed"] = 0,
            ["cancelled"] = 0
        };

        using var conn = _openConnection();
        var rows = await conn.QueryAsync<QueueStateCountRow>(Cmd(@"
SELECT state AS State, COUNT(*) AS Count
FROM queue_items
GROUP BY state", cancellationToken: cancellationToken));

        foreach (var row in rows)
        {
            if (stats.ContainsKey(row.State))
                stats[row.State] = row.Count;
        }

        return new QueueStats(
            stats["queued"],
            stats["leased"] + stats["running"],
            stats["awaiting_review"],
            stats["done"],
            stats["failed"],
            stats["cancelled"],
            stats["leased"],
            stats["running"],
            stats["awaiting_review"],
            stats["rejected"]);
    }

    public async Task<bool> CancelQueueItem(int id, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var nowIso = now.ToString("O");
        using var conn = _openConnection();
        var changed = await conn.ExecuteAsync(Cmd(@"
UPDATE queue_items
SET state = 'cancelled', cancelled_at = @Now, updated_at = @Now,
    lease_owner = NULL, lease_heartbeat_at = NULL, lease_expires_at = NULL
WHERE id = @Id AND state IN ('queued', 'leased', 'running')", new { Now = nowIso, Id = id }, cancellationToken: cancellationToken));

        if (changed > 0)
            _onChange();

        return changed > 0;
    }

    public async Task<int> RetryFailed(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var nowIso = now.ToString("O");
        using var conn = _openConnection();
        var changed = await conn.ExecuteAsync(Cmd(@"
UPDATE queue_items
SET state = 'queued', next_attempt_at = @Now, updated_at = @Now, last_error = NULL,
    lease_owner = NULL, lease_heartbeat_at = NULL, lease_expires_at = NULL,
    session_id = NULL, open_code_url = NULL
WHERE state = 'failed'", new { Now = nowIso }, cancellationToken: cancellationToken));

        if (changed > 0)
            _onChange();

        return changed;
    }

    public async Task<int> ClearQueued(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var nowIso = now.ToString("O");
        using var conn = _openConnection();
        var changed = await conn.ExecuteAsync(Cmd(@"
UPDATE queue_items
SET state = 'cancelled', cancelled_at = @Now, updated_at = @Now
WHERE state = 'queued'", new { Now = nowIso }, cancellationToken: cancellationToken));

        if (changed > 0)
            _onChange();

        return changed;
    }

    public async Task<QueueItemRecord?> TryLeaseTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
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

    public async Task<bool> HeartbeatTask(int id, string leaseOwner, DateTimeOffset heartbeatAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
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

    public async Task<List<NormalizedIssue>> GetTaskIssues(int id, CancellationToken cancellationToken = default)
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

    public async Task<IReadOnlyList<TaskReviewHistoryRecord>> GetTaskReviewHistory(int id, CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();
        var rows = await conn.QueryAsync<TaskReviewHistoryRow>(@"
SELECT
    action AS Action,
    reason AS Reason,
    created_at AS CreatedAt
FROM task_review_history
WHERE task_id = @TaskId
ORDER BY datetime(created_at) DESC, id DESC", new { TaskId = id });

        return rows.Select(MapReviewHistory).ToList();
    }

    public async Task<bool> MarkTaskRunning(
        int id,
        string sessionId,
        string? openCodeUrl,
        string leaseOwner,
        DateTimeOffset timestamp,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
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

    public async Task<bool> MarkTaskAwaitingReview(int id, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
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

    public async Task<bool> ApproveTask(int id, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        return await UpdateTerminalReviewState(id, QueueState.Done, TaskReviewAction.Approved, null, timestamp, [QueueState.AwaitingReview]);
    }

    public async Task<bool> RejectTask(int id, string? reason, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        return await UpdateTerminalReviewState(id, QueueState.Rejected, TaskReviewAction.Rejected, reason, timestamp, [QueueState.AwaitingReview]);
    }

    public async Task<bool> RequeueTask(int id, string? reason, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();
        using var tx = conn.BeginTransaction();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'queued',
    next_attempt_at = @Timestamp,
    updated_at = @Timestamp,
    last_error = @Reason,
    completed_at = NULL,
    cancelled_at = NULL,
    lease_owner = NULL,
    lease_heartbeat_at = NULL,
    lease_expires_at = NULL,
    session_id = NULL,
    open_code_url = NULL
WHERE id = @Id AND state IN ('awaiting_review', 'rejected')", new
            {
                Id = id,
                Timestamp = timestamp.ToString("O"),
                Reason = SqliteOrchestrationDataMapper.NullIfWhiteSpace(reason)
            }, tx);

        if (changed > 0)
        {
            await AppendReviewHistoryAsync(conn, tx, id, TaskReviewAction.Requeue, reason, timestamp);
            tx.Commit();
            _onChange();
        }
        else
            tx.Rollback();

        return changed > 0;
    }

    public async Task<bool> RepromptTask(int id, string instructions, string? reason, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();
        using var tx = conn.BeginTransaction();
            var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = 'queued',
    instructions_snapshot = @Instructions,
    next_attempt_at = @Timestamp,
    updated_at = @Timestamp,
    last_error = @Reason,
    completed_at = NULL,
    cancelled_at = NULL,
    session_id = NULL,
    open_code_url = NULL,
    lease_owner = NULL,
    lease_heartbeat_at = NULL,
    lease_expires_at = NULL,
    attempt_count = 0
WHERE id = @Id AND state IN ('awaiting_review', 'rejected')", new
            {
                Id = id,
                Instructions = instructions,
                Timestamp = timestamp.ToString("O"),
                Reason = SqliteOrchestrationDataMapper.NullIfWhiteSpace(reason)
            }, tx);

        if (changed > 0)
        {
            await AppendReviewHistoryAsync(conn, tx, id, TaskReviewAction.Reprompt, reason, timestamp);
            tx.Commit();
            _onChange();
        }
        else
            tx.Rollback();

        return changed > 0;
    }

    public async Task<(int AttemptCount, int MaxAttempts)> GetAttemptInfo(int id, int fallbackAttemptCount, int fallbackMaxAttempts, CancellationToken cancellationToken = default)
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

    public async Task<bool> MarkDispatchFailure(
        int id,
        QueueState state,
        DateTimeOffset? nextAttemptAt,
        string lastError,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();
        using var tx = conn.BeginTransaction();
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
                State = state.Value,
                NextAttemptAt = nextAttemptAt?.ToString("O"),
                LastError = lastError,
                UpdatedAt = updatedAt.ToString("O")
            }, tx);

        if (changed > 0)
        {
            tx.Commit();
            _onChange();
        }
        else
            tx.Rollback();

        return changed > 0;
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
            LastReviewAction = row.LastReviewAction,
            LastReviewReason = row.LastReviewReason,
            LastReviewedAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.LastReviewedAt),
            CreatedAt = SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.CreatedAt),
            UpdatedAt = SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.UpdatedAt),
            DispatchedAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.DispatchedAt),
            CompletedAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.CompletedAt),
            CancelledAt = SqliteOrchestrationDataMapper.ParseOptionalDateTime(row.CancelledAt)
        };
    }

    const string SelectTaskSql = @"
SELECT
    queue_items.id AS Id,
    queue_items.task_key AS TaskKey,
    queue_items.task_unit AS TaskUnit,
    queue_items.issue_key AS IssueKey,
    queue_items.issue_count AS IssueCount,
    queue_items.mapping_id AS MappingId,
    queue_items.sonar_project_key AS SonarProjectKey,
    queue_items.directory AS Directory,
    queue_items.branch AS Branch,
    queue_items.issue_type AS IssueType,
    queue_items.severity AS Severity,
    queue_items.rule AS Rule,
    queue_items.message AS Message,
    queue_items.component AS Component,
    queue_items.relative_path AS RelativePath,
    queue_items.absolute_path AS AbsolutePath,
    queue_items.lock_key AS LockKey,
    queue_items.line AS Line,
    queue_items.issue_status AS IssueStatus,
    queue_items.instructions_snapshot AS Instructions,
    queue_items.state AS State,
    queue_items.priority_score AS PriorityScore,
    queue_items.attempt_count AS AttemptCount,
    queue_items.max_attempts AS MaxAttempts,
    queue_items.lease_owner AS LeaseOwner,
    queue_items.lease_heartbeat_at AS LeaseHeartbeatAt,
    queue_items.lease_expires_at AS LeaseExpiresAt,
    queue_items.next_attempt_at AS NextAttemptAt,
    queue_items.session_id AS SessionId,
    queue_items.open_code_url AS OpenCodeUrl,
    queue_items.last_error AS LastError,
    queue_items.created_at AS CreatedAt,
    queue_items.updated_at AS UpdatedAt,
    queue_items.dispatched_at AS DispatchedAt,
    queue_items.completed_at AS CompletedAt,
    queue_items.cancelled_at AS CancelledAt,
    review.action AS LastReviewAction,
    review.reason AS LastReviewReason,
    review.created_at AS LastReviewedAt
FROM queue_items
LEFT JOIN (
    SELECT history.task_id, history.action, history.reason, history.created_at, history.id
    FROM task_review_history history
    INNER JOIN (
        SELECT task_id, MAX(id) AS max_id
        FROM task_review_history
        GROUP BY task_id
    ) latest ON latest.task_id = history.task_id AND latest.max_id = history.id
) review ON review.task_id = queue_items.id";

    const string SelectTaskByIdSql = SelectTaskSql + @"
WHERE queue_items.id = @Id";

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
        public string? LastReviewAction { get; init; }
        public string? LastReviewReason { get; init; }
        public string? LastReviewedAt { get; init; }
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

    sealed class TaskReviewHistoryRow
    {
        public string Action { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
    }

    async Task<bool> UpdateTerminalReviewState(int id, QueueState state, TaskReviewAction action, string? reason, DateTimeOffset timestamp, IReadOnlyList<QueueState> allowedStates)
    {
        using var conn = _openConnection();
        using var tx = conn.BeginTransaction();
        var changed = await conn.ExecuteAsync(@"
UPDATE queue_items
SET state = @State,
    updated_at = @Timestamp,
    completed_at = COALESCE(completed_at, @Timestamp),
    last_error = CASE WHEN @Reason IS NULL OR @Reason = '' THEN last_error ELSE @Reason END,
    lease_owner = NULL,
    lease_heartbeat_at = NULL,
    lease_expires_at = NULL
WHERE id = @Id AND state IN @AllowedStates", new
            {
                Id = id,
                State = state.Value,
                Timestamp = timestamp.ToString("O"),
                Reason = SqliteOrchestrationDataMapper.NullIfWhiteSpace(reason),
                AllowedStates = allowedStates.Select(queueState => queueState.Value).ToArray()
            }, tx);

        if (changed > 0)
        {
            await AppendReviewHistoryAsync(conn, tx, id, action, reason, timestamp);
            tx.Commit();
            _onChange();
        }
        else
            tx.Rollback();

        return changed > 0;
    }

    async Task AppendReviewHistoryAsync(SqliteConnection conn, SqliteTransaction tx, int taskId, TaskReviewAction action, string? reason, DateTimeOffset timestamp)
    {
        await conn.ExecuteAsync(@"
INSERT INTO task_review_history (task_id, action, reason, created_at)
VALUES (@TaskId, @Action, @Reason, @CreatedAt)", new
        {
            TaskId = taskId,
            Action = action.Value,
            Reason = SqliteOrchestrationDataMapper.NullIfWhiteSpace(reason),
            CreatedAt = timestamp.ToString("O")
        }, tx);
    }

    static TaskReviewHistoryRecord MapReviewHistory(TaskReviewHistoryRow row)
    {
        return new TaskReviewHistoryRecord(
            row.Action,
            row.Reason,
            SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.CreatedAt));
    }

    static CommandDefinition Cmd(string sql, object? param = null, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
        => new(sql, param, transaction: transaction, cancellationToken: cancellationToken);
}
