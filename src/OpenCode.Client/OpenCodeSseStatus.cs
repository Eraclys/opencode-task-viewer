using System.Text.Json.Serialization;

namespace OpenCode.Client;

public sealed class OpenCodeSseStatus
{
    [JsonPropertyName("type")] public string? Type { get; init; }
}