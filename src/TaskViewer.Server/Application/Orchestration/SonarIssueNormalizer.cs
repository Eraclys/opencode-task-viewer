using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application.Orchestration;

public static class SonarIssueNormalizer
{
    public static NormalizedIssue? NormalizeForQueue(JsonNode? rawNode, MappingRecord mapping)
    {
        if (rawNode is not JsonObject raw)
            return null;

        var key = raw["key"]?.ToString()?.Trim() ?? raw["issueKey"]?.ToString()?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(key))
            return null;

        var type = NormalizeIssueType(raw["type"]?.ToString() ?? raw["issueType"]?.ToString() ?? "CODE_SMELL") ?? "CODE_SMELL";
        var severity = raw["severity"]?.ToString()?.Trim()?.ToUpperInvariant();
        var rule = raw["rule"]?.ToString()?.Trim();
        var message = raw["message"]?.ToString()?.Trim();
        var line = ParseIntNullable(raw["line"]?.ToString());
        var status = raw["status"]?.ToString()?.Trim();
        var component = raw["component"]?.ToString()?.Trim() ?? raw["file"]?.ToString()?.Trim();

        var projectKey = mapping.SonarProjectKey?.Trim() ?? string.Empty;
        string? relativePath = null;

        if (!string.IsNullOrWhiteSpace(component))
        {
            if (!string.IsNullOrWhiteSpace(projectKey) &&
                component.StartsWith(projectKey + ":", StringComparison.Ordinal))
                relativePath = component[(projectKey.Length + 1)..];
            else
            {
                var idx = component.IndexOf(':');
                relativePath = idx >= 0 ? component[(idx + 1)..] : component;
            }
        }

        relativePath = relativePath?.Replace('\\', '/').TrimStart('/');

        var absolutePath = !string.IsNullOrWhiteSpace(relativePath)
            ? $"{mapping.Directory.TrimEnd('/')}/{relativePath}"
            : null;

        return new NormalizedIssue
        {
            Key = key,
            Type = type,
            Severity = string.IsNullOrWhiteSpace(severity) ? null : severity,
            Rule = string.IsNullOrWhiteSpace(rule) ? null : rule,
            Message = string.IsNullOrWhiteSpace(message) ? null : message,
            Line = line,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            Component = string.IsNullOrWhiteSpace(component) ? null : component,
            RelativePath = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath,
            AbsolutePath = string.IsNullOrWhiteSpace(absolutePath) ? null : absolutePath
        };
    }

    static string? NormalizeIssueType(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    static int? ParseIntNullable(object? value)
    {
        if (value is null)
            return null;

        if (value is int i)
            return i;

        if (value is long l &&
            l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}
