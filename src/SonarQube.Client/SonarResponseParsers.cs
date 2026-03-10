using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonarQube.Client;

public static class SonarResponseParsers
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static SonarIssuesSearchResponse ParseIssuesSearchResponse(string? data, int fallbackPageIndex, int fallbackPageSize)
    {
        var payload = Deserialize<SonarIssuesSearchPayload>(data);
        var issues = payload?.Issues?.Select(ParseIssue).OfType<SonarIssueTransport>().ToList() ?? [];

        return new SonarIssuesSearchResponse(
            payload?.Paging?.PageIndex ?? fallbackPageIndex,
            payload?.Paging?.PageSize ?? fallbackPageSize,
            payload?.Paging?.Total,
            issues);
    }

    public static SonarRuleDetailsResponse ParseRuleDetails(string? data)
        => new(ParseRuleName(data));

    public static string? ParseRuleName(string? data)
    {
        var name = Deserialize<SonarRuleDetailsPayload>(data)?.Rule?.Name?.Trim();

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static SonarIssueTransport? ParseIssue(SonarIssuePayload? raw)
    {
        if (raw is null)
            return null;

        return new SonarIssueTransport(
            raw.Key?.Trim(),
            raw.IssueKey?.Trim(),
            raw.Type?.Trim(),
            raw.IssueType?.Trim(),
            raw.Severity?.Trim(),
            raw.Rule?.Trim(),
            raw.Message?.Trim(),
            raw.Component?.Trim(),
            raw.File?.Trim(),
            raw.Line?.ToString()?.Trim(),
            raw.Status?.Trim());
    }

    public static string? ParseIssueRuleKey(SonarIssueTransport? raw)
    {
        var key = raw?.Rule?.Trim();

        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    static T? Deserialize<T>(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(data, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    sealed class SonarIssuesSearchPayload
    {
        [JsonPropertyName("paging")] public SonarPagingPayload? Paging { get; init; }

        [JsonPropertyName("issues")] public List<SonarIssuePayload>? Issues { get; init; }
    }

    sealed class SonarPagingPayload
    {
        [JsonPropertyName("pageIndex")] public int? PageIndex { get; init; }

        [JsonPropertyName("pageSize")] public int? PageSize { get; init; }

        [JsonPropertyName("total")] public int? Total { get; init; }
    }

    sealed class SonarRuleDetailsPayload
    {
        [JsonPropertyName("rule")] public SonarRulePayload? Rule { get; init; }
    }

    sealed class SonarRulePayload
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
    }

    public sealed class SonarIssuePayload
    {
        [JsonPropertyName("key")] public string? Key { get; init; }

        [JsonPropertyName("issueKey")] public string? IssueKey { get; init; }

        [JsonPropertyName("type")] public string? Type { get; init; }

        [JsonPropertyName("issueType")] public string? IssueType { get; init; }

        [JsonPropertyName("severity")] public string? Severity { get; init; }

        [JsonPropertyName("rule")] public string? Rule { get; init; }

        [JsonPropertyName("message")] public string? Message { get; init; }

        [JsonPropertyName("component")] public string? Component { get; init; }

        [JsonPropertyName("file")] public string? File { get; init; }

        [JsonPropertyName("line")] public JsonElement? Line { get; init; }

        [JsonPropertyName("status")] public string? Status { get; init; }
    }
}
