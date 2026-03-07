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
            using var cmd = conn.CreateCommand();

            var where = string.Empty;

            if (states.Count > 0)
            {
                var names = new List<string>();

                for (var i = 0; i < states.Count; i++)
                {
                    var p = $"$s{i}";
                    names.Add(p);
                    cmd.Parameters.AddWithValue(p, states[i]);
                }

                where = $"WHERE state IN ({string.Join(", ", names)})";
            }

            cmd.CommandText = $"SELECT * FROM queue_items {where} ORDER BY datetime(updated_at) DESC, id DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            var items = new List<QueueItemRecord>();

            while (reader.Read())
            {
                items.Add(MapQueue(reader));
            }

            return items;
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
                using var existing = conn.CreateCommand();
                existing.CommandText = "SELECT id, state FROM queue_items WHERE mapping_id = $mid AND issue_key = $issueKey AND state IN ('queued', 'dispatching') LIMIT 1";
                existing.Parameters.AddWithValue("$mid", mapping.Id);
                existing.Parameters.AddWithValue("$issueKey", issue.Key);
                using var existingReader = existing.ExecuteReader();

                if (existingReader.Read())
                {
                    var state = existingReader.GetString(existingReader.GetOrdinal("state"));
                    skipped.Add(new QueueSkip(issue.Key, $"already-{state}"));

                    continue;
                }

                using var insert = conn.CreateCommand();

                insert.CommandText = @"
          INSERT INTO queue_items (
            issue_key, mapping_id, sonar_project_key, directory, branch,
            issue_type, severity, rule, message,
            component, relative_path, absolute_path, line, issue_status,
            instructions_snapshot,
            state, attempt_count, max_attempts, next_attempt_at,
            created_at, updated_at
          ) VALUES (
            $issue_key, $mapping_id, $sonar_key, $directory, $branch,
            $issue_type, $severity, $rule, $message,
            $component, $relative_path, $absolute_path, $line, $issue_status,
            $instructions,
            'queued', 0, $max_attempts, $next_attempt_at,
            $created_at, $updated_at
          )";

                insert.Parameters.AddWithValue("$issue_key", issue.Key);
                insert.Parameters.AddWithValue("$mapping_id", mapping.Id);
                insert.Parameters.AddWithValue("$sonar_key", mapping.SonarProjectKey);
                insert.Parameters.AddWithValue("$directory", mapping.Directory);
                AddNullableText(insert.Parameters, "$branch", mapping.Branch);
                AddNullableText(insert.Parameters, "$issue_type", type ?? issue.Type);
                AddNullableText(insert.Parameters, "$severity", issue.Severity);
                AddNullableText(insert.Parameters, "$rule", issue.Rule);
                AddNullableText(insert.Parameters, "$message", issue.Message);
                AddNullableText(insert.Parameters, "$component", issue.Component);
                AddNullableText(insert.Parameters, "$relative_path", issue.RelativePath);
                AddNullableText(insert.Parameters, "$absolute_path", issue.AbsolutePath);
                AddNullableInt(insert.Parameters, "$line", issue.Line);
                AddNullableText(insert.Parameters, "$issue_status", issue.Status);
                AddNullableText(insert.Parameters, "$instructions", instructionText);
                insert.Parameters.AddWithValue("$max_attempts", maxAttempts);
                insert.Parameters.AddWithValue("$next_attempt_at", nowIso);
                insert.Parameters.AddWithValue("$created_at", nowIso);
                insert.Parameters.AddWithValue("$updated_at", nowIso);
                insert.ExecuteNonQuery();

                using var readInserted = conn.CreateCommand();
                readInserted.CommandText = "SELECT * FROM queue_items WHERE id = last_insert_rowid()";
                using var insertedReader = readInserted.ExecuteReader();

                if (insertedReader.Read())
                    createdItems.Add(MapQueue(insertedReader));
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
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT state, COUNT(*) AS count FROM queue_items GROUP BY state";
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var state = reader.GetString(reader.GetOrdinal("state"));

                if (!stats.ContainsKey(state))
                    continue;

                stats[state] = reader.GetInt32(reader.GetOrdinal("count"));
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
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
        UPDATE queue_items
        SET state = 'cancelled', cancelled_at = $now, updated_at = $now
        WHERE id = $id AND state IN ('queued', 'dispatching')";

            cmd.Parameters.AddWithValue("$now", nowIso);
            cmd.Parameters.AddWithValue("$id", id);
            var changed = cmd.ExecuteNonQuery();

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
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
        UPDATE queue_items
        SET state = 'queued', next_attempt_at = $now, updated_at = $now, last_error = NULL
        WHERE state = 'failed'";

            cmd.Parameters.AddWithValue("$now", nowIso);
            var changed = cmd.ExecuteNonQuery();

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
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
        UPDATE queue_items
        SET state = 'cancelled', cancelled_at = $now, updated_at = $now
        WHERE state = 'queued'";

            cmd.Parameters.AddWithValue("$now", nowIso);
            var changed = cmd.ExecuteNonQuery();

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
            using var select = conn.CreateCommand();

            select.CommandText = @"
        SELECT *
        FROM queue_items
        WHERE state = 'queued'
          AND (next_attempt_at IS NULL OR next_attempt_at <= $now)
        ORDER BY datetime(created_at) ASC, id ASC
        LIMIT 1";

            select.Parameters.AddWithValue("$now", nowIso);

            using var reader = select.ExecuteReader();

            if (!reader.Read())
                return null;

            var id = reader.GetInt32(reader.GetOrdinal("id"));
            using var claim = conn.CreateCommand();

            claim.CommandText = @"
        UPDATE queue_items
        SET state = 'dispatching',
            attempt_count = attempt_count + 1,
            updated_at = $now,
            dispatched_at = COALESCE(dispatched_at, $now),
            last_error = NULL
        WHERE id = $id AND state = 'queued'";

            claim.Parameters.AddWithValue("$now", nowIso);
            claim.Parameters.AddWithValue("$id", id);

            if (claim.ExecuteNonQuery() == 0)
                return null;

            using var readClaimed = conn.CreateCommand();
            readClaimed.CommandText = "SELECT * FROM queue_items WHERE id = $id";
            readClaimed.Parameters.AddWithValue("$id", id);
            using var claimedReader = readClaimed.ExecuteReader();

            return claimedReader.Read() ? MapQueue(claimedReader) : null;
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
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
          UPDATE queue_items
          SET state = 'session_created',
              session_id = $sid,
              open_code_url = $url,
              completed_at = $ts,
              updated_at = $ts,
              next_attempt_at = NULL,
              last_error = NULL
          WHERE id = $id AND state = 'dispatching'";

            cmd.Parameters.AddWithValue("$sid", sessionId);
            AddNullableText(cmd.Parameters, "$url", openCodeUrl);
            cmd.Parameters.AddWithValue("$ts", timestampIso);
            cmd.Parameters.AddWithValue("$id", id);

            var changed = cmd.ExecuteNonQuery();

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
            using var read = conn.CreateCommand();
            read.CommandText = "SELECT attempt_count, max_attempts FROM queue_items WHERE id = $id";
            read.Parameters.AddWithValue("$id", id);
            using var reader = read.ExecuteReader();

            if (!reader.Read())
                return (fallbackAttemptCount, fallbackMaxAttempts);

            var attemptCountOrd = reader.GetOrdinal("attempt_count");
            var maxAttemptsOrd = reader.GetOrdinal("max_attempts");

            var attemptCount = reader.IsDBNull(attemptCountOrd) ? fallbackAttemptCount : reader.GetInt32(attemptCountOrd);
            var maxAttempts = reader.IsDBNull(maxAttemptsOrd) ? fallbackMaxAttempts : reader.GetInt32(maxAttemptsOrd);

            return (attemptCount, maxAttempts);
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
            using var update = conn.CreateCommand();

            update.CommandText = @"
          UPDATE queue_items
          SET state = $state,
              next_attempt_at = $next,
              last_error = $error,
              updated_at = $updated
          WHERE id = $id AND state = 'dispatching'";

            update.Parameters.AddWithValue("$state", state);
            AddNullableDateTime(update.Parameters, "$next", nextAttemptAt);
            update.Parameters.AddWithValue("$error", lastError);
            update.Parameters.AddWithValue("$updated", updatedAtIso);
            update.Parameters.AddWithValue("$id", id);

            var changed = update.ExecuteNonQuery();

            if (changed > 0)
                _onChange();

            return changed > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    static void AddNullableText(SqliteParameterCollection parameters, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            parameters.AddWithValue(name, DBNull.Value);
        else
            parameters.AddWithValue(name, value);
    }

    static void AddNullableInt(SqliteParameterCollection parameters, string name, int? value)
    {
        if (value.HasValue)
            parameters.AddWithValue(name, value.Value);
        else
            parameters.AddWithValue(name, DBNull.Value);
    }

    static void AddNullableDateTime(SqliteParameterCollection parameters, string name, DateTimeOffset? value)
    {
        if (value.HasValue)
            parameters.AddWithValue(name, value.Value.ToString("O"));
        else
            parameters.AddWithValue(name, DBNull.Value);
    }

    static QueueItemRecord MapQueue(SqliteDataReader reader)
    {
        string? Str(string name)
        {
            var ord = reader.GetOrdinal(name);

            return reader.IsDBNull(ord) ? null : reader.GetString(ord);
        }

        int? IntNullable(string name)
        {
            var ord = reader.GetOrdinal(name);

            return reader.IsDBNull(ord) ? null : reader.GetInt32(ord);
        }

        DateTimeOffset ParseDateTime(string name)
        {
            var ord = reader.GetOrdinal(name);

            if (reader.IsDBNull(ord))
                return DateTimeOffset.MinValue;

            var raw = reader.GetString(ord);

            return DateTimeOffset.TryParse(raw, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;
        }

        DateTimeOffset? ParseDateTimeNullable(string name)
        {
            var ord = reader.GetOrdinal(name);

            if (reader.IsDBNull(ord))
                return null;

            var raw = reader.GetString(ord);

            return DateTimeOffset.TryParse(raw, out var parsed)
                ? parsed
                : null;
        }

        return new QueueItemRecord
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            IssueKey = reader.GetString(reader.GetOrdinal("issue_key")),
            MappingId = reader.GetInt32(reader.GetOrdinal("mapping_id")),
            SonarProjectKey = reader.GetString(reader.GetOrdinal("sonar_project_key")),
            Directory = reader.GetString(reader.GetOrdinal("directory")),
            Branch = Str("branch"),
            IssueType = Str("issue_type"),
            Severity = Str("severity"),
            Rule = Str("rule"),
            Message = Str("message"),
            Component = Str("component"),
            RelativePath = Str("relative_path"),
            AbsolutePath = Str("absolute_path"),
            Line = IntNullable("line"),
            IssueStatus = Str("issue_status"),
            Instructions = Str("instructions_snapshot"),
            State = reader.GetString(reader.GetOrdinal("state")),
            AttemptCount = reader.GetInt32(reader.GetOrdinal("attempt_count")),
            MaxAttempts = reader.GetInt32(reader.GetOrdinal("max_attempts")),
            NextAttemptAt = ParseDateTimeNullable("next_attempt_at"),
            SessionId = Str("session_id"),
            OpenCodeUrl = Str("open_code_url"),
            LastError = Str("last_error"),
            CreatedAt = ParseDateTime("created_at"),
            UpdatedAt = ParseDateTime("updated_at"),
            DispatchedAt = ParseDateTimeNullable("dispatched_at"),
            CompletedAt = ParseDateTimeNullable("completed_at"),
            CancelledAt = ParseDateTimeNullable("cancelled_at")
        };
    }
}
