namespace TaskViewer.Domain.Orchestration;

public sealed record DispatchFailureDecision(QueueState State, DateTimeOffset? NextAttemptAt);
