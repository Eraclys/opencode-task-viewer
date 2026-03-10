namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record DispatchFailureDecision(QueueState State, DateTimeOffset? NextAttemptAt);
