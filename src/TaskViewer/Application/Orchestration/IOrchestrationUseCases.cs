using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Application.Orchestration;

public interface IOrchestrationUseCases
{
    OrchestrationConfigDto GetPublicConfig();
    Task<List<MappingRecord>> ListMappingsAsync();
    Task<bool> DeleteMappingAsync(int mappingId);
    Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request);
    Task<InstructionProfileDto> GetInstructionProfileAsync(int? mappingId, string? issueType);
    Task<InstructionProfileDto> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request);

    Task<IssuesListDto> ListIssuesAsync(
        int mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        int? page,
        int? pageSize,
        string? ruleKeys);

    Task<RulesListDto> ListRulesAsync(int mappingId, string? issueType, string? issueStatus);
    Task<EnqueueIssuesResultDto> EnqueueIssuesAsync(EnqueueIssuesRequest request);
    Task<EnqueueAllResultDto> EnqueueAllMatchingAsync(EnqueueAllRequest request);
    Task<QueueOverviewDto> GetQueueAsync(string? states, int? limit);
    Task<bool> CancelQueueItemAsync(int queueId);
    Task<int> RetryFailedAsync();
    Task<int> ClearQueuedAsync();
    Task<bool> ApproveTaskAsync(int taskId);
    Task<bool> RejectTaskAsync(int taskId, string? reason);
    Task<bool> RequeueTaskAsync(int taskId, string? reason);
    Task<bool> RepromptTaskAsync(int taskId, string instructions, string? reason);
    Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistoryAsync(int taskId);
    Task ResetStateAsync();
}
