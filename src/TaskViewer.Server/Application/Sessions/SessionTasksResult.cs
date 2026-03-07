namespace TaskViewer.Server.Application.Sessions;

public sealed record SessionTasksResult(bool Found, IReadOnlyList<ViewerTaskDto> Tasks);
