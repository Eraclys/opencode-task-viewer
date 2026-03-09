using TaskViewer.SonarQube;

namespace TaskViewer.Application.Orchestration;

public sealed class SonarRulesReadService : ISonarRulesReadService
{
    readonly ISonarRuleReadService _ruleReadService;
    readonly ISonarQubeService _sonarQubeService;

    public SonarRulesReadService(ISonarQubeService sonarQubeService, ISonarRuleReadService ruleReadService)
    {
        _sonarQubeService = sonarQubeService;
        _ruleReadService = ruleReadService;
    }

    public async Task<SonarRulesSummary> SummarizeRulesAsync(
        MappingRecord mapping,
        string? issueType,
        string? issueStatus,
        int maxScanIssues)
    {
        var type = NormalizeUpper(issueType);
        var status = NormalizeUpper(issueStatus) ?? string.Empty;
        var types = string.IsNullOrWhiteSpace(type) ? Array.Empty<string>() : new[] { type };
        var statuses = string.IsNullOrWhiteSpace(status) ? Array.Empty<string>() : new[] { status };

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        const int pageSize = 500;
        var page = 1;
        var scanned = 0;
        int? total = null;

        while (true)
        {
            var response = await _sonarQubeService.SearchIssuesAsync(new SearchIssuesQuery
            {
                ComponentKey = mapping.SonarProjectKey,
                Branch = mapping.Branch,
                PageIndex = page,
                PageSize = pageSize,
                Types = types,
                Statuses = statuses
            });

            var issues = response.Issues;
            total ??= response.Total;

            foreach (var issueNode in issues)
            {
                var key = SonarResponseParsers.ParseIssueRuleKey(issueNode);

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                counts.TryGetValue(key, out var current);
                counts[key] = current + 1;
                scanned += 1;
            }

            var endReached = issues.Count < pageSize || (total.HasValue && page * pageSize >= total.Value) || scanned >= maxScanIssues;

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
            type,
            string.IsNullOrWhiteSpace(status) ? null : status,
            scanned,
            scanned >= maxScanIssues,
            rules);
    }

    static string? NormalizeUpper(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
