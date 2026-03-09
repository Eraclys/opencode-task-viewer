using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

interface IOrchestrationMappingService
{
    Task<List<MappingRecord>> ListMappingsAsync();
    Task<MappingRecord?> GetMappingByIdAsync(int? mappingId);
    Task<bool> DeleteMappingAsync(int? mappingId);
    Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request);
    Task<InstructionProfileRecord?> GetInstructionProfileAsync(int? mappingId, string? issueType);
    Task<InstructionProfileRecord> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request);
}
