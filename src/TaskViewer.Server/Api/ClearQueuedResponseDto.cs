using System.Text.Json.Serialization;

namespace TaskViewer.Server.Api;

public sealed class ClearQueuedResponseDto
{
    [JsonPropertyName("cleared")]
    public required int Cleared { get; init; }
}
