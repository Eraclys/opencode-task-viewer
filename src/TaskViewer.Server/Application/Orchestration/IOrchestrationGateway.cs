using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application.Orchestration;

public interface IOrchestrationGateway
{
    object GetPublicConfig();
    Task<List<MappingRecord>> ListMappings();
    Task<MappingRecord> UpsertMapping(JsonNode? payload);
    Task<JsonObject?> GetInstructionProfile(object? mappingId, string? issueType);
    Task<JsonObject> UpsertInstructionProfile(object? mappingId, string? issueType, string? instructions);

    Task<object> ListIssues(
        object? mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        object? page,
        object? pageSize,
        object? ruleKeys);

    Task<object> ListRules(object? mappingId, string? issueType, string? issueStatus);

    Task<object> EnqueueIssues(
        object? mappingId,
        string? issueType,
        string? instructions,
        JsonArray? issues);

    Task<object> EnqueueAllMatching(
        object? mappingId,
        string? issueType,
        object? ruleKeys,
        string? issueStatus,
        string? severity,
        string? instructions);

    Task<List<QueueItemRecord>> ListQueue(object? states, object? limit);
    Task<object> GetQueueStats();
    Task<object> GetWorkerState();
    Task<bool> CancelQueueItem(object? queueId);
    Task<int> RetryFailed();
    Task<int> ClearQueued();
}
