using System.Text.Json.Serialization;
using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record SonarIssueSummaryItem(
    string Key,
    [property: JsonIgnore] SonarIssueType Type,
    [property: JsonIgnore] SonarIssueSeverity Severity,
    string? Rule,
    string? Message,
    string? Component,
    int? Line,
    [property: JsonIgnore] SonarIssueStatus Status,
    string? RelativePath,
    string? AbsolutePath)
{
    [JsonPropertyName("type")] public string? TypeValue => Type.OrNull();

    [JsonPropertyName("severity")] public string? SeverityValue => Severity.OrNull();

    [JsonPropertyName("status")] public string? StatusValue => Status.OrNull();
}
