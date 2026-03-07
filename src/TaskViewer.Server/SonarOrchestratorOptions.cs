using System.Text.Json.Nodes;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server;

public sealed class SonarOrchestratorOptions
{
    public required string SonarUrl { get; init; }
    public required string SonarToken { get; init; }
    public required string DbPath { get; init; }
    public required int MaxActive { get; init; }
    public required int PollMs { get; init; }
    public required int MaxAttempts { get; init; }
    public required int MaxWorkingGlobal { get; init; }
    public required int WorkingResumeBelow { get; init; }
    public ISonarGateway? SonarGateway { get; init; }
    public ISonarRuleReadService? SonarRuleReadService { get; init; }
    public ISonarRulesReadService? SonarRulesReadService { get; init; }
    public ISonarIssuesReadService? SonarIssuesReadService { get; init; }
    public ISonarEnqueueAllIssuesReadService? SonarEnqueueAllIssuesReadService { get; init; }
    public IWorkingSessionsReadService? WorkingSessionsReadService { get; init; }
    public IQueueDispatchService? QueueDispatchService { get; init; }
    public IDispatchFailurePolicy? DispatchFailurePolicy { get; init; }
    public IQueueWorkerCoordinator? QueueWorkerCoordinator { get; init; }
    public IOrchestratorRuntime? OrchestratorRuntime { get; init; }
    public IWorkloadBackpressurePolicy? WorkloadBackpressurePolicy { get; init; }
    internal IOrchestrationStatusService? OrchestrationStatusService { get; init; }
    internal IWorkloadBackpressureStateService? WorkloadBackpressureStateService { get; init; }
    internal IOrchestrationInputNormalizer? OrchestrationInputNormalizer { get; init; }
    internal IOrchestrationMappingService? OrchestrationMappingService { get; init; }
    internal IQueueEnqueueService? QueueEnqueueService { get; init; }
    internal IQueueCommandsService? QueueCommandsService { get; init; }
    internal IQueueQueryService? QueueQueryService { get; init; }
    internal IQueueDispatchOrchestrationService? QueueDispatchOrchestrationService { get; init; }
    public required Func<string, OpenCodeRequest, Task<JsonNode?>> OpenCodeFetch { get; init; }
    public required Func<string?, string?> NormalizeDirectory { get; init; }
    public required Func<string, string?, string?> BuildOpenCodeSessionUrl { get; init; }
    public required Action OnChange { get; init; }
}
