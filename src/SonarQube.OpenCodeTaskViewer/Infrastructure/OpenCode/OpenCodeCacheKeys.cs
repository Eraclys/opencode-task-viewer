using SonarQube.OpenCodeTaskViewer.Domain;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.OpenCode;

public static class OpenCodeCacheKeys
{
    public static string? Directory(string? directory)
    {
        var parsedDirectory = DirectoryPath.Parse(directory);

        return parsedDirectory?.CacheKey;
    }

    public static string DirectorySession(string? directory, string sessionId)
        => $"{Directory(directory)}::{sessionId}";
}
