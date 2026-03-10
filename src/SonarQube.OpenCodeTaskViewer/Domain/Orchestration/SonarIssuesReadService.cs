using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class SonarIssuesReadService : ISonarIssuesReadService
{
    readonly ISonarQubeService _sonarQubeService;

    public SonarIssuesReadService(ISonarQubeService sonarQubeService)
    {
        _sonarQubeService = sonarQubeService;
    }

    public async Task<SonarIssuesPage> ListIssuesAsync(
        MappingRecord mapping,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueSeverity> severities,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        int page,
        int pageSize,
        IReadOnlyList<string> ruleKeys,
        CancellationToken cancellationToken = default)
    {
        var response = await _sonarQubeService.SearchIssuesAsync(
            new SearchIssuesQuery
            {
                ComponentKey = mapping.SonarProjectKey,
                Branch = mapping.Branch,
                PageIndex = page,
                PageSize = pageSize,
                Types = issueTypes,
                Severities = severities,
                Statuses = issueStatuses,
                RuleKeys = ruleKeys
            },
            cancellationToken);

        var issues = new List<SonarIssueSummaryItem>();

        foreach (var raw in response.Issues)
        {
            var issue = SonarIssueNormalizer.NormalizeForQueue(raw, mapping);

            if (issue is null)
                continue;

            issues.Add(
                new SonarIssueSummaryItem(
                    issue.Key,
                    issue.IssueType,
                    issue.IssueSeverity,
                    issue.Rule,
                    issue.Message,
                    issue.Component,
                    issue.Line,
                    issue.IssueStatus,
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
