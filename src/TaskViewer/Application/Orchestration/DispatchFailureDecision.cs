namespace TaskViewer.Application.Orchestration;

public sealed record DispatchFailureDecision(string State, DateTimeOffset? NextAttemptAt);
