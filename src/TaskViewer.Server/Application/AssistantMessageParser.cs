using System.Text.Json.Nodes;
using TaskViewer.Server.Domain;

namespace TaskViewer.Server.Application;

public static class AssistantMessageParser
{
    public static string GetMessageRole(JsonObject? message)
    {
        return (message?["info"]?["role"]?.ToString()
                ?? message?["role"]?.ToString()
                ?? message?["author"]?["role"]?.ToString()
                ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    public static string ExtractAssistantMessageText(JsonObject? message)
    {
        var candidates = new[]
        {
            message?["content"],
            message?["text"],
            message?["message"],
            message?["body"],
            message?["output"],
            message?["response"],
            message?["parts"],
            message?["data"],
            message?["info"]?["content"],
            message?["info"]?["text"]
        };

        foreach (var candidate in candidates)
        {
            var text = ExtractTextFragment(candidate);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return string.Empty;
    }

    public static string? ExtractMessageCreatedAt(JsonObject? message)
    {
        var candidates = new[]
        {
            message?["info"]?["time"]?["created"]?.ToString(),
            message?["time"]?["created"]?.ToString(),
            message?["createdAt"]?.ToString(),
            message?["timestamp"]?.ToString()
        };

        foreach (var candidate in candidates)
        {
            var timestamp = TimeParser.ParseIsoTime(candidate);
            if (!string.IsNullOrWhiteSpace(timestamp))
                return timestamp;
        }

        return null;
    }

    private static string ExtractTextFragment(JsonNode? value, int depth = 0)
    {
        if (depth > 5 || value is null)
            return string.Empty;

        switch (value)
        {
            case JsonValue jsonValue:
                return jsonValue.ToString().Trim();
            case JsonArray jsonArray:
                return string.Join(
                    "\n",
                    jsonArray.Select(item => ExtractTextFragment(item, depth + 1)).Where(item => !string.IsNullOrWhiteSpace(item)))
                    .Trim();
            case JsonObject jsonObject:
                foreach (var key in new[] { "text", "content", "message", "body", "value", "markdown" })
                {
                    var text = ExtractTextFragment(jsonObject[key], depth + 1);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }

                if (jsonObject["parts"] is JsonArray parts)
                {
                    var text = ExtractTextFragment(parts, depth + 1);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }

                if (string.Equals(jsonObject["type"]?.ToString(), "text", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(jsonObject["text"]?.ToString()))
                {
                    return jsonObject["text"]!.ToString().Trim();
                }

                return string.Empty;
            default:
                return string.Empty;
        }
    }
}
