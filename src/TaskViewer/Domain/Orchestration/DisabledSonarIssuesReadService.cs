using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed class DisabledSonarIssuesReadService : ISonarIssuesReadService
{
    public Task<SonarIssuesPage> ListIssuesAsync(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        int page,
        int pageSize,
        IReadOnlyList<string> ruleKeys,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("SonarQube is not configured");
}
