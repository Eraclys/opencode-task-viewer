using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.Persistence;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

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
        IReadOnlyList<string> ruleKeys,
        CancellationToken cancellationToken = default)
    {
        var types = SonarIssueType.ParseCsv(issueType);
        var severities = SonarIssueSeverity.ParseCsv(severity);
        var statuses = SonarIssueStatus.ParseCsv(issueStatus);

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

}
