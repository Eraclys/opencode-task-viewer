using TaskViewer.Infrastructure.Persistence;
using System.Text.Json.Serialization;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed class RulesListDto
{
    public required MappingRecord Mapping { get; init; }
    [JsonIgnore]
    public SonarIssueType IssueType { get; init; }
    [JsonPropertyName("issueType")]
    public string? IssueTypeValue => IssueType.OrNull();
    [JsonIgnore]
    public SonarIssueStatus IssueStatus { get; init; }
    [JsonPropertyName("issueStatus")]
    public string? IssueStatusValue => IssueStatus.OrNull();
    public required int ScannedIssues { get; init; }
    public required bool Truncated { get; init; }
    public required List<RuleCountDto> Rules { get; init; }
}
