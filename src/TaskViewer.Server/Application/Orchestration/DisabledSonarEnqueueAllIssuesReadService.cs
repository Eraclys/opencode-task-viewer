namespace TaskViewer.Server.Application.Orchestration;

public sealed class DisabledSonarEnqueueAllIssuesReadService : ISonarEnqueueAllIssuesReadService
{
    public Task<SonarEnqueueAllIssuesResult> CollectMatchingIssuesAsync(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        IReadOnlyList<string> ruleKeys,
        int maxScanIssues) =>
        throw new InvalidOperationException("SonarQube is not configured");
}
