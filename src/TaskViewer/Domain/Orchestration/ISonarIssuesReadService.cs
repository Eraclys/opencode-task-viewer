using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public interface ISonarIssuesReadService
{
    Task<SonarIssuesPage> ListIssuesAsync(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        int page,
        int pageSize,
        IReadOnlyList<string> ruleKeys,
        CancellationToken cancellationToken = default);
}
