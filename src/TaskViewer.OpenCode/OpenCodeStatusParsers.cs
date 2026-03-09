using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskViewer.OpenCode;

public static class OpenCodeStatusParsers
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Dictionary<string, string> ParseWorkingStatusMap(string? data)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(data))
            return map;

        Dictionary<string, SessionStatusPayload>? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, SessionStatusPayload>>(data, JsonOptions);
        }
        catch (JsonException)
        {
            return map;
        }

        if (parsed is null)
            return map;

        foreach (var kv in parsed)
        {
            var statusType = kv.Value?.Type?.Trim().ToLowerInvariant();

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
