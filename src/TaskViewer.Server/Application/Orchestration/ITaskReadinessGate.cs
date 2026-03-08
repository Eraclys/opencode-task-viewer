namespace TaskViewer.Server.Application.Orchestration;

interface ITaskReadinessGate
{
    Task<TaskReadinessDecision> EvaluateAsync(QueueItemRecord task, IReadOnlyList<NormalizedIssue> issues);
}
