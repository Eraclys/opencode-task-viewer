namespace TaskViewer.Application.Orchestration;

public interface ITaskReadinessGate
{
    Task<TaskReadinessDecision> EvaluateAsync(QueueItemRecord task, IReadOnlyList<NormalizedIssue> issues);
}
