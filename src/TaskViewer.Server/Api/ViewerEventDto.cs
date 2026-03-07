using System.Text.Json.Serialization;

namespace TaskViewer.Server.Api;

public sealed class ViewerEventDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}
