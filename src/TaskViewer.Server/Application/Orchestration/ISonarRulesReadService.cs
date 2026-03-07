namespace TaskViewer.Server.Application.Orchestration;

public interface ISonarRulesReadService
{
    Task<SonarRulesSummary> SummarizeRulesAsync(
        MappingRecord mapping,
        string? issueType,
        string? issueStatus,
        int maxScanIssues);
}
