using System.Text.Json.Serialization;

namespace TaskViewer.Server.Api;

public sealed class ErrorResponseDto
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }
}
