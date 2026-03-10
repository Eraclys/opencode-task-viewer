namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record TaskReadinessDecision(bool IsReady, string? Reason);
