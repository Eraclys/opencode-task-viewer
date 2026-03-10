using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCode.Client;

public static class OpenCodeStatusParsers
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Dictionary<string, SessionRuntimeStatus> ParseWorkingStatusMap(string? data)
    {
        var map = new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

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
            var raw = kv.Value?.Type;

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var status = SessionRuntimeStatus.FromRaw(raw);

            map[kv.Key] = status;
        }

        return map;
    }

    sealed class SessionStatusPayload
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
    }
}
