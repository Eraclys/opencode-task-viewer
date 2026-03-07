using TaskViewer.Server.Domain;

namespace TaskViewer.Server.Infrastructure.OpenCode;

static class OpenCodeCacheKeys
{
    public static string? Directory(string? directory)
    {
        var normalizedDirectory = DirectoryPath.Normalize(directory) ?? directory;
        return string.IsNullOrWhiteSpace(normalizedDirectory) ? null : DirectoryPath.GetCacheKey(normalizedDirectory);
    }

    public static string DirectorySession(string? directory, string sessionId)
        => $"{Directory(directory)}::{sessionId}";
}
