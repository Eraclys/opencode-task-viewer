using System.Text.Json.Nodes;
using TaskViewer.Domain;

namespace TaskViewer.Infrastructure.OpenCode;

public static class OpenCodeEventParser
{
    public static OpenCodeEventEnvelope? Parse(JsonNode? evt)
    {
        var directoryRaw = evt?["directory"]?.ToString();
        var directory = DirectoryPath.Normalize(directoryRaw) ?? directoryRaw;
        var type = evt?["payload"]?["type"]?.ToString().Trim();

        if (string.IsNullOrWhiteSpace(type))
            return null;

        var properties = evt?["payload"]?["properties"];
        var sessionId = ReadSessionId(properties);
        var statusType = ReadStatusType(properties);

        return new OpenCodeEventEnvelope(directory, type, sessionId, statusType);
    }

    static string? ReadSessionId(JsonNode? properties)
        => properties?["sessionID"]?.ToString() ?? properties?["sessionId"]?.ToString();

    static string? ReadStatusType(JsonNode? properties)
        => properties?["status"]?["type"]?.ToString() ?? properties?["type"]?.ToString();
}
