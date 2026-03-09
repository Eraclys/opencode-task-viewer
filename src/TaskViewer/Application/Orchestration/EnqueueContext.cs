namespace TaskViewer.Application.Orchestration;

public sealed record EnqueueContext(
    MappingRecord Mapping,
    string? Type,
    string InstructionText);
