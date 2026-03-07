using System.Text.Json.Nodes;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed class OrchestrationGatewayAdapter : IOrchestrationGateway
{
    readonly SonarOrchestrator _orchestrator;

    public OrchestrationGatewayAdapter(SonarOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public object GetPublicConfig() => _orchestrator.GetPublicConfig();

    public Task<List<MappingRecord>> ListMappings() => _orchestrator.ListMappings();

    public Task<MappingRecord> UpsertMapping(JsonNode? payload) => _orchestrator.UpsertMapping(payload);

    public Task<JsonObject?> GetInstructionProfile(object? mappingId, string? issueType) => _orchestrator.GetInstructionProfile(mappingId, issueType);

    public Task<JsonObject> UpsertInstructionProfile(object? mappingId, string? issueType, string? instructions) => _orchestrator.UpsertInstructionProfile(mappingId, issueType, instructions);

    public Task<object> ListIssues(
        object? mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        object? page,
        object? pageSize,
        object? ruleKeys)
        => _orchestrator.ListIssues(
            mappingId,
            issueType,
            severity,
            issueStatus,
            page,
            pageSize,
            ruleKeys);

    public Task<object> ListRules(object? mappingId, string? issueType, string? issueStatus) => _orchestrator.ListRules(mappingId, issueType, issueStatus);

    public Task<object> EnqueueIssues(
        object? mappingId,
        string? issueType,
        string? instructions,
        JsonArray? issues)
        => _orchestrator.EnqueueIssues(
            mappingId,
            issueType,
            instructions,
            issues);

    public Task<object> EnqueueAllMatching(
        object? mappingId,
        string? issueType,
        object? ruleKeys,
        string? issueStatus,
        string? severity,
        string? instructions)
        => _orchestrator.EnqueueAllMatching(
            mappingId,
            issueType,
            ruleKeys,
            issueStatus,
            severity,
            instructions);

    public Task<List<QueueItemRecord>> ListQueue(object? states, object? limit) => _orchestrator.ListQueue(states, limit);

    public Task<object> GetQueueStats() => _orchestrator.GetQueueStats();

    public Task<object> GetWorkerState() => _orchestrator.GetWorkerState();

    public Task<bool> CancelQueueItem(object? queueId) => _orchestrator.CancelQueueItem(queueId);

    public Task<int> RetryFailed() => _orchestrator.RetryFailed();

    public Task<int> ClearQueued() => _orchestrator.ClearQueued();
}
