namespace TaskViewer.Server.Infrastructure.Orchestration;

public interface IMappingRepository
{
    Task<List<MappingRecord>> ListMappings();
    Task<MappingRecord?> GetMappingById(int id);

    Task<MappingRecord> UpsertMapping(
        int? id,
        string sonarProjectKey,
        string directory,
        string? branch,
        bool enabled,
        DateTimeOffset now);

    Task<InstructionProfileRecord?> GetInstructionProfile(int mappingId, string issueType);

    Task<InstructionProfileRecord> UpsertInstructionProfile(
        int mappingId,
        string issueType,
        string instructions,
        DateTimeOffset now);

    Task<List<string>> ListEnabledMappingDirectories();
}
