using System.Globalization;

namespace TaskViewer.Server.Application.Orchestration;

public static class SonarIssuesQueryBuilder
{
    public static Dictionary<string, string?> Build(
        MappingRecord mapping,
        int page,
        int pageSize,
        string? issueType,
        string? severity,
        string? issueStatus,
        IReadOnlyList<string>? ruleKeys)
    {
        var query = new Dictionary<string, string?>
        {
            ["componentKeys"] = mapping.SonarProjectKey,
            ["p"] = page.ToString(CultureInfo.InvariantCulture),
            ["ps"] = pageSize.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(issueType))
            query["types"] = issueType;

        if (!string.IsNullOrWhiteSpace(severity))
            query["severities"] = severity;

        if (!string.IsNullOrWhiteSpace(issueStatus))
            query["statuses"] = issueStatus;

        if (ruleKeys is { Count: > 0 })
            query["rules"] = string.Join(',', ruleKeys);

        if (!string.IsNullOrWhiteSpace(mapping.Branch))
            query["branch"] = mapping.Branch;

        return query;
    }
}
