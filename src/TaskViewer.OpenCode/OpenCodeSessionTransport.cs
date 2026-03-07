namespace TaskViewer.OpenCode;

public sealed record OpenCodeSessionTransport(
    string Id,
    string? Name,
    string? Directory,
    string? Project,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ArchivedAt);
