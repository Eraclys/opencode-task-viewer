using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public interface IOrchestrationMappingService
{
    Task<List<MappingRecord>> ListMappingsAsync(CancellationToken cancellationToken = default);
    Task<MappingRecord?> GetMappingByIdAsync(int? mappingId, CancellationToken cancellationToken = default);
    Task<bool> DeleteMappingAsync(int? mappingId, CancellationToken cancellationToken = default);
    Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request, CancellationToken cancellationToken = default);
    Task<InstructionProfileRecord?> GetInstructionProfileAsync(int? mappingId, string? issueType, CancellationToken cancellationToken = default);
    Task<InstructionProfileRecord> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request, CancellationToken cancellationToken = default);
}
