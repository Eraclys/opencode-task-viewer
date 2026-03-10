using System.Text.Json.Serialization;

namespace SonarQube.OpenCodeTaskViewer.Server.Api;

public sealed class LastAssistantMessageResponseDto
{
    [JsonPropertyName("sessionId")] public required string SessionId { get; init; }

    [JsonPropertyName("message")] public string? Message { get; init; }

    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; init; }
}
