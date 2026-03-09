namespace TaskViewer.Application.Orchestration;

public sealed record TaskReadinessDecision(bool IsReady, string? Reason);
