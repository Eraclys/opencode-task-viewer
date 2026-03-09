using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.SonarQube;

namespace TaskViewer.Application.Orchestration;

public sealed class SonarEnqueueAllIssuesReadService : ISonarEnqueueAllIssuesReadService
{
    readonly ISonarQubeService _sonarQubeService;

    public SonarEnqueueAllIssuesReadService(ISonarQubeService sonarQubeService)
    {
        _sonarQubeService = sonarQubeService;
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
        var allIssues = new List<NormalizedIssue>();

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

            var response = await _sonarQubeService.SearchIssuesAsync(query, page, pageSize);
            total ??= response.Total;
            var issuesRaw = response.Issues;

            foreach (var issue in issuesRaw)
            {
                if (allIssues.Count >= maxScanIssues)
                    break;

                var normalized = SonarIssueNormalizer.NormalizeForQueue(issue, mapping);

                if (normalized is null)
                    continue;

                allIssues.Add(normalized);
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
}
