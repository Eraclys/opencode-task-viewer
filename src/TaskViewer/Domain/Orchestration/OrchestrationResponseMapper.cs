using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

static class OrchestrationResponseMapper
{
    public static RulesListDto BuildRulesList(MappingRecord mapping, SonarRulesSummary summary)
    {
        var rules = summary
            .Rules.Select(r => new RuleCountDto
            {
                Key = r.Key,
                Name = r.Name,
                Count = r.Count
            })
            .ToList();

        return new RulesListDto
        {
            Mapping = mapping,
            IssueType = summary.ParsedIssueType,
            IssueStatus = summary.ParsedIssueStatus,
            ScannedIssues = summary.ScannedIssues,
            Truncated = summary.Truncated,
            Rules = rules
        };
    }

    public static IssuesListDto BuildIssuesList(MappingRecord mapping, SonarIssuesPage result)
    {
        var issues = result
            .Issues.Select(i =>
                new IssueListItemDto
                {
                    Key = i.Key,
                    Type = i.Type,
                    Severity = i.Severity,
                    Rule = i.Rule,
                    Message = i.Message,
                    Component = i.Component,
                    Line = i.Line,
                    Status = i.Status,
                    RelativePath = i.RelativePath,
                    AbsolutePath = i.AbsolutePath
                })
            .ToList();

        return new IssuesListDto
        {
            Mapping = mapping,
            Paging = new IssuesPagingDto
            {
                PageIndex = result.PageIndex,
                PageSize = result.PageSize,
                Total = result.Total
            },
            Issues = issues
        };
    }

    public static QueueEnqueueSkipView BuildInvalidIssueSkip()
    {
        return new QueueEnqueueSkipView
        {
            IssueKey = null,
            Reason = "invalid-issue"
        };
    }

    public static QueueEnqueueSkipView BuildRepoSkip(QueueSkip skip)
    {
        return new QueueEnqueueSkipView
        {
            IssueKey = skip.IssueKey,
            Reason = skip.Reason
        };
    }

    public static EnqueueIssuesResultDto BuildEnqueueIssuesResult(IReadOnlyList<QueueItemRecord> createdItems, IReadOnlyList<QueueEnqueueSkipView> skipped)
    {
        return new EnqueueIssuesResultDto
        {
            Requested = createdItems.Count + skipped.Count,
            Created = createdItems.Count,
            Skipped = skipped,
            Items = createdItems
        };
    }

    public static EnqueueAllResultDto BuildEnqueueAllResult(
        int matched,
        bool truncated,
        IReadOnlyList<QueueItemRecord> createdItems,
        IReadOnlyList<QueueEnqueueSkipView> skipped)
    {
        return new EnqueueAllResultDto
        {
            Requested = matched,
            Matched = matched,
            Created = createdItems.Count,
            Skipped = skipped,
            Truncated = truncated,
            Items = createdItems
        };
    }

    public static QueueStatsDto BuildQueueStats(QueueStats stats)
    {
        return new QueueStatsDto
        {
            Queued = stats.Queued,
            Dispatching = stats.Dispatching,
            SessionCreated = stats.SessionCreated,
            Done = stats.Done,
            Failed = stats.Failed,
            Cancelled = stats.Cancelled,
            Leased = stats.Leased,
            Running = stats.Running,
            AwaitingReview = stats.AwaitingReview,
            Rejected = stats.Rejected
        };
    }

    public static OrchestrationWorkerStateDto BuildWorkerState(
        int inFlightLeases,
        int runningTasks,
        int maxActiveDispatches,
        int perProjectMaxActive,
        int leaseSeconds,
        bool pausedByWorking,
        int workingCount,
        int maxWorkingGlobal,
        int workingResumeBelow,
        DateTimeOffset? workingSampleAt)
    {
        return new OrchestrationWorkerStateDto
        {
            InFlightDispatches = inFlightLeases,
            RunningTasks = runningTasks,
            MaxActiveDispatches = maxActiveDispatches,
            PerProjectMaxActive = perProjectMaxActive,
            LeaseSeconds = leaseSeconds,
            PausedByWorking = pausedByWorking,
            WorkingCount = workingCount,
            MaxWorkingGlobal = maxWorkingGlobal,
            WorkingResumeBelow = workingResumeBelow,
            WorkingSampleAt = workingSampleAt
        };
    }
}
