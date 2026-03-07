using System.Text.Json.Serialization;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class QueueStatsDto
{
    public required int Queued { get; init; }
    public required int Dispatching { get; init; }

    [JsonPropertyName("session_created")]
    public required int SessionCreated { get; init; }

    public required int Done { get; init; }
    public required int Failed { get; init; }
    public required int Cancelled { get; init; }
}
