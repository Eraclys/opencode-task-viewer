using System.Text.Json.Nodes;
using TaskViewer.Server;

namespace TaskViewer.Server.Application.Orchestration;

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

public sealed record SonarEnqueueAllIssuesResult(
    IReadOnlyList<JsonNode?> Issues,
    int Matched,
    bool Truncated);
