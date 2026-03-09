namespace TaskViewer.Application.Orchestration;

public interface ISonarEnqueueAllIssuesReadService
{
    Task<SonarEnqueueAllIssuesResult> CollectMatchingIssuesAsync(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        IReadOnlyList<string> ruleKeys,
        int maxScanIssues);
}
