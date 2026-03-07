namespace TaskViewer.Server.Application.Orchestration;

public sealed class DisabledSonarRulesReadService : ISonarRulesReadService
{
    public Task<SonarRulesSummary> SummarizeRulesAsync(
        MappingRecord mapping,
        string? issueType,
        string? issueStatus,
        int maxScanIssues) => throw new InvalidOperationException("SonarQube is not configured");
}
