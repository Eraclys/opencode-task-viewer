using TaskViewer.Infrastructure.Persistence;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed class DisabledSonarEnqueueAllIssuesReadService : ISonarEnqueueAllIssuesReadService
{
    public Task<SonarEnqueueAllIssuesResult> CollectMatchingIssuesAsync(
        MappingRecord mapping,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueSeverity> severities,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        IReadOnlyList<string> ruleKeys,
        int maxScanIssues,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("SonarQube is not configured");
}
