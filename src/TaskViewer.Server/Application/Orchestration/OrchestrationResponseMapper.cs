using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

static class OrchestrationResponseMapper
{
    public static object BuildRulesList(MappingRecord mapping, SonarRulesSummary summary)
    {
        var rules = summary
            .Rules.Select(r => new
            {
                key = r.Key,
                name = r.Name,
                count = r.Count
            })
            .ToList();

        return new
        {
            mapping,
            issueType = summary.IssueType,
            issueStatus = summary.IssueStatus,
            scannedIssues = summary.ScannedIssues,
            truncated = summary.Truncated,
            rules
        };
    }

    public static object BuildIssuesList(MappingRecord mapping, SonarIssuesPage result)
    {
        var issues = result
            .Issues.Select(i =>
                new
                {
                    key = i.Key,
                    type = i.Type,
                    severity = i.Severity,
                    rule = i.Rule,
                    message = i.Message,
                    component = i.Component,
                    line = i.Line,
                    status = i.Status,
                    relativePath = i.RelativePath,
                    absolutePath = i.AbsolutePath
                })
            .ToList();

        return new
        {
            mapping,
            paging = new
            {
                pageIndex = result.PageIndex,
                pageSize = result.PageSize,
                total = result.Total
            },
            issues
        };
    }

    public static QueueEnqueueSkipView BuildInvalidIssueSkip()
    {
        return new QueueEnqueueSkipView
        {
            issueKey = null,
            reason = "invalid-issue"
        };
    }

    public static QueueEnqueueSkipView BuildRepoSkip(QueueSkip skip)
    {
        return new QueueEnqueueSkipView
        {
            issueKey = skip.IssueKey,
            reason = skip.Reason
        };
    }

    public static object BuildEnqueueIssuesResult(IReadOnlyList<QueueItemRecord> createdItems, IReadOnlyList<QueueEnqueueSkipView> skipped)
    {
        return new
        {
            created = createdItems.Count,
            skipped,
            items = createdItems
        };
    }

    public static object BuildEnqueueAllResult(
        int matched,
        bool truncated,
        IReadOnlyList<QueueItemRecord> createdItems,
        IReadOnlyList<QueueEnqueueSkipView> skipped)
    {
        return new
        {
            matched,
            created = createdItems.Count,
            skipped,
            truncated,
            items = createdItems
        };
    }

    public static object BuildQueueStats(QueueStats stats)
    {
        return new
        {
            queued = stats.Queued,
            dispatching = stats.Dispatching,
            session_created = stats.SessionCreated,
            done = stats.Done,
            failed = stats.Failed,
            cancelled = stats.Cancelled
        };
    }

    public static object BuildWorkerState(
        int inFlightDispatches,
        int maxActiveDispatches,
        bool pausedByWorking,
        int workingCount,
        int maxWorkingGlobal,
        int workingResumeBelow,
        string? workingSampleAt)
    {
        return new
        {
            inFlightDispatches,
            maxActiveDispatches,
            pausedByWorking,
            workingCount,
            maxWorkingGlobal,
            workingResumeBelow,
            workingSampleAt
        };
    }
}

sealed class QueueEnqueueSkipView
{
    public string? issueKey { get; init; }
    public string reason { get; init; } = string.Empty;
}
