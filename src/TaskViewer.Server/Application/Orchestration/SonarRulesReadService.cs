using System.Text.Json.Nodes;
using TaskViewer.Server;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class SonarRulesReadService : ISonarRulesReadService
{
    private readonly ISonarGateway _sonarGateway;
    private readonly ISonarRuleReadService _ruleReadService;

    public SonarRulesReadService(ISonarGateway sonarGateway, ISonarRuleReadService ruleReadService)
    {
        _sonarGateway = sonarGateway;
        _ruleReadService = ruleReadService;
    }

    public async Task<SonarRulesSummary> SummarizeRulesAsync(MappingRecord mapping, string? issueType, string? issueStatus, int maxScanIssues)
    {
        var normalizedType = NormalizeUpper(issueType);
        var normalizedStatus = NormalizeUpper(issueStatus) ?? string.Empty;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        const int pageSize = 500;
        var page = 1;
        var scanned = 0;
        int? total = null;

        while (true)
        {
            var query = SonarIssuesQueryBuilder.Build(
                mapping,
                page,
                pageSize,
                normalizedType,
                severity: null,
                issueStatus: normalizedStatus,
                ruleKeys: null);

            var data = await _sonarGateway.Fetch("/api/issues/search", query);
            var issues = data?["issues"] as JsonArray ?? [];
            total ??= ParseIntNullable(data?["paging"]?["total"]?.ToString());

            foreach (var issueNode in issues)
            {
                var key = issueNode?["rule"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                counts.TryGetValue(key, out var current);
                counts[key] = current + 1;
                scanned += 1;
            }

            var endReached = issues.Count < pageSize
                             || (total.HasValue && page * pageSize >= total.Value)
                             || scanned >= maxScanIssues;

            if (endReached)
                break;

            page += 1;
        }

        var rules = new List<SonarRuleSummaryItem>(counts.Count);
        foreach (var key in counts.Keys)
        {
            var name = await _ruleReadService.GetRuleDisplayName(key);
            rules.Add(new SonarRuleSummaryItem(key, string.IsNullOrWhiteSpace(name) ? key : name, counts[key]));
        }

        rules = rules
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SonarRulesSummary(
            IssueType: normalizedType,
            IssueStatus: string.IsNullOrWhiteSpace(normalizedStatus) ? null : normalizedStatus,
            ScannedIssues: scanned,
            Truncated: scanned >= maxScanIssues,
            Rules: rules);
    }

    private static string? NormalizeUpper(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static int? ParseIntNullable(object? value)
    {
        if (value is null)
            return null;

        if (value is int i)
            return i;

        if (value is long l && l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}
