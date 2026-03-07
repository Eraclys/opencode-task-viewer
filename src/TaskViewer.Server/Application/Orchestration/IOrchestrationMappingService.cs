using System.Text.Json.Nodes;
using TaskViewer.Server;

namespace TaskViewer.Server.Application.Orchestration;

internal interface IOrchestrationMappingService
{
    Task<List<MappingRecord>> ListMappingsAsync();
    Task<MappingRecord?> GetMappingByIdAsync(object? mappingId);
    Task<MappingRecord> UpsertMappingAsync(JsonNode? payload);
    Task<JsonObject?> GetInstructionProfileAsync(object? mappingId, string? issueType);
    Task<JsonObject> UpsertInstructionProfileAsync(object? mappingId, string? issueType, string? instructions);
}
