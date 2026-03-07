using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class SonarIssuesReadService : ISonarIssuesReadService
{
    readonly ISonarGateway _sonarGateway;

    public SonarIssuesReadService(ISonarGateway sonarGateway)
    {
        _sonarGateway = sonarGateway;
    }

    public async Task<SonarIssuesPage> ListIssuesAsync(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        int page,
        int pageSize,
        IReadOnlyList<string> ruleKeys)
    {
        var type = NormalizeUpper(issueType);
        var sev = NormalizeUpper(severity);
        var status = NormalizeUpper(issueStatus);

        var query = SonarIssuesQueryBuilder.Build(
            mapping,
            page,
            pageSize,
            type,
            sev,
            status,
            ruleKeys);

        var data = await _sonarGateway.Fetch("/api/issues/search", query);
        var rawIssues = data?["issues"] as JsonArray ?? [];
        var issues = new List<SonarIssueSummaryItem>();

        foreach (var raw in rawIssues)
        {
            var issue = SonarIssueNormalizer.NormalizeForQueue(raw, mapping);

            if (issue is null)
                continue;

            issues.Add(
                new SonarIssueSummaryItem(
                    issue.Key,
                    issue.Type,
                    issue.Severity,
                    issue.Rule,
                    issue.Message,
                    issue.Component,
                    issue.Line,
                    issue.Status,
                    issue.RelativePath,
                    issue.AbsolutePath));
        }

        var pageIndex = ParseIntSafe(data?["paging"]?["pageIndex"]?.ToString(), page);
        var parsedPageSize = ParseIntSafe(data?["paging"]?["pageSize"]?.ToString(), pageSize);
        var total = ParseIntSafe(data?["paging"]?["total"]?.ToString(), issues.Count);

        return new SonarIssuesPage(
            pageIndex,
            parsedPageSize,
            total,
            issues);
    }

    static string? NormalizeUpper(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    static int ParseIntSafe(object? value, int fallback)
    {
        if (value is null)
            return fallback;

        if (value is int i)
            return i;

        if (value is long l &&
            l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }
}
