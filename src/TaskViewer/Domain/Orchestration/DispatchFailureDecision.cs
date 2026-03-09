namespace TaskViewer.Domain.Orchestration;

public sealed record DispatchFailureDecision(string State, DateTimeOffset? NextAttemptAt);
