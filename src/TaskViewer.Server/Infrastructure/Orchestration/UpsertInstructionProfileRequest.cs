namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed record UpsertInstructionProfileRequest(
    int? MappingId,
    string? IssueType,
    string? Instructions);
