namespace TaskViewer.Server.Domain;

public static class SessionStatusPolicy
{
    public static bool IsRuntimeRunning(string? type)
    {
        var normalized = (type ?? string.Empty).Trim().ToLowerInvariant();

        return normalized is "busy" or "retry" or "running";
    }
}
