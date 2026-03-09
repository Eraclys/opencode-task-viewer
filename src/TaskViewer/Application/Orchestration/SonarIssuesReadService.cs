using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.SonarQube;

namespace TaskViewer.Application.Orchestration;

public sealed class SonarIssuesReadService : ISonarIssuesReadService
{
    readonly ISonarQubeService _sonarQubeService;

    public SonarIssuesReadService(ISonarQubeService sonarQubeService)
    {
        _sonarQubeService = sonarQubeService;
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
        var types = string.IsNullOrWhiteSpace(type) ? Array.Empty<string>() : new[] { type };
        var severities = string.IsNullOrWhiteSpace(sev) ? Array.Empty<string>() : new[] { sev };
        var statuses = string.IsNullOrWhiteSpace(status) ? Array.Empty<string>() : new[] { status };

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
        });

        var issues = new List<SonarIssueSummaryItem>();

        foreach (var raw in response.Issues)
        {
            var issue = SonarIssueNormalizer.NormalizeForQueue(raw, mapping);

            if (issue is null)
                continue;

            issues.Add(new SonarIssueSummaryItem(
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

        var total = response.Total ?? issues.Count;

        return new SonarIssuesPage(
            response.PageIndex,
            response.PageSize,
            total,
            issues);
    }

    static string? NormalizeUpper(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
