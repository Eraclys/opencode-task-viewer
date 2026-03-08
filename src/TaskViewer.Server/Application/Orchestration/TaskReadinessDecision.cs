namespace TaskViewer.Server.Application.Orchestration;

sealed record TaskReadinessDecision(bool IsReady, string? Reason);
