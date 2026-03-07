using System.Text.Json.Nodes;
using TaskViewer.Server;

namespace TaskViewer.Server.Application.Orchestration;

public interface IOrchestrationUseCases
{
    object GetPublicConfig();
    Task<List<MappingRecord>> ListMappingsAsync();
    Task<MappingRecord> UpsertMappingAsync(JsonNode? payload);
    Task<object> GetInstructionProfileAsync(string? mappingId, string? issueType);
    Task<object> UpsertInstructionProfileAsync(JsonNode? payload);
    Task<object> ListIssuesAsync(string mappingId, string? issueType, string? severity, string? issueStatus, string? page, string? pageSize, string? ruleKeys);
    Task<object> ListRulesAsync(string mappingId, string? issueType, string? issueStatus);
    Task<object> EnqueueIssuesAsync(JsonNode? payload);
    Task<object> EnqueueAllMatchingAsync(JsonNode? payload);
    Task<object> GetQueueAsync(string? states, string? limit);
    Task<bool> CancelQueueItemAsync(string queueId);
    Task<int> RetryFailedAsync();
    Task<int> ClearQueuedAsync();
}
