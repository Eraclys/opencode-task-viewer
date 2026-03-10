namespace SonarQube.OpenCodeTaskViewer.Domain.Sessions;

public sealed class ViewerTaskDto
{
    public required string Id { get; init; }
    public required string Subject { get; init; }
    public required string Status { get; init; }
    public string? Priority { get; init; }
}
