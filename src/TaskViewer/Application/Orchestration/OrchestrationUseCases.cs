using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Application.Orchestration;

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

    public Task<List<MappingRecord>> ListMappingsAsync() => _gateway.ListMappings();

    public Task<bool> DeleteMappingAsync(int mappingId) => _gateway.DeleteMapping(mappingId);

    public Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request) => _gateway.UpsertMapping(request);

    public async Task<InstructionProfileDto> GetInstructionProfileAsync(int? mappingId, string? issueType)
    {
        var profile = await _gateway.GetInstructionProfile(mappingId, issueType);

        return new InstructionProfileDto
        {
            MappingId = mappingId,
            IssueType = string.IsNullOrWhiteSpace(issueType) ? null : issueType.ToUpperInvariant(),
            Instructions = profile?.Instructions
        };
    }

    public async Task<InstructionProfileDto> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request)
    {
        var profile = await _gateway.UpsertInstructionProfile(request);

        return new InstructionProfileDto
        {
            MappingId = profile.MappingId,
            IssueType = profile.IssueType,
            Instructions = profile.Instructions,
            UpdatedAt = profile.UpdatedAt
        };
    }

    public Task<IssuesListDto> ListIssuesAsync(
        int mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        int? page,
        int? pageSize,
        string? ruleKeys)
        => _gateway.ListIssues(
            mappingId,
            issueType,
            severity,
            issueStatus,
            page,
            pageSize,
            ruleKeys);

    public Task<RulesListDto> ListRulesAsync(int mappingId, string? issueType, string? issueStatus)
        => _gateway.ListRules(mappingId, issueType, issueStatus);

    public Task<EnqueueIssuesResultDto> EnqueueIssuesAsync(EnqueueIssuesRequest request) => _gateway.EnqueueIssues(request);

    public Task<EnqueueAllResultDto> EnqueueAllMatchingAsync(EnqueueAllRequest request) => _gateway.EnqueueAllMatching(request);

    public async Task<QueueOverviewDto> GetQueueAsync(string? states, int? limit)
    {
        var items = await _gateway.ListQueue(states, limit);
        var stats = await _gateway.GetQueueStats();
        var worker = await _gateway.GetWorkerState();

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

    public Task<bool> CancelQueueItemAsync(int queueId) => _gateway.CancelQueueItem(queueId);

    public Task<int> RetryFailedAsync() => _gateway.RetryFailed();

    public Task<int> ClearQueuedAsync() => _gateway.ClearQueued();

    public Task<bool> ApproveTaskAsync(int taskId) => _gateway.ApproveTask(taskId);

    public Task<bool> RejectTaskAsync(int taskId, string? reason) => _gateway.RejectTask(taskId, reason);

    public Task<bool> RequeueTaskAsync(int taskId, string? reason) => _gateway.RequeueTask(taskId, reason);

    public Task<bool> RepromptTaskAsync(int taskId, string instructions, string? reason)
        => _gateway.RepromptTask(taskId, instructions, reason);

    public Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistoryAsync(int taskId)
        => _gateway.GetTaskReviewHistory(taskId);

    public Task ResetStateAsync() => _gateway.ResetState();
}
