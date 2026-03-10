using System.Text.Json.Serialization;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed class InstructionProfileDto
{
    public int? MappingId { get; init; }
    [JsonIgnore]
    public SonarIssueType IssueType { get; init; }
    [JsonPropertyName("issueType")]
    public string? IssueTypeValue => IssueType.OrNull();
    public string? Instructions { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
