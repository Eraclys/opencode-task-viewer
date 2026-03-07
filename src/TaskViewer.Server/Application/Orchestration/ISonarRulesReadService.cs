namespace TaskViewer.Server.Application.Orchestration;

public interface ISonarRulesReadService
{
    Task<SonarRulesSummary> SummarizeRulesAsync(
        MappingRecord mapping,
        string? issueType,
        string? issueStatus,
        int maxScanIssues);
}

public sealed record SonarRuleSummaryItem(string Key, string Name, int Count);

public sealed record SonarRulesSummary(
    string? IssueType,
    string? IssueStatus,
    int ScannedIssues,
    bool Truncated,
    IReadOnlyList<SonarRuleSummaryItem> Rules);
