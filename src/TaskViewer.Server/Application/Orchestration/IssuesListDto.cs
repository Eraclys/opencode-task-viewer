namespace TaskViewer.Server.Application.Orchestration;

public sealed class IssuesListDto
{
    public required MappingRecord Mapping { get; init; }
    public required IssuesPagingDto Paging { get; init; }
    public required List<IssueListItemDto> Issues { get; init; }
}
