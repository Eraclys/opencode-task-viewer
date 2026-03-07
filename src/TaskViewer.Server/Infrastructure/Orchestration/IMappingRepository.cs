using System.Text.Json.Nodes;

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
        string now);

    Task<JsonObject?> GetInstructionProfile(int mappingId, string issueType);

    Task<JsonObject> UpsertInstructionProfile(
        int mappingId,
        string issueType,
        string instructions,
        string now);

    Task<List<string>> ListEnabledMappingDirectories();
}
