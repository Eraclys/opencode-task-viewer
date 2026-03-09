using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public interface IOrchestrationGateway
{
    OrchestrationConfigDto GetPublicConfig();
    Task<List<MappingRecord>> ListMappings(CancellationToken cancellationToken = default);
    Task<bool> DeleteMapping(int mappingId, CancellationToken cancellationToken = default);
    Task<MappingRecord> UpsertMapping(UpsertMappingRequest request, CancellationToken cancellationToken = default);
    Task<InstructionProfileRecord?> GetInstructionProfile(int? mappingId, string? issueType, CancellationToken cancellationToken = default);
    Task<InstructionProfileRecord> UpsertInstructionProfile(UpsertInstructionProfileRequest request, CancellationToken cancellationToken = default);

    Task<IssuesListDto> ListIssues(
        int mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        int? page,
        int? pageSize,
        string? ruleKeys,
        CancellationToken cancellationToken = default);

    Task<RulesListDto> ListRules(int mappingId, string? issueType, string? issueStatus, CancellationToken cancellationToken = default);

    Task<EnqueueIssuesResultDto> EnqueueIssues(EnqueueIssuesRequest request, CancellationToken cancellationToken = default);

    Task<EnqueueAllResultDto> EnqueueAllMatching(EnqueueAllRequest request, CancellationToken cancellationToken = default);

    Task<List<QueueItemRecord>> ListQueue(string? states, int? limit, CancellationToken cancellationToken = default);
    Task<QueueStatsDto> GetQueueStats(CancellationToken cancellationToken = default);
    Task<OrchestrationWorkerStateDto> GetWorkerState(CancellationToken cancellationToken = default);
    Task<bool> CancelQueueItem(int queueId, CancellationToken cancellationToken = default);
    Task<int> RetryFailed(CancellationToken cancellationToken = default);
    Task<int> ClearQueued(CancellationToken cancellationToken = default);
    Task<bool> ApproveTask(int taskId, CancellationToken cancellationToken = default);
    Task<bool> RejectTask(int taskId, string? reason, CancellationToken cancellationToken = default);
    Task<bool> RequeueTask(int taskId, string? reason, CancellationToken cancellationToken = default);
    Task<bool> RepromptTask(int taskId, string instructions, string? reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistory(int taskId, CancellationToken cancellationToken = default);
    Task ResetState(CancellationToken cancellationToken = default);
}
