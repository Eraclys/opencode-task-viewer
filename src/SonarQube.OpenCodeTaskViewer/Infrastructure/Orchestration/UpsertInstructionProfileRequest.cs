using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;

public sealed record UpsertInstructionProfileRequest(
    int? MappingId,
    SonarIssueType IssueType,
    string? Instructions);
