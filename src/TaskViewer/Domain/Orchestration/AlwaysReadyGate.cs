using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed class AlwaysReadyGate : ITaskReadinessGate
{
    public Task<TaskReadinessDecision> EvaluateAsync(QueueItemRecord task, IReadOnlyList<NormalizedIssue> issues)
        => Task.FromResult(new TaskReadinessDecision(true, null));
}
