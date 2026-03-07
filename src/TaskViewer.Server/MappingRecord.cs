namespace TaskViewer.Server;

public sealed class MappingRecord
{
    public int Id { get; init; }
    public string SonarProjectKey { get; init; } = "";
    public string Directory { get; init; } = "";
    public string? Branch { get; init; }
    public bool Enabled { get; init; }
    public string CreatedAt { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
}
