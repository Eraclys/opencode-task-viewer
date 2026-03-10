using TaskViewer.Infrastructure.Persistence;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public interface ISonarRulesReadService
{
    Task<SonarRulesSummary> SummarizeRulesAsync(
        MappingRecord mapping,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        int maxScanIssues,
        CancellationToken cancellationToken = default);
}
