using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed class RulesListDto
{
    public required MappingRecord Mapping { get; init; }
    public string? IssueType { get; init; }
    public string? IssueStatus { get; init; }
    public required int ScannedIssues { get; init; }
    public required bool Truncated { get; init; }
    public required List<RuleCountDto> Rules { get; init; }
}
