using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Application.Orchestration;

public interface IOrchestrationGateway
{
    OrchestrationConfigDto GetPublicConfig();
    Task<List<MappingRecord>> ListMappings();
    Task<bool> DeleteMapping(int mappingId);
    Task<MappingRecord> UpsertMapping(UpsertMappingRequest request);
    Task<InstructionProfileRecord?> GetInstructionProfile(int? mappingId, string? issueType);
    Task<InstructionProfileRecord> UpsertInstructionProfile(UpsertInstructionProfileRequest request);

    Task<IssuesListDto> ListIssues(
        int mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        int? page,
        int? pageSize,
        string? ruleKeys);

    Task<RulesListDto> ListRules(int mappingId, string? issueType, string? issueStatus);

    Task<EnqueueIssuesResultDto> EnqueueIssues(EnqueueIssuesRequest request);

    Task<EnqueueAllResultDto> EnqueueAllMatching(EnqueueAllRequest request);

    Task<List<QueueItemRecord>> ListQueue(string? states, int? limit);
    Task<QueueStatsDto> GetQueueStats();
    Task<OrchestrationWorkerStateDto> GetWorkerState();
    Task<bool> CancelQueueItem(int queueId);
    Task<int> RetryFailed();
    Task<int> ClearQueued();
    Task<bool> ApproveTask(int taskId);
    Task<bool> RejectTask(int taskId, string? reason);
    Task<bool> RequeueTask(int taskId, string? reason);
    Task<bool> RepromptTask(int taskId, string instructions, string? reason);
    Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistory(int taskId);
    Task ResetState();
}
