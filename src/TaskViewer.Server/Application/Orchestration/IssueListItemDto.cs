namespace TaskViewer.Server.Application.Orchestration;

public sealed class IssueListItemDto
{
    public required string Key { get; init; }
    public string? Type { get; init; }
    public string? Severity { get; init; }
    public string? Rule { get; init; }
    public string? Message { get; init; }
    public string? Component { get; init; }
    public int? Line { get; init; }
    public string? Status { get; init; }
    public string? RelativePath { get; init; }
    public string? AbsolutePath { get; init; }
}
