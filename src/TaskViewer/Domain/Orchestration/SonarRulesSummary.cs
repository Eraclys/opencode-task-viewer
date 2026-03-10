using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed record SonarRulesSummary(
    string? IssueType,
    string? IssueStatus,
    int ScannedIssues,
    bool Truncated,
    IReadOnlyList<SonarRuleSummaryItem> Rules)
{
    public SonarIssueType ParsedIssueType => SonarIssueType.FromRaw(IssueType);
    public SonarIssueStatus ParsedIssueStatus => SonarIssueStatus.FromRaw(IssueStatus);
}
