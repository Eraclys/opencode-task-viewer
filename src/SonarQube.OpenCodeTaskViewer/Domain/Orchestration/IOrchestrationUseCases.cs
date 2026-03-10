using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface IOrchestrationUseCases
{
    OrchestrationConfigDto GetPublicConfig();
    Task<List<MappingRecord>> ListMappingsAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteMappingAsync(int mappingId, CancellationToken cancellationToken = default);
    Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request, CancellationToken cancellationToken = default);
    Task<InstructionProfileDto> GetInstructionProfileAsync(int? mappingId, SonarIssueType issueType, CancellationToken cancellationToken = default);
    Task<InstructionProfileDto> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request, CancellationToken cancellationToken = default);

    Task<IssuesListDto> ListIssuesAsync(
        int mappingId,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueSeverity> severities,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        int? page,
        int? pageSize,
        string? ruleKeys,
        CancellationToken cancellationToken = default);

    Task<RulesListDto> ListRulesAsync(
        int mappingId,
        IReadOnlyList<SonarIssueType> issueTypes,
        IReadOnlyList<SonarIssueStatus> issueStatuses,
        CancellationToken cancellationToken = default);

    Task<EnqueueIssuesResultDto> EnqueueIssuesAsync(EnqueueIssuesRequest request, CancellationToken cancellationToken = default);
    Task<EnqueueAllResultDto> EnqueueAllMatchingAsync(EnqueueAllRequest request, CancellationToken cancellationToken = default);
    Task<QueueOverviewDto> GetQueueAsync(IReadOnlyList<QueueState> states, int? limit, CancellationToken cancellationToken = default);
    Task<bool> CancelQueueItemAsync(int queueId, CancellationToken cancellationToken = default);
    Task<int> RetryFailedAsync(CancellationToken cancellationToken = default);
    Task<int> ClearQueuedAsync(CancellationToken cancellationToken = default);
    Task<bool> ApproveTaskAsync(int taskId, CancellationToken cancellationToken = default);
    Task<bool> RejectTaskAsync(int taskId, string? reason, CancellationToken cancellationToken = default);
    Task<bool> RequeueTaskAsync(int taskId, string? reason, CancellationToken cancellationToken = default);

    Task<bool> RepromptTaskAsync(
        int taskId,
        string instructions,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistoryAsync(int taskId, CancellationToken cancellationToken = default);
    Task ResetStateAsync(CancellationToken cancellationToken = default);
}
