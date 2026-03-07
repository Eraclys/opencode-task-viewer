namespace TaskViewer.Server.Application.Sessions;

public sealed record ArchiveSessionResult(bool Found, DateTimeOffset? ArchivedAt);
