using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application.Orchestration;

interface IOrchestrationMappingService
{
    Task<List<MappingRecord>> ListMappingsAsync();
    Task<MappingRecord?> GetMappingByIdAsync(object? mappingId);
    Task<MappingRecord> UpsertMappingAsync(JsonNode? payload);
    Task<JsonObject?> GetInstructionProfileAsync(object? mappingId, string? issueType);
    Task<JsonObject> UpsertInstructionProfileAsync(object? mappingId, string? issueType, string? instructions);
}
