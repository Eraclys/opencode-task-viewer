using System.Text.Json.Serialization;

namespace OpenCode.Client;

public sealed class OpenCodeSseProperties
{
    [JsonPropertyName("sessionID")] public string? LegacySessionId { get; init; }

    [JsonPropertyName("sessionId")] public string? SessionId { get; init; }

    [JsonPropertyName("status")] public OpenCodeSseStatus? Status { get; init; }

    [JsonPropertyName("type")] public string? Type { get; init; }
}