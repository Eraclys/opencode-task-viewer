using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

public interface IOrchestrationUseCases
{
    OrchestrationConfigDto GetPublicConfig();
    Task<List<MappingRecord>> ListMappingsAsync();
    Task<bool> DeleteMappingAsync(string mappingId);
    Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request);
    Task<InstructionProfileDto> GetInstructionProfileAsync(string? mappingId, string? issueType);
    Task<InstructionProfileDto> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request);

    Task<IssuesListDto> ListIssuesAsync(
        string mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        string? page,
        string? pageSize,
        string? ruleKeys);

    Task<RulesListDto> ListRulesAsync(string mappingId, string? issueType, string? issueStatus);
    Task<EnqueueIssuesResultDto> EnqueueIssuesAsync(EnqueueIssuesRequest request);
    Task<EnqueueAllResultDto> EnqueueAllMatchingAsync(EnqueueAllRequest request);
    Task<QueueOverviewDto> GetQueueAsync(string? states, string? limit);
    Task<bool> CancelQueueItemAsync(string queueId);
    Task<int> RetryFailedAsync();
    Task<int> ClearQueuedAsync();
    Task<bool> ApproveTaskAsync(string taskId);
    Task<bool> RejectTaskAsync(string taskId, string? reason);
    Task<bool> RequeueTaskAsync(string taskId, string? reason);
    Task<bool> RepromptTaskAsync(string taskId, string instructions, string? reason);
    Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistoryAsync(string taskId);
    Task ResetStateAsync();
}
