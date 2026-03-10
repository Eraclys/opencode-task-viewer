using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed class InstructionProfileDto
{
    public int? MappingId { get; init; }
    public string? IssueType { get; init; }
    public SonarIssueType ParsedIssueType => SonarIssueType.FromRaw(IssueType);
    public string? Instructions { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
