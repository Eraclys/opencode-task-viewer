namespace TaskViewer.Domain.Orchestration;

public sealed record SonarIssuesPage(
    int PageIndex,
    int PageSize,
    int Total,
    IReadOnlyList<SonarIssueSummaryItem> Issues);
