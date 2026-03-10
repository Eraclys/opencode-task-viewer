using System.Text.Json.Serialization;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class QueueStatsDto
{
    public required int Queued { get; init; }
    public required int Dispatching { get; init; }

    [JsonPropertyName("session_created")] public required int SessionCreated { get; init; }

    public required int Done { get; init; }
    public required int Failed { get; init; }
    public required int Cancelled { get; init; }

    public int? Leased { get; init; }
    public int? Running { get; init; }

    [JsonPropertyName("awaiting_review")] public int? AwaitingReview { get; init; }

    public int? Rejected { get; init; }
}
