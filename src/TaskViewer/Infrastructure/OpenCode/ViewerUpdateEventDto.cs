using System.Text.Json.Serialization;

namespace TaskViewer.Infrastructure.OpenCode;

sealed class ViewerUpdateEventDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}
