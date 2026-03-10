using System.Text.Json.Serialization;
using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class IssueListItemDto
{
    public required string Key { get; init; }

    [JsonIgnore] public SonarIssueType Type { get; init; }

    [JsonPropertyName("type")] public string? TypeValue => Type.OrNull();

    [JsonIgnore] public SonarIssueSeverity Severity { get; init; }

    [JsonPropertyName("severity")] public string? SeverityValue => Severity.OrNull();

    public string? Rule { get; init; }
    public string? Message { get; init; }
    public string? Component { get; init; }
    public int? Line { get; init; }

    [JsonIgnore] public SonarIssueStatus Status { get; init; }

    [JsonPropertyName("status")] public string? StatusValue => Status.OrNull();

    public string? RelativePath { get; init; }
    public string? AbsolutePath { get; init; }
}
