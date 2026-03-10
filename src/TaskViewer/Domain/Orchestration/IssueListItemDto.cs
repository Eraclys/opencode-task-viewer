using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed class IssueListItemDto
{
    public required string Key { get; init; }
    public string? Type { get; init; }
    public SonarIssueType ParsedType => SonarIssueType.FromRaw(Type);
    public string? Severity { get; init; }
    public SonarIssueSeverity ParsedSeverity => SonarIssueSeverity.FromRaw(Severity);
    public string? Rule { get; init; }
    public string? Message { get; init; }
    public string? Component { get; init; }
    public int? Line { get; init; }
    public string? Status { get; init; }
    public SonarIssueStatus ParsedStatus => SonarIssueStatus.FromRaw(Status);
    public string? RelativePath { get; init; }
    public string? AbsolutePath { get; init; }
}
