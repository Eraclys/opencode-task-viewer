namespace TaskViewer.Domain.Sessions;

public sealed class GlobalViewerTaskDto
{
    public required string Id { get; init; }
    public required string Subject { get; init; }
    public required string Status { get; init; }
    public string? Priority { get; init; }
    public required string SessionId { get; init; }
    public string? SessionName { get; init; }
    public string? Project { get; init; }
}
