using System.Text.Json.Nodes;

namespace TaskViewer.SonarQube;

public static class SonarResponseParsers
{
    public static SonarIssuesSearchResponse ParseIssuesSearchResponse(JsonNode? data, int fallbackPageIndex, int fallbackPageSize)
    {
        var issues = data?["issues"] is JsonArray rawIssues
            ? rawIssues.Select(ParseIssue).OfType<SonarIssueTransport>().ToList()
            : [];

        return new SonarIssuesSearchResponse(
            ParsePageIndex(data, fallbackPageIndex),
            ParsePageSize(data, fallbackPageSize),
            ParseNullableTotal(data),
            issues);
    }

    public static SonarRuleDetailsResponse ParseRuleDetails(JsonNode? data)
        => new(ParseRuleName(data));

    public static int ParsePageIndex(JsonNode? data, int fallback)
        => ParseIntSafe(data?["paging"]?["pageIndex"]?.ToString(), fallback);

    public static int ParsePageSize(JsonNode? data, int fallback)
        => ParseIntSafe(data?["paging"]?["pageSize"]?.ToString(), fallback);

    public static int ParseTotal(JsonNode? data, int fallback)
        => ParseIntSafe(data?["paging"]?["total"]?.ToString(), fallback);

    public static int? ParseNullableTotal(JsonNode? data)
        => ParseNullableInt(data?["paging"]?["total"]?.ToString());

    public static string? ParseRuleName(JsonNode? data)
    {
        var name = data?["rule"]?["name"]?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static SonarIssueTransport? ParseIssue(JsonNode? raw)
    {
        if (raw is null)
            return null;

        return new SonarIssueTransport(
            raw["key"]?.ToString()?.Trim(),
            raw["issueKey"]?.ToString()?.Trim(),
            raw["type"]?.ToString()?.Trim(),
            raw["issueType"]?.ToString()?.Trim(),
            raw["severity"]?.ToString()?.Trim(),
            raw["rule"]?.ToString()?.Trim(),
            raw["message"]?.ToString()?.Trim(),
            raw["component"]?.ToString()?.Trim(),
            raw["file"]?.ToString()?.Trim(),
            raw["line"]?.ToString()?.Trim(),
            raw["status"]?.ToString()?.Trim());
    }

    public static string? ParseIssueRuleKey(SonarIssueTransport? raw)
    {
        var key = raw?.Rule?.Trim();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    static int ParseIntSafe(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

    static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;
}
