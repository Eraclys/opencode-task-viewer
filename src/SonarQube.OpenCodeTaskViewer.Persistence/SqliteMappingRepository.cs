using Dapper;
using Microsoft.Data.Sqlite;
using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Persistence;

public sealed class SqliteMappingRepository : IMappingRepository
{
    readonly Action _onChange;
    readonly Func<SqliteConnection> _openConnection;

    public SqliteMappingRepository(Func<SqliteConnection> openConnection, Action onChange)
    {
        _openConnection = openConnection;
        _onChange = onChange;
    }

    public async Task<List<MappingRecord>> ListMappings(CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();

        var rows = await conn.QueryAsync<MappingRow>(
            Cmd(
                @"
SELECT
    id AS Id,
    sonar_project_key AS SonarProjectKey,
    directory AS Directory,
    branch AS Branch,
    enabled AS Enabled,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM project_mappings
ORDER BY sonar_project_key COLLATE NOCASE ASC",
                cancellationToken: cancellationToken));

        return rows.Select(MapMapping).ToList();
    }

    public async Task<MappingRecord?> GetMappingById(int id, CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();

        var row = await conn.QuerySingleOrDefaultAsync<MappingRow>(
            Cmd(
                @"
SELECT
    id AS Id,
    sonar_project_key AS SonarProjectKey,
    directory AS Directory,
    branch AS Branch,
    enabled AS Enabled,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM project_mappings
WHERE id = @Id
LIMIT 1",
                new
                {
                    Id = id
                },
                cancellationToken: cancellationToken));

        return row is null ? null : MapMapping(row);
    }

    public async Task<bool> DeleteMapping(int id, CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();
        using var tx = conn.BeginTransaction();

        var deleted = await conn.ExecuteAsync(
            Cmd(
                @"
DELETE FROM project_mappings
WHERE id = @Id",
                new
                {
                    Id = id
                },
                tx,
                cancellationToken));

        tx.Commit();

        if (deleted > 0)
            _onChange();

        return deleted > 0;
    }

    public async Task<MappingRecord> UpsertMapping(
        int? id,
        string sonarProjectKey,
        string directory,
        string? branch,
        bool enabled,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var nowIso = now.ToString("O");
        var normalizedBranch = SqliteOrchestrationDataMapper.NullIfWhiteSpace(branch);

        using var conn = _openConnection();

        if (id.HasValue &&
            id.Value > 0)
        {
            var updated = await conn.ExecuteAsync(
                Cmd(
                    @"
UPDATE project_mappings
SET sonar_project_key = @SonarProjectKey,
    directory = @Directory,
    branch = @Branch,
    enabled = @Enabled,
    updated_at = @UpdatedAt
WHERE id = @Id",
                    new
                    {
                        Id = id.Value,
                        SonarProjectKey = sonarProjectKey,
                        Directory = directory,
                        Branch = normalizedBranch,
                        Enabled = enabled ? 1 : 0,
                        UpdatedAt = nowIso
                    },
                    cancellationToken: cancellationToken));

            if (updated == 0)
                throw new InvalidOperationException("Mapping not found");

            var row = await conn.QuerySingleOrDefaultAsync<MappingRow>(
                Cmd(
                    @"
SELECT
    id AS Id,
    sonar_project_key AS SonarProjectKey,
    directory AS Directory,
    branch AS Branch,
    enabled AS Enabled,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM project_mappings
WHERE id = @Id
LIMIT 1",
                    new
                    {
                        Id = id.Value
                    },
                    cancellationToken: cancellationToken));

            if (row is null)
                throw new InvalidOperationException("Mapping not found");

            _onChange();

            return MapMapping(row);
        }

        await conn.ExecuteAsync(
            Cmd(
                @"
INSERT INTO project_mappings (sonar_project_key, directory, branch, enabled, created_at, updated_at)
VALUES (@SonarProjectKey, @Directory, @Branch, @Enabled, @CreatedAt, @UpdatedAt)
ON CONFLICT(sonar_project_key) DO UPDATE SET
  directory = excluded.directory,
  branch = excluded.branch,
  enabled = excluded.enabled,
  updated_at = excluded.updated_at",
                new
                {
                    SonarProjectKey = sonarProjectKey,
                    Directory = directory,
                    Branch = normalizedBranch,
                    Enabled = enabled ? 1 : 0,
                    CreatedAt = nowIso,
                    UpdatedAt = nowIso
                },
                cancellationToken: cancellationToken));

        var insertedRow = await conn.QuerySingleOrDefaultAsync<MappingRow>(
            Cmd(
                @"
SELECT
    id AS Id,
    sonar_project_key AS SonarProjectKey,
    directory AS Directory,
    branch AS Branch,
    enabled AS Enabled,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM project_mappings
WHERE sonar_project_key = @SonarProjectKey
LIMIT 1",
                new
                {
                    SonarProjectKey = sonarProjectKey
                },
                cancellationToken: cancellationToken));

        if (insertedRow is null)
            throw new InvalidOperationException("Failed to save mapping");

        _onChange();

        return MapMapping(insertedRow);
    }

