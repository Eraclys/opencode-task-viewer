using System.Text.Json.Serialization;

namespace OpenCode.Client;

public sealed class OpenCodeSseEvent
{
    [JsonPropertyName("directory")] public string? Directory { get; init; }

    [JsonPropertyName("payload")] public OpenCodeSsePayload? Payload { get; init; }
}