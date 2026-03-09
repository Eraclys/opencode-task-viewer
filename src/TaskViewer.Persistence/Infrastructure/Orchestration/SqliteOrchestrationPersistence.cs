using Dapper;
using Microsoft.Data.Sqlite;

namespace TaskViewer.Infrastructure.Orchestration;

public sealed class SqliteOrchestrationPersistence : IOrchestrationPersistence
{
    readonly SemaphoreSlim _dbLock = new(1, 1);
    readonly string _dbPath;
    bool _disposed;

    public SqliteOrchestrationPersistence(string dbPath, Action onChange)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath) ?? ".");
        InitializeSchema();
        QueueRepository = new SqliteQueueRepository(_dbLock, OpenConnection, onChange);
        MappingRepository = new SqliteMappingRepository(_dbLock, OpenConnection, onChange);
    }

    public IQueueRepository QueueRepository { get; }

    public IMappingRepository MappingRepository { get; }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _dbLock.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task ResetStateAsync()
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync("DELETE FROM task_issue_links");
            await conn.ExecuteAsync("DELETE FROM task_review_history");
            await conn.ExecuteAsync("DELETE FROM queue_items");
            await conn.ExecuteAsync("DELETE FROM instruction_profiles");
            await conn.ExecuteAsync("DELETE FROM project_mappings");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
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
        task_key TEXT NOT NULL UNIQUE,
        task_unit TEXT NOT NULL,
        issue_key TEXT NOT NULL,
        issue_count INTEGER NOT NULL DEFAULT 1,
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
        lock_key TEXT,
        line INTEGER,
        issue_status TEXT,
        instructions_snapshot TEXT,
        state TEXT NOT NULL,
        priority_score INTEGER NOT NULL DEFAULT 0,
        attempt_count INTEGER NOT NULL DEFAULT 0,
        max_attempts INTEGER NOT NULL DEFAULT 3,
        lease_owner TEXT,
        lease_heartbeat_at TEXT,
        lease_expires_at TEXT,
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

      CREATE TABLE IF NOT EXISTS task_issue_links (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        task_id INTEGER NOT NULL,
        issue_key TEXT NOT NULL,
        issue_type TEXT NOT NULL,
        severity TEXT,
        rule TEXT,
        message TEXT,
        component TEXT,
        relative_path TEXT,
        absolute_path TEXT,
        line INTEGER,
        issue_status TEXT,
        created_at TEXT NOT NULL,
        UNIQUE(task_id, issue_key)
      );

      CREATE TABLE IF NOT EXISTS task_review_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        task_id INTEGER NOT NULL,
        action TEXT NOT NULL,
        reason TEXT,
        created_at TEXT NOT NULL
      );

      CREATE INDEX IF NOT EXISTS idx_task_issue_task ON task_issue_links(task_id);
      CREATE INDEX IF NOT EXISTS idx_task_review_history_task ON task_review_history(task_id, created_at DESC);
    ");

        EnsureColumnExists(conn, "queue_items", "task_key", "TEXT");
        EnsureColumnExists(conn, "queue_items", "task_unit", "TEXT NOT NULL DEFAULT 'project+file+rule'");
        EnsureColumnExists(conn, "queue_items", "issue_count", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(conn, "queue_items", "lock_key", "TEXT");
        EnsureColumnExists(conn, "queue_items", "instructions_snapshot", "TEXT");
        EnsureColumnExists(conn, "queue_items", "priority_score", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(conn, "queue_items", "lease_owner", "TEXT");
        EnsureColumnExists(conn, "queue_items", "lease_heartbeat_at", "TEXT");
        EnsureColumnExists(conn, "queue_items", "lease_expires_at", "TEXT");

        BackfillQueueTaskColumns(conn);

        conn.Execute(@"
CREATE INDEX IF NOT EXISTS idx_queue_state_next_attempt ON queue_items(state, next_attempt_at, created_at);
CREATE INDEX IF NOT EXISTS idx_queue_priority ON queue_items(state, priority_score DESC, created_at ASC);
CREATE INDEX IF NOT EXISTS idx_queue_project_state ON queue_items(sonar_project_key, branch, state);
CREATE INDEX IF NOT EXISTS idx_queue_lock_state ON queue_items(lock_key, state);
");
    }

    static void EnsureColumnExists(SqliteConnection conn, string tableName, string columnName, string columnType)
    {
        var columns = conn.Query<TableInfoRow>($"PRAGMA table_info({tableName})").ToList();

        if (columns.Any(column => string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)))
            return;

        conn.Execute($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}");
    }

    static void BackfillQueueTaskColumns(SqliteConnection conn)
    {
        conn.Execute(@"
UPDATE queue_items
SET task_key = COALESCE(NULLIF(task_key, ''), 'legacy-task-' || id)
WHERE task_key IS NULL OR task_key = '';

UPDATE queue_items
SET task_unit = COALESCE(NULLIF(task_unit, ''), 'project+file+rule')
WHERE task_unit IS NULL OR task_unit = '';

UPDATE queue_items
SET lock_key = COALESCE(NULLIF(lock_key, ''), sonar_project_key || '|' || COALESCE(branch, '') || '|' || COALESCE(relative_path, absolute_path, issue_key, id) || '|' || COALESCE(rule, ''))
WHERE lock_key IS NULL OR lock_key = '';

UPDATE queue_items
SET issue_count = COALESCE(issue_count, 1)
WHERE issue_count IS NULL;

UPDATE queue_items
SET priority_score = COALESCE(priority_score, 0)
WHERE priority_score IS NULL;

UPDATE queue_items
SET instructions_snapshot = COALESCE(instructions_snapshot, '')
WHERE instructions_snapshot IS NULL;
        ");
    }

    sealed class TableInfoRow
    {
        public string Name { get; init; } = string.Empty;
    }
}
