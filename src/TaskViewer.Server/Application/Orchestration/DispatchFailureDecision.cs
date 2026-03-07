namespace TaskViewer.Server.Application.Orchestration;

public sealed record DispatchFailureDecision(string State, DateTimeOffset? NextAttemptAt);
