using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class OrchestrationUseCases : IOrchestrationUseCases
{
    readonly IOrchestrationGateway _gateway;

    internal OrchestrationUseCases(SonarOrchestrator orchestrator)
        : this((IOrchestrationGateway)orchestrator)
    {
    }

    internal OrchestrationUseCases(IOrchestrationGateway gateway)
    {
        _gateway = gateway;
    }

    public OrchestrationConfigDto GetPublicConfig() => _gateway.GetPublicConfig();

    public Task<List<MappingRecord>> ListMappingsAsync() => _gateway.ListMappings();

    public Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request) => _gateway.UpsertMapping(request);

    public async Task<InstructionProfileDto> GetInstructionProfileAsync(string? mappingId, string? issueType)
    {
        var parsedMappingId = int.TryParse(mappingId, out var parsed) ? parsed : (int?)null;
        var profile = await _gateway.GetInstructionProfile(parsedMappingId, issueType);

        return new InstructionProfileDto
        {
            MappingId = parsedMappingId,
            IssueType = string.IsNullOrWhiteSpace(issueType) ? null : issueType.ToUpperInvariant(),
            Instructions = profile?.Instructions
        };
    }

    public async Task<InstructionProfileDto> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request)
    {
        var profile = await _gateway.UpsertInstructionProfile(request);

        return new InstructionProfileDto
        {
            MappingId = profile.MappingId,
            IssueType = profile.IssueType,
            Instructions = profile.Instructions,
            UpdatedAt = profile.UpdatedAt
        };
    }

    public Task<IssuesListDto> ListIssuesAsync(
        string mappingId,
        string? issueType,
        string? severity,
        string? issueStatus,
        string? page,
        string? pageSize,
        string? ruleKeys)
    {
        var parsedMappingId = int.TryParse(mappingId, out var id) ? id : (int?)null;

        return _gateway.ListIssues(
            parsedMappingId,
            issueType,
            severity,
            issueStatus,
            page,
            pageSize,
            ruleKeys);
    }

    public Task<RulesListDto> ListRulesAsync(string mappingId, string? issueType, string? issueStatus)
    {
        var parsedMappingId = int.TryParse(mappingId, out var id) ? id : (int?)null;
        return _gateway.ListRules(parsedMappingId, issueType, issueStatus);
    }

    public Task<EnqueueIssuesResultDto> EnqueueIssuesAsync(EnqueueIssuesRequest request) => _gateway.EnqueueIssues(request);

    public Task<EnqueueAllResultDto> EnqueueAllMatchingAsync(EnqueueAllRequest request) => _gateway.EnqueueAllMatching(request);

    public async Task<QueueOverviewDto> GetQueueAsync(string? states, string? limit)
    {
        var items = await _gateway.ListQueue(states, limit);
        var stats = await _gateway.GetQueueStats();
        var worker = await _gateway.GetWorkerState();

        return new QueueOverviewDto
        {
            Items = items,
            Stats = stats,
            Worker = worker
        };
    }

    public Task<bool> CancelQueueItemAsync(string queueId)
    {
        var parsedQueueId = int.TryParse(queueId, out var id) ? id : (int?)null;
        return _gateway.CancelQueueItem(parsedQueueId);
    }

    public Task<int> RetryFailedAsync() => _gateway.RetryFailed();

    public Task<int> ClearQueuedAsync() => _gateway.ClearQueued();
}
