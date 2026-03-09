using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed record EnqueueContext(
    MappingRecord Mapping,
    string? Type,
    string InstructionText);
