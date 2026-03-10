using TaskViewer.SonarQube;

namespace TaskViewer.Infrastructure.Orchestration;

public sealed record EnqueueAllRequest(
    int? MappingId,
    SonarIssueType IssueType,
    string? RuleKeys,
    IReadOnlyList<SonarIssueStatus> IssueStatuses,
    IReadOnlyList<SonarIssueSeverity> Severities,
    string? Instructions);