    public async Task<InstructionProfileRecord?> GetInstructionProfile(int mappingId, SonarIssueType issueType, CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();

        var row = await conn.QuerySingleOrDefaultAsync<InstructionProfileRow>(
            Cmd(
                @"
SELECT
    id AS Id,
    mapping_id AS MappingId,
    issue_type AS IssueType,
    instructions AS Instructions,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM instruction_profiles
WHERE mapping_id = @MappingId AND issue_type = @IssueType
LIMIT 1",
                new
                {
                    MappingId = mappingId,
                    IssueType = issueType.Value
                },
                cancellationToken: cancellationToken));

        return row is null ? null : MapInstructionProfile(row);
    }

    public async Task<InstructionProfileRecord> UpsertInstructionProfile(
        int mappingId,
        SonarIssueType issueType,
        string instructions,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var nowIso = now.ToString("O");
        using var conn = _openConnection();

        await conn.ExecuteAsync(
            Cmd(
                @"
INSERT INTO instruction_profiles (mapping_id, issue_type, instructions, created_at, updated_at)
VALUES (@MappingId, @IssueType, @Instructions, @CreatedAt, @UpdatedAt)
ON CONFLICT(mapping_id, issue_type) DO UPDATE SET
  instructions = excluded.instructions,
  updated_at = excluded.updated_at",
                new
                {
                    MappingId = mappingId,
                    IssueType = issueType.Value,
                    Instructions = instructions,
                    CreatedAt = nowIso,
                    UpdatedAt = nowIso
                },
                cancellationToken: cancellationToken));

        var row = await conn.QuerySingleOrDefaultAsync<InstructionProfileRow>(
            Cmd(
                @"
SELECT
    id AS Id,
    mapping_id AS MappingId,
    issue_type AS IssueType,
    instructions AS Instructions,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM instruction_profiles
WHERE mapping_id = @MappingId AND issue_type = @IssueType
LIMIT 1",
                new
                {
                    MappingId = mappingId,
                    IssueType = issueType.Value
                },
                cancellationToken: cancellationToken));

        if (row is null)
            throw new InvalidOperationException("Failed to save instruction profile");

        _onChange();

        return MapInstructionProfile(row);
    }

    public async Task<List<string>> ListEnabledMappingDirectories(CancellationToken cancellationToken = default)
    {
        using var conn = _openConnection();

        var directories = await conn.QueryAsync<string>(
            Cmd(
                @"
SELECT directory
FROM project_mappings
WHERE enabled = 1",
                cancellationToken: cancellationToken));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var directory in directories)
        {
            var trimmed = directory?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var key = trimmed.Replace('\\', '/');

            if (seen.Add(key))
                result.Add(trimmed);
        }

        return result;
    }

    static MappingRecord MapMapping(MappingRow row)
    {
        return new MappingRecord
        {
            Id = row.Id,
            SonarProjectKey = row.SonarProjectKey,
            Directory = row.Directory,
            Branch = row.Branch,
            Enabled = row.Enabled != 0,
            CreatedAt = SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.CreatedAt),
            UpdatedAt = SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.UpdatedAt)
        };
    }

    static InstructionProfileRecord MapInstructionProfile(InstructionProfileRow row)
    {
        return new InstructionProfileRecord
        {
            Id = row.Id,
            MappingId = row.MappingId,
            IssueType = row.IssueType,
            Instructions = row.Instructions,
            CreatedAt = SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.CreatedAt),
            UpdatedAt = SqliteOrchestrationDataMapper.ParseRequiredDateTime(row.UpdatedAt)
        };
    }

    static CommandDefinition Cmd(
        string sql,
        object? param = null,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        => new(
            sql,
            param,
            transaction,
            cancellationToken: cancellationToken);

    sealed class MappingRow
    {
        public int Id { get; init; }
        public string SonarProjectKey { get; init; } = string.Empty;
        public string Directory { get; init; } = string.Empty;
        public string? Branch { get; init; }
        public int Enabled { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;
    }

    sealed class InstructionProfileRow
    {
        public int Id { get; init; }
        public int MappingId { get; init; }
        public string IssueType { get; init; } = string.Empty;
        public string Instructions { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;
    }
}
