using System.Text.Json.Serialization;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class TaskReviewHistoryDto
{
    [JsonIgnore] public required TaskReviewAction Action { get; init; }

    [JsonPropertyName("action")] public string? ActionValue => Action.OrNull();

    public string? Reason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
