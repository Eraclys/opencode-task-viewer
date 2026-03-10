using TaskViewer.Domain.Sessions;

namespace TaskViewer.Domain;

public static class SessionStatusPolicy
{
    public static bool IsRuntimeRunning(string? type)
        => SessionRuntimeStatus.FromRaw(type).IsRunning;
}
