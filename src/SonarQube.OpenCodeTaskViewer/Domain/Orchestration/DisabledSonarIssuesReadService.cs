using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class DisabledSonarIssuesReadService : ISonarIssuesReadService
{
    public Task<SonarIssuesPage> ListIssuesAsync(
        MappingRecord mapping,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueSeverity> severities,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        int page,
        int pageSize,
        IReadOnlyList<string> ruleKeys,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("SonarQube is not configured");
}
