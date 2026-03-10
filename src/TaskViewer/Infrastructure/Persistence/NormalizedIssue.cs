using TaskViewer.SonarQube;

namespace TaskViewer.Infrastructure.Persistence;

public sealed class NormalizedIssue
{
    public required string Key { get; init; }
    public required string Type { get; init; }
    public SonarIssueType IssueType => SonarIssueType.FromRaw(Type);
    public string? Severity { get; init; }
    public SonarIssueSeverity IssueSeverity => SonarIssueSeverity.FromRaw(Severity);
    public string? Rule { get; init; }
    public string? Message { get; init; }
    public int? Line { get; init; }
    public string? Status { get; init; }
    public SonarIssueStatus IssueStatus => SonarIssueStatus.FromRaw(Status);
    public string? Component { get; init; }
    public string? RelativePath { get; init; }
    public string? AbsolutePath { get; init; }
}
