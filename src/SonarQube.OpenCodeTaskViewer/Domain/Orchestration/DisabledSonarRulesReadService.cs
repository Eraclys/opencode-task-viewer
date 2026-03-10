using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class DisabledSonarRulesReadService : ISonarRulesReadService
{
    public Task<SonarRulesSummary> SummarizeRulesAsync(
        MappingRecord mapping,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        int maxScanIssues,
        CancellationToken cancellationToken = default) => throw new InvalidOperationException("SonarQube is not configured");
}
