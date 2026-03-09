namespace TaskViewer.Domain.Sessions;

public sealed record SessionTasksResult(bool Found, IReadOnlyList<ViewerTaskDto> Tasks);
