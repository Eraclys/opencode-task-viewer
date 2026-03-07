using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class OrchestrationUseCases : IOrchestrationUseCases
{
    readonly IOrchestrationGateway _gateway;

    public OrchestrationUseCases(IOrchestrationGateway gateway)
    {
        _gateway = gateway;
    }

    public object GetPublicConfig() => _gateway.GetPublicConfig();

    public Task<List<MappingRecord>> ListMappingsAsync() => _gateway.ListMappings();

    public Task<MappingRecord> UpsertMappingAsync(JsonNode? payload) => _gateway.UpsertMapping(payload);

    public async Task<object> GetInstructionProfileAsync(string? mappingId, string? issueType)
    {
        var profile = await _gateway.GetInstructionProfile(mappingId, issueType);

        return new
        {
            mappingId = int.TryParse(mappingId, out var parsed) ? parsed : (int?)null,
            issueType = string.IsNullOrWhiteSpace(issueType) ? null : issueType.ToUpperInvariant(),
            instructions = profile?["instructions"]?.ToString()
        };
    }

    public async Task<object> UpsertInstructionProfileAsync(JsonNode? payload)
    {
        var profile = await _gateway.UpsertInstructionProfile(
            payload?["mappingId"]?.ToString(),
            payload?["issueType"]?.ToString(),
            payload?["instructions"]?.ToString());

        return new
        {
            mappingId = profile["mapping_id"]?.GetValue<int>(),
            issueType = profile["issue_type"]?.ToString(),
            instructions = profile["instructions"]?.ToString(),
            updatedAt = profile["updated_at"]?.ToString()
        };
    }

    public Task<object> ListIssuesAsync(
        string mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        string? page,
        string? pageSize,
        string? ruleKeys) => _gateway.ListIssues(
        mappingId,
        issueType,
        severity,
        issueStatus,
        page,
        pageSize,
        ruleKeys);

    public Task<object> ListRulesAsync(string mappingId, string? issueType, string? issueStatus) => _gateway.ListRules(mappingId, issueType, issueStatus);

    public Task<object> EnqueueIssuesAsync(JsonNode? payload)
    {
        return _gateway.EnqueueIssues(
            payload?["mappingId"]?.ToString(),
            payload?["issueType"]?.ToString(),
            payload?["instructions"]?.ToString(),
            payload?["issues"] as JsonArray);
    }

    public Task<object> EnqueueAllMatchingAsync(JsonNode? payload)
    {
        var ruleKeys = payload?["ruleKeys"] ?? payload?["rules"] ?? payload?["rule"];

        return _gateway.EnqueueAllMatching(
            payload?["mappingId"]?.ToString(),
            payload?["issueType"]?.ToString(),
            ruleKeys,
            payload?["issueStatus"]?.ToString(),
            payload?["severity"]?.ToString(),
            payload?["instructions"]?.ToString());
    }

    public async Task<object> GetQueueAsync(string? states, string? limit)
    {
        var items = await _gateway.ListQueue(states, limit);
        var stats = await _gateway.GetQueueStats();
        var worker = await _gateway.GetWorkerState();

        return new
        {
            items,
            stats,
            worker
        };
    }

    public Task<bool> CancelQueueItemAsync(string queueId) => _gateway.CancelQueueItem(queueId);

    public Task<int> RetryFailedAsync() => _gateway.RetryFailed();

    public Task<int> ClearQueuedAsync() => _gateway.ClearQueued();
}
