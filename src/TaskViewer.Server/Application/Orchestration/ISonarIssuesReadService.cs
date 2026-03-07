namespace TaskViewer.Server.Application.Orchestration;

public interface ISonarIssuesReadService
{
    Task<SonarIssuesPage> ListIssuesAsync(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        int page,
        int pageSize,
        IReadOnlyList<string> ruleKeys);
}

public sealed record SonarIssueSummaryItem(
    string Key,
    string Type,
    string? Severity,
    string? Rule,
    string? Message,
    string? Component,
    int? Line,
    string? Status,
    string? RelativePath,
    string? AbsolutePath);

public sealed record SonarIssuesPage(
    int PageIndex,
    int PageSize,
    int Total,
    IReadOnlyList<SonarIssueSummaryItem> Issues);
