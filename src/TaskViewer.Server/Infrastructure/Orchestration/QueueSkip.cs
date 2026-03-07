namespace TaskViewer.Server.Infrastructure.Orchestration;

sealed record QueueSkip(string IssueKey, string Reason);
