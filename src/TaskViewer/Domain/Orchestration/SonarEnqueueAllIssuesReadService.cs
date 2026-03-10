using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.Persistence;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

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
        int maxScanIssues,
        CancellationToken cancellationToken = default)
    {
        var types = SonarIssueType.ParseCsv(issueType);
        var severities = SonarIssueSeverity.ParseCsv(severity);
        var statuses = SonarIssueStatus.ParseCsv(issueStatus);

        const int pageSize = 500;
        var page = 1;
        int? total = null;
        var allIssues = new List<NormalizedIssue>();

        while (allIssues.Count < maxScanIssues)
        {
            var response = await _sonarQubeService.SearchIssuesAsync(new SearchIssuesQuery
            {
                ComponentKey = mapping.SonarProjectKey,
                Branch = mapping.Branch,
                PageIndex = page,
                PageSize = pageSize,
                Types = types,
                Severities = severities,
                Statuses = statuses,
                RuleKeys =  ruleKeys
            }, cancellationToken);

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

}
