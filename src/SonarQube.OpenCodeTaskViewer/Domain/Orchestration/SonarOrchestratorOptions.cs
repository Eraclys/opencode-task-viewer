using OpenCode.Client;
using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class SonarOrchestratorOptions
{
    public required string SonarUrl { get; init; }
    public required string SonarToken { get; init; }
    public required string DbPath { get; init; }
    public required int MaxActive { get; init; }
    public required int PerProjectMaxActive { get; init; }
    public required int PollMs { get; init; }
    public required int LeaseSeconds { get; init; }
    public required int MaxAttempts { get; init; }
    public required int MaxWorkingGlobal { get; init; }
    public required int WorkingResumeBelow { get; init; }
    public IOpenCodeService? OpenCodeApiClient { get; init; }
    public ISonarQubeService? SonarQubeService { get; init; }
    public ISonarRuleReadService? SonarRuleReadService { get; init; }
    public ISonarRulesReadService? SonarRulesReadService { get; init; }
    public ISonarIssuesReadService? SonarIssuesReadService { get; init; }
    public ISonarEnqueueAllIssuesReadService? SonarEnqueueAllIssuesReadService { get; init; }
    public IWorkingSessionsReadService? WorkingSessionsReadService { get; init; }
    public IQueueDispatchService? QueueDispatchService { get; init; }
    public IDispatchFailurePolicy? DispatchFailurePolicy { get; init; }
    public IWorkloadBackpressurePolicy? WorkloadBackpressurePolicy { get; init; }
    public IOrchestrationStatusService? OrchestrationStatusService { get; init; }
    public IWorkloadBackpressureStateService? WorkloadBackpressureStateService { get; init; }
    public IOrchestrationInputNormalizer? OrchestrationInputNormalizer { get; init; }
    public IOrchestrationMappingService? OrchestrationMappingService { get; init; }
    public ITaskSchedulerService? TaskSchedulerService { get; init; }
    public ITaskReconcilerService? TaskReconcilerService { get; init; }
    public ITaskReadinessGate? TaskReadinessGate { get; init; }
    public IOrchestrationPersistence? Persistence { get; init; }
    public required Func<string?, string?> NormalizeDirectory { get; init; }
    public required Func<string, string?, string?> BuildOpenCodeSessionUrl { get; init; }
    public required Action OnChange { get; init; }
}
