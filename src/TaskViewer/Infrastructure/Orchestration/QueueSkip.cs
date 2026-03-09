namespace TaskViewer.Infrastructure.Orchestration;

sealed record QueueSkip(string IssueKey, string Reason);
