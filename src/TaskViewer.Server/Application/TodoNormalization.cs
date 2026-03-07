using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application;

public static class TodoNormalization
{
    public static string NormalizeStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "pending";

        var compact = raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");

        return compact switch
        {
            "inprogress" or "in_progress" => "in_progress",
            "done" or "complete" or "completed" => "completed",
            "canceled" or "cancelled" => "cancelled",
            "pending" or "todo" or "idle" => "pending",
            _ => compact
        };
    }

    public static string? NormalizePriority(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().ToLowerInvariant();
        return normalized switch
        {
            "p0" or "0" or "urgent" or "p1" or "1" => "high",
            "p2" or "2" => "medium",
            "p3" or "3" => "low",
            "high" or "medium" or "low" => normalized,
            _ => normalized
        };
    }

    public static JsonObject NormalizeTodo(JsonObject? todo)
    {
        var item = todo ?? new JsonObject();
        var content = item["content"]?.ToString() ?? item["text"]?.ToString() ?? item["title"]?.ToString() ?? string.Empty;

        return new JsonObject
        {
            ["content"] = content,
            ["status"] = NormalizeStatus(item["status"]?.ToString() ?? item["state"]?.ToString()),
            ["priority"] = NormalizePriority(item["priority"]?.ToString())
        };
    }
}
