using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed record SonarIssueSummaryItem(
    string Key,
    string Type,
    string? Severity,
    string? Rule,
    string? Message,
    string? Component,
    int? Line,
    string? Status,
    string? RelativePath,
    string? AbsolutePath)
{
    public SonarIssueType ParsedType => SonarIssueType.FromRaw(Type);
    public SonarIssueSeverity ParsedSeverity => SonarIssueSeverity.FromRaw(Severity);
    public SonarIssueStatus ParsedStatus => SonarIssueStatus.FromRaw(Status);
}
