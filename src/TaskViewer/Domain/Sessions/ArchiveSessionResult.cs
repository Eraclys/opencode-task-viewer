namespace TaskViewer.Domain.Sessions;

public sealed record ArchiveSessionResult(bool Found, DateTimeOffset? ArchivedAt);
