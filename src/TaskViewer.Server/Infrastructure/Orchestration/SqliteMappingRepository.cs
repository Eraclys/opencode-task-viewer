using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TaskViewer.Server;

namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed class SqliteMappingRepository : IMappingRepository
{
    private readonly SemaphoreSlim _dbLock;
    private readonly Func<SqliteConnection> _openConnection;
    private readonly Action _onChange;

    public SqliteMappingRepository(SemaphoreSlim dbLock, Func<SqliteConnection> openConnection, Action onChange)
    {
        _dbLock = dbLock;
        _openConnection = openConnection;
        _onChange = onChange;
    }

    public async Task<List<MappingRecord>> ListMappings()
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at FROM project_mappings ORDER BY sonar_project_key COLLATE NOCASE ASC";
            using var reader = cmd.ExecuteReader();
            var list = new List<MappingRecord>();

            while (reader.Read())
            {
                var row = MapMapping(reader);
                if (row is not null)
                    list.Add(row);
            }

            return list;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<MappingRecord?> GetMappingById(int id)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at FROM project_mappings WHERE id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();

            return reader.Read() ? MapMapping(reader) : null;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<MappingRecord> UpsertMapping(int? id, string sonarProjectKey, string directory, string? branch, bool enabled, string now)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();

            if (id.HasValue && id.Value > 0)
            {
                using var update = conn.CreateCommand();
                update.CommandText = @"
          UPDATE project_mappings
          SET sonar_project_key = $key,
              directory = $dir,
              branch = $branch,
              enabled = $enabled,
              updated_at = $updated
          WHERE id = $id";

                update.Parameters.AddWithValue("$key", sonarProjectKey);
                update.Parameters.AddWithValue("$dir", directory);
                update.Parameters.AddWithValue("$branch", (object?)branch ?? DBNull.Value);
                update.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
                update.Parameters.AddWithValue("$updated", now);
                update.Parameters.AddWithValue("$id", id.Value);

                if (update.ExecuteNonQuery() == 0)
                    throw new InvalidOperationException("Mapping not found");

                using var select = conn.CreateCommand();
                select.CommandText = "SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at FROM project_mappings WHERE id = $id LIMIT 1";
                select.Parameters.AddWithValue("$id", id.Value);
                using var reader = select.ExecuteReader();
                if (!reader.Read())
                    throw new InvalidOperationException("Mapping not found");

                _onChange();
                return MapMapping(reader)!;
            }

            using var upsert = conn.CreateCommand();
            upsert.CommandText = @"
        INSERT INTO project_mappings (sonar_project_key, directory, branch, enabled, created_at, updated_at)
        VALUES ($key, $dir, $branch, $enabled, $created, $updated)
        ON CONFLICT(sonar_project_key) DO UPDATE SET
          directory = excluded.directory,
          branch = excluded.branch,
          enabled = excluded.enabled,
          updated_at = excluded.updated_at";

            upsert.Parameters.AddWithValue("$key", sonarProjectKey);
            upsert.Parameters.AddWithValue("$dir", directory);
            upsert.Parameters.AddWithValue("$branch", (object?)branch ?? DBNull.Value);
            upsert.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            upsert.Parameters.AddWithValue("$created", now);
            upsert.Parameters.AddWithValue("$updated", now);
            upsert.ExecuteNonQuery();

            using var select2 = conn.CreateCommand();
            select2.CommandText = "SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at FROM project_mappings WHERE sonar_project_key = $key LIMIT 1";
            select2.Parameters.AddWithValue("$key", sonarProjectKey);
            using var reader2 = select2.ExecuteReader();
            if (!reader2.Read())
                throw new InvalidOperationException("Failed to save mapping");

            _onChange();
            return MapMapping(reader2)!;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<JsonObject?> GetInstructionProfile(int mappingId, string issueType)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, mapping_id, issue_type, instructions, created_at, updated_at FROM instruction_profiles WHERE mapping_id = $mid AND issue_type = $type LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mappingId);
            cmd.Parameters.AddWithValue("$type", issueType);
            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return null;

            return new JsonObject
            {
                ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
                ["mapping_id"] = reader.GetInt32(reader.GetOrdinal("mapping_id")),
                ["issue_type"] = reader.GetString(reader.GetOrdinal("issue_type")),
                ["instructions"] = reader.GetString(reader.GetOrdinal("instructions")),
                ["created_at"] = reader.GetString(reader.GetOrdinal("created_at")),
                ["updated_at"] = reader.GetString(reader.GetOrdinal("updated_at"))
            };
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<JsonObject> UpsertInstructionProfile(int mappingId, string issueType, string instructions, string now)
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            using var upsert = conn.CreateCommand();
            upsert.CommandText = @"
        INSERT INTO instruction_profiles (mapping_id, issue_type, instructions, created_at, updated_at)
        VALUES ($mid, $type, $instructions, $created, $updated)
        ON CONFLICT(mapping_id, issue_type) DO UPDATE SET
          instructions = excluded.instructions,
          updated_at = excluded.updated_at";

            upsert.Parameters.AddWithValue("$mid", mappingId);
            upsert.Parameters.AddWithValue("$type", issueType);
            upsert.Parameters.AddWithValue("$instructions", instructions);
            upsert.Parameters.AddWithValue("$created", now);
            upsert.Parameters.AddWithValue("$updated", now);
            upsert.ExecuteNonQuery();

            using var select = conn.CreateCommand();
            select.CommandText = "SELECT id, mapping_id, issue_type, instructions, created_at, updated_at FROM instruction_profiles WHERE mapping_id = $mid AND issue_type = $type LIMIT 1";
            select.Parameters.AddWithValue("$mid", mappingId);
            select.Parameters.AddWithValue("$type", issueType);
            using var reader = select.ExecuteReader();

            if (!reader.Read())
                throw new InvalidOperationException("Failed to save instruction profile");

            var result = new JsonObject
            {
                ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
                ["mapping_id"] = reader.GetInt32(reader.GetOrdinal("mapping_id")),
                ["issue_type"] = reader.GetString(reader.GetOrdinal("issue_type")),
                ["instructions"] = reader.GetString(reader.GetOrdinal("instructions")),
                ["created_at"] = reader.GetString(reader.GetOrdinal("created_at")),
                ["updated_at"] = reader.GetString(reader.GetOrdinal("updated_at"))
            };

            _onChange();
            return result;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<string>> ListEnabledMappingDirectories()
    {
        await _dbLock.WaitAsync();

        try
        {
            using var conn = _openConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT directory FROM project_mappings WHERE enabled = 1";
            using var reader = cmd.ExecuteReader();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new List<string>();

            while (reader.Read())
            {
                var d = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(d))
                    continue;

                var key = d.Replace('\\', '/');
                if (seen.Add(key))
                    dirs.Add(d);
            }

            return dirs;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static MappingRecord? MapMapping(SqliteDataReader reader)
    {
        return new MappingRecord
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            SonarProjectKey = reader.GetString(reader.GetOrdinal("sonar_project_key")),
            Directory = reader.GetString(reader.GetOrdinal("directory")),
            Branch = reader.IsDBNull(reader.GetOrdinal("branch")) ? null : reader.GetString(reader.GetOrdinal("branch")),
            Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) != 0,
            CreatedAt = reader.GetString(reader.GetOrdinal("created_at")),
            UpdatedAt = reader.GetString(reader.GetOrdinal("updated_at"))
        };
    }
}
