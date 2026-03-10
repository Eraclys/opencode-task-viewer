using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class OrchestrationUseCases : IOrchestrationUseCases
{
    readonly IOrchestrationGateway _gateway;

    public OrchestrationUseCases(SonarOrchestrator orchestrator)
        : this((IOrchestrationGateway)orchestrator)
    {
    }

    public OrchestrationUseCases(IOrchestrationGateway gateway)
    {
        _gateway = gateway;
    }

    public OrchestrationConfigDto GetPublicConfig() => _gateway.GetPublicConfig();

    public Task<List<MappingRecord>> ListMappingsAsync(CancellationToken cancellationToken = default) => _gateway.ListMappings(cancellationToken);

    public Task<bool> DeleteMappingAsync(int mappingId, CancellationToken cancellationToken = default) => _gateway.DeleteMapping(mappingId, cancellationToken);

    public Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request, CancellationToken cancellationToken = default) => _gateway.UpsertMapping(request, cancellationToken);

    public async Task<InstructionProfileDto> GetInstructionProfileAsync(int? mappingId, SonarIssueType issueType, CancellationToken cancellationToken = default)
    {
        var profile = await _gateway.GetInstructionProfile(mappingId, issueType, cancellationToken);

        return new InstructionProfileDto
        {
            MappingId = mappingId,
            IssueType = profile?.ParsedIssueType ?? issueType,
            Instructions = profile?.Instructions
        };
    }

    public async Task<InstructionProfileDto> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request, CancellationToken cancellationToken = default)
    {
        var profile = await _gateway.UpsertInstructionProfile(request, cancellationToken);

        return new InstructionProfileDto
        {
            MappingId = profile.MappingId,
            IssueType = profile.ParsedIssueType,
            Instructions = profile.Instructions,
            UpdatedAt = profile.UpdatedAt
        };
    }

    public Task<IssuesListDto> ListIssuesAsync(
        int mappingId,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueSeverity> severities,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        int? page,
        int? pageSize,
        string? ruleKeys,
        CancellationToken cancellationToken = default)
        => _gateway.ListIssues(
            mappingId,
            issueTypes,
            severities,
            issueStatuses,
            page,
            pageSize,
            ruleKeys,
            cancellationToken);

    public Task<RulesListDto> ListRulesAsync(
        int mappingId,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        CancellationToken cancellationToken = default)
        => _gateway.ListRules(
            mappingId,
            issueTypes,
            issueStatuses,
            cancellationToken);

    public Task<EnqueueIssuesResultDto> EnqueueIssuesAsync(EnqueueIssuesRequest request, CancellationToken cancellationToken = default) => _gateway.EnqueueIssues(request, cancellationToken);

    public Task<EnqueueAllResultDto> EnqueueAllMatchingAsync(EnqueueAllRequest request, CancellationToken cancellationToken = default) => _gateway.EnqueueAllMatching(request, cancellationToken);

    public async Task<QueueOverviewDto> GetQueueAsync(IReadOnlyList<QueueState> states, int? limit, CancellationToken cancellationToken = default)
    {
        var items = await _gateway.ListQueue(states, limit, cancellationToken);
        var stats = await _gateway.GetQueueStats(cancellationToken);
        var worker = await _gateway.GetWorkerState(cancellationToken);

        return new QueueOverviewDto
        {
            Items = items,
            Stats = stats,
            Worker = worker,
            Review = new TaskReviewSummaryDto
            {
                AwaitingReview = stats.AwaitingReview ?? 0,
                Rejected = stats.Rejected ?? 0
            }
        };
    }

    public Task<bool> CancelQueueItemAsync(int queueId, CancellationToken cancellationToken = default) => _gateway.CancelQueueItem(queueId, cancellationToken);

    public Task<int> RetryFailedAsync(CancellationToken cancellationToken = default) => _gateway.RetryFailed(cancellationToken);

    public Task<int> ClearQueuedAsync(CancellationToken cancellationToken = default) => _gateway.ClearQueued(cancellationToken);

    public Task<bool> ApproveTaskAsync(int taskId, CancellationToken cancellationToken = default) => _gateway.ApproveTask(taskId, cancellationToken);

    public Task<bool> RejectTaskAsync(int taskId, string? reason, CancellationToken cancellationToken = default) => _gateway.RejectTask(taskId, reason, cancellationToken);

    public Task<bool> RequeueTaskAsync(int taskId, string? reason, CancellationToken cancellationToken = default) => _gateway.RequeueTask(taskId, reason, cancellationToken);

    public Task<bool> RepromptTaskAsync(
        int taskId,
        string instructions,
        string? reason,
        CancellationToken cancellationToken = default)
        => _gateway.RepromptTask(
            taskId,
            instructions,
            reason,
            cancellationToken);

    public Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistoryAsync(int taskId, CancellationToken cancellationToken = default)
        => _gateway.GetTaskReviewHistory(taskId, cancellationToken);

    public Task ResetStateAsync(CancellationToken cancellationToken = default) => _gateway.ResetState(cancellationToken);
}
