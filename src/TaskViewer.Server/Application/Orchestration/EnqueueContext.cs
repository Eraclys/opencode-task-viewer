namespace TaskViewer.Server.Application.Orchestration;

public sealed record EnqueueContext(
    MappingRecord Mapping,
    string? Type,
    string InstructionText);
