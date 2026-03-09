using TaskViewer.Domain.Orchestration;
using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Server.Tests;

sealed class TestTaskReadinessGate : ITaskReadinessGate
{
    readonly TaskReadinessDecision _decision;

    public TestTaskReadinessGate(bool isReady = true, string? reason = null)
    {
        _decision = new TaskReadinessDecision(isReady, reason);
    }

    public Task<TaskReadinessDecision> EvaluateAsync(QueueItemRecord task, IReadOnlyList<NormalizedIssue> issues)
    {
        return Task.FromResult(_decision);
    }
}
