namespace TaskViewer.Application.Orchestration;

public sealed class IssuesPagingDto
{
    public required int PageIndex { get; init; }
    public required int PageSize { get; init; }
    public required int Total { get; init; }
}
