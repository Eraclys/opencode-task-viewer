using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskViewer.OpenCode;

public static class OpenCodeDispatchParsers
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string? ParseCreatedSessionId(string? created)
    {
        if (string.IsNullOrWhiteSpace(created))
            return null;

        OpenCodeCreatedSessionPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<OpenCodeCreatedSessionPayload>(created, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        var sessionId = payload?.Id?.Trim();
        return string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
    }

    sealed class OpenCodeCreatedSessionPayload
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }
}
