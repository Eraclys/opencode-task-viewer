using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;

public static class SonarIssueNormalizer
{
    public static NormalizedIssue? NormalizeForQueue(SonarIssueTransport? rawIssue, MappingRecord mapping)
    {
        if (rawIssue is null)
            return null;

        var key = rawIssue.Key ?? rawIssue.IssueKey ?? string.Empty;

        if (string.IsNullOrWhiteSpace(key))
            return null;

        var type = SonarIssueType.FromRaw(rawIssue.Type ?? rawIssue.IssueType ?? SonarIssueType.CodeSmell.Value);

        if (!type.HasValue)
            type = SonarIssueType.CodeSmell;

        var severity = SonarIssueSeverity.FromRaw(rawIssue.Severity);
        var rule = rawIssue.Rule?.Trim();
        var message = rawIssue.Message?.Trim();
        var line = ParseIntNullable(rawIssue.Line);
        var status = SonarIssueStatus.FromRaw(rawIssue.Status);
        var component = rawIssue.Component?.Trim() ?? rawIssue.File?.Trim();

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
            Type = type.Value,
            Severity = severity.OrNull(),
            Rule = string.IsNullOrWhiteSpace(rule) ? null : rule,
            Message = string.IsNullOrWhiteSpace(message) ? null : message,
            Line = line,
            Status = status.OrNull(),
            Component = string.IsNullOrWhiteSpace(component) ? null : component,
            RelativePath = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath,
            AbsolutePath = string.IsNullOrWhiteSpace(absolutePath) ? null : absolutePath
        };
    }

    static int? ParseIntNullable(string? value) => int.TryParse(value, out var parsed) ? parsed : null;
}
