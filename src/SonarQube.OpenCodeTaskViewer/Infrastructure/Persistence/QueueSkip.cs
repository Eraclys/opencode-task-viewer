namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

public sealed record QueueSkip(string IssueKey, string Reason);
