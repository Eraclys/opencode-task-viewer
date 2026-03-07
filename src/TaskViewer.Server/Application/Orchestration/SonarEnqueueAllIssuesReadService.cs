using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class SonarEnqueueAllIssuesReadService : ISonarEnqueueAllIssuesReadService
{
    readonly ISonarGateway _sonarGateway;

    public SonarEnqueueAllIssuesReadService(ISonarGateway sonarGateway)
    {
        _sonarGateway = sonarGateway;
    }

    public async Task<SonarEnqueueAllIssuesResult> CollectMatchingIssuesAsync(
        MappingRecord mapping,
        string? issueType,
        string? severity,
        string? issueStatus,
        IReadOnlyList<string> ruleKeys,
        int maxScanIssues)
    {
        var type = NormalizeUpper(issueType);
        var sev = NormalizeUpper(severity);
        var status = NormalizeUpper(issueStatus);

        const int pageSize = 500;
        var page = 1;
        int? total = null;
        var allIssues = new List<JsonNode?>();

        while (allIssues.Count < maxScanIssues)
        {
            var query = SonarIssuesQueryBuilder.Build(
                mapping,
                page,
                pageSize,
                type,
                sev,
                status,
                ruleKeys);

            var data = await _sonarGateway.Fetch("/api/issues/search", query);

            total ??= ParseIntNullable(data?["paging"]?["total"]?.ToString());
            var issuesRaw = data?["issues"] as JsonArray ?? [];

            foreach (var issue in issuesRaw)
            {
                if (allIssues.Count >= maxScanIssues)
                    break;

                allIssues.Add(issue);
            }

            var endReached = issuesRaw.Count < pageSize || (total.HasValue && page * pageSize >= total.Value) || allIssues.Count >= maxScanIssues;

            if (endReached)
                break;

            page += 1;
        }

        return new SonarEnqueueAllIssuesResult(
            allIssues,
            total ?? allIssues.Count,
            allIssues.Count >= maxScanIssues);
    }

    static string? NormalizeUpper(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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
