using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record SonarRulesSummary(
    IReadOnlyList<SonarIssueType> IssueTypes,
    IReadOnlyList<SonarIssueStatus> IssueStatuses,
    int ScannedIssues,
    bool Truncated,
    IReadOnlyList<SonarRuleSummaryItem> Rules);
