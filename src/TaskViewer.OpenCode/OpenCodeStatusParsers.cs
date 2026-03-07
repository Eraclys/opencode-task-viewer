using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TaskViewer.OpenCode;

public static class OpenCodeStatusParsers
{
    public static Dictionary<string, string> ParseWorkingStatusMap(JsonNode? data)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (data is null || data is JsonValue || data is JsonArray)
            return map;

        Dictionary<string, SessionStatusPayload>? parsed;

        try
        {
            parsed = data.Deserialize<Dictionary<string, SessionStatusPayload>>();
        }
        catch (JsonException)
        {
            return map;
        }

        if (parsed is null)
            return map;

        foreach (var kv in parsed)
        {
            var statusType = kv.Value?.Type?.Trim()?.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(statusType))
                map[kv.Key] = statusType;
        }

        return map;
    }

    sealed class SessionStatusPayload
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }
    }
}
