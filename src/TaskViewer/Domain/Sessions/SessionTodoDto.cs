namespace TaskViewer.Domain.Sessions;

public sealed record SessionTodoDto(string Content, string Status, string? Priority)
{
    public ViewerTaskStatus TaskStatus => ViewerTaskStatus.FromRaw(Status);
}
