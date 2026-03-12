using System.Text.Json.Serialization;

namespace OpenCode.Client;

public sealed class OpenCodeSsePayload
{
    [JsonPropertyName("type")] public string? Type { get; init; }

    [JsonPropertyName("properties")] public OpenCodeSseProperties? Properties { get; init; }
}