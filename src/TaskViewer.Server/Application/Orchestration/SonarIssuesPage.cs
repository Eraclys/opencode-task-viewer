namespace TaskViewer.Server.Application.Orchestration;

public sealed record SonarIssuesPage(
    int PageIndex,
    int PageSize,
    int Total,
    IReadOnlyList<SonarIssueSummaryItem> Issues);
