using TaskViewer.SonarQube;

namespace TaskViewer.Infrastructure.Orchestration;

public sealed record UpsertInstructionProfileRequest(
    int? MappingId,
    SonarIssueType IssueType,
    string? Instructions);
