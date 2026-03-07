using TaskViewer.Server;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class DisabledSonarIssuesReadService : ISonarIssuesReadService
{
    public Task<SonarIssuesPage> ListIssuesAsync(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        int page,
        int pageSize,
        IReadOnlyList<string> ruleKeys)
    {
        throw new InvalidOperationException("SonarQube is not configured");
    }
}
