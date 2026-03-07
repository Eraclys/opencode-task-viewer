using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

internal interface IOrchestrationGateway
{
    OrchestrationConfigDto GetPublicConfig();
    Task<List<MappingRecord>> ListMappings();
    Task<MappingRecord> UpsertMapping(UpsertMappingRequest request);
    Task<InstructionProfileRecord?> GetInstructionProfile(int? mappingId, string? issueType);
    Task<InstructionProfileRecord> UpsertInstructionProfile(UpsertInstructionProfileRequest request);

    Task<IssuesListDto> ListIssues(
        int? mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        string? page,
        string? pageSize,
        string? ruleKeys);

    Task<RulesListDto> ListRules(int? mappingId, string? issueType, string? issueStatus);

    Task<EnqueueIssuesResultDto> EnqueueIssues(EnqueueIssuesRequest request);

    Task<EnqueueAllResultDto> EnqueueAllMatching(EnqueueAllRequest request);

    Task<List<QueueItemRecord>> ListQueue(string? states, string? limit);
    Task<QueueStatsDto> GetQueueStats();
    Task<OrchestrationWorkerStateDto> GetWorkerState();
    Task<bool> CancelQueueItem(int? queueId);
    Task<int> RetryFailed();
    Task<int> ClearQueued();
}
