namespace TaskViewer.Infrastructure.Orchestration;

public sealed record QueueSkip(string IssueKey, string Reason);
