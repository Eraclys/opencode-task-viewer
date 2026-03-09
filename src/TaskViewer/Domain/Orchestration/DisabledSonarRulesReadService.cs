using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed class DisabledSonarRulesReadService : ISonarRulesReadService
{
    public Task<SonarRulesSummary> SummarizeRulesAsync(
        MappingRecord mapping,
        string? issueType,
        string? issueStatus,
        int maxScanIssues,
        CancellationToken cancellationToken = default) => throw new InvalidOperationException("SonarQube is not configured");
}
