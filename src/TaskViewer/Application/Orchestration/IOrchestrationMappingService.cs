using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Application.Orchestration;

public interface IOrchestrationMappingService
{
    Task<List<MappingRecord>> ListMappingsAsync();
    Task<MappingRecord?> GetMappingByIdAsync(int? mappingId);
    Task<bool> DeleteMappingAsync(int? mappingId);
    Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request);
    Task<InstructionProfileRecord?> GetInstructionProfileAsync(int? mappingId, string? issueType);
    Task<InstructionProfileRecord> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request);
}
