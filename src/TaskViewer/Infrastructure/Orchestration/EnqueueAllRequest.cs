namespace TaskViewer.Infrastructure.Orchestration;

public sealed record EnqueueAllRequest(
    int? MappingId,
    string? IssueType,
    string? RuleKeys,
    string? IssueStatus,
    string? Severity,
    string? Instructions);
