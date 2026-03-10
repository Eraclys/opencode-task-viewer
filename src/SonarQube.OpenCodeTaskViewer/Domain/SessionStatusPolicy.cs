using OpenCode.Client;

namespace SonarQube.OpenCodeTaskViewer.Domain;

public static class SessionStatusPolicy
{
    public static bool IsRuntimeRunning(string? type)
        => SessionRuntimeStatus.FromRaw(type).IsRunning;
}
