namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

public sealed class MappingRecord
{
    public int Id { get; init; }
    public string SonarProjectKey { get; init; } = "";
    public string Directory { get; init; } = "";
    public string? Branch { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
