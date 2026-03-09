namespace TaskViewer.Application.Orchestration;

public sealed record SonarRulesSummary(
    string? IssueType,
    string? IssueStatus,
    int ScannedIssues,
    bool Truncated,
    IReadOnlyList<SonarRuleSummaryItem> Rules);
