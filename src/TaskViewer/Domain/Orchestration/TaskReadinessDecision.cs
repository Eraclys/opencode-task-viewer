namespace TaskViewer.Domain.Orchestration;

public sealed record TaskReadinessDecision(bool IsReady, string? Reason);
