using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

sealed class TestTaskReadinessGate : ITaskReadinessGate
{
    readonly TaskReadinessDecision _decision;

    public TestTaskReadinessGate(bool isReady = true, string? reason = null)
    {
        _decision = new TaskReadinessDecision(isReady, reason);
    }

    public Task<TaskReadinessDecision> EvaluateAsync(QueueItemRecord task, IReadOnlyList<NormalizedIssue> issues) => Task.FromResult(_decision);
}
