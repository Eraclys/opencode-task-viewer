using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed record SonarRulesSummary(
    IReadOnlyList<SonarIssueType> IssueTypes,
    IReadOnlyList<SonarIssueStatus> IssueStatuses,
    int ScannedIssues,
    bool Truncated,
    IReadOnlyList<SonarRuleSummaryItem> Rules)
{
    public string? IssueType => ParsedIssueType.OrNull();
    public string? IssueStatus => ParsedIssueStatus.OrNull();
    public SonarIssueType ParsedIssueType => IssueTypes.Count == 1 ? IssueTypes[0] : default;
    public SonarIssueStatus ParsedIssueStatus => IssueStatuses.Count == 1 ? IssueStatuses[0] : default;
}
