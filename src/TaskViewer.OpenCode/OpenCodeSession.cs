namespace TaskViewer.OpenCode;

public sealed record OpenCodeSession(
    string Id,
    string? Name,
    string? Directory,
    string? Project,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ArchivedAt);
