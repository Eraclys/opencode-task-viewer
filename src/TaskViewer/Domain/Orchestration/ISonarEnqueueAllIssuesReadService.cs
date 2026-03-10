using TaskViewer.Infrastructure.Persistence;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public interface ISonarEnqueueAllIssuesReadService
{
    Task<SonarEnqueueAllIssuesResult> CollectMatchingIssuesAsync(
        MappingRecord mapping,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueSeverity> severities,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        IReadOnlyList<string> ruleKeys,
        int maxScanIssues,
        CancellationToken cancellationToken = default);
}
