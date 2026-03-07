using TaskViewer.Server.Application.Sessions;

namespace TaskViewer.Server;

sealed class SessionCache
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.MinValue;
    public List<OpenCodeSessionDto> Data { get; set; } = [];
    public Dictionary<string, OpenCodeSessionDto> ById { get; set; } = new(StringComparer.Ordinal);
}