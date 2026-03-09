using System.Text.Json.Nodes;

namespace TaskViewer.OpenCode;

public static class OpenCodeDispatchParsers
{
    public static string? ParseCreatedSessionId(JsonNode? created)
    {
        var sessionId = created?["id"]?.ToString().Trim();
        return string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
    }
}
