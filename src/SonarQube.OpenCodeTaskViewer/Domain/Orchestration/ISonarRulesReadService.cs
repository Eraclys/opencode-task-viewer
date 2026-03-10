using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface ISonarRulesReadService
{
    Task<SonarRulesSummary> SummarizeRulesAsync(
        MappingRecord mapping,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        int maxScanIssues,
        CancellationToken cancellationToken = default);
}
