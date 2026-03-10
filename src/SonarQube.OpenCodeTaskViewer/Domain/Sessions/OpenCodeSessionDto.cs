namespace SonarQube.OpenCodeTaskViewer.Domain.Sessions;

public sealed record OpenCodeSessionDto(
    string Id,
    string? Name,
    string? Directory,
    string? Project,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);
