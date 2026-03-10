using TaskViewer.Infrastructure.Persistence;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed class RulesListDto
{
    public required MappingRecord Mapping { get; init; }
    public string? IssueType { get; init; }
    public SonarIssueType ParsedIssueType => SonarIssueType.FromRaw(IssueType);
    public string? IssueStatus { get; init; }
    public SonarIssueStatus ParsedIssueStatus => SonarIssueStatus.FromRaw(IssueStatus);
    public required int ScannedIssues { get; init; }
    public required bool Truncated { get; init; }
    public required List<RuleCountDto> Rules { get; init; }
}
