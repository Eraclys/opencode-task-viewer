using System.Text.Json.Serialization;

namespace TaskViewer.OpenCode;

public sealed class OpenCodeSseEvent
{
    [JsonPropertyName("directory")]
    public string? Directory { get; init; }

    [JsonPropertyName("payload")]
    public OpenCodeSsePayload? Payload { get; init; }
}

public sealed class OpenCodeSsePayload
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("properties")]
    public OpenCodeSseProperties? Properties { get; init; }
}

public sealed class OpenCodeSseProperties
{
    [JsonPropertyName("sessionID")]
    public string? LegacySessionId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("status")]
    public OpenCodeSseStatus? Status { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed class OpenCodeSseStatus
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
