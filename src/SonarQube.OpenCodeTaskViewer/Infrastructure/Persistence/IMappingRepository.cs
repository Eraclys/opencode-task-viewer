using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

public interface IMappingRepository
{
    Task<List<MappingRecord>> ListMappings(CancellationToken cancellationToken = default);
    Task<MappingRecord?> GetMappingById(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteMapping(int id, CancellationToken cancellationToken = default);

    Task<MappingRecord> UpsertMapping(
        int? id,
        string sonarProjectKey,
        string directory,
        string? branch,
        bool enabled,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<InstructionProfileRecord?> GetInstructionProfile(int mappingId, SonarIssueType issueType, CancellationToken cancellationToken = default);

    Task<InstructionProfileRecord> UpsertInstructionProfile(
        int mappingId,
        SonarIssueType issueType,
        string instructions,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<List<string>> ListEnabledMappingDirectories(CancellationToken cancellationToken = default);
}
