using TaskViewer.OpenCode;

namespace TaskViewer;

sealed class ProjectsCache
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.MinValue;
    public List<OpenCodeProject> Data { get; set; } = [];
}