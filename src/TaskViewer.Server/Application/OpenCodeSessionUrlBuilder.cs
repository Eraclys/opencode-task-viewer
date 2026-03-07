using System.Text;
using TaskViewer.Server.Domain;

namespace TaskViewer.Server.Application;

public static class OpenCodeSessionUrlBuilder
{
    public static string? Build(string opencodeUrl, string sessionId, string? directory)
    {
        var sid = (sessionId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(sid))
            return null;

        var baseUrl = (opencodeUrl ?? string.Empty).TrimEnd('/');

        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var normalizedDirectory = DirectoryPath.Normalize(directory);

        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return $"{baseUrl}/session/{Uri.EscapeDataString(sid)}";

        var bytes = Encoding.UTF8.GetBytes(normalizedDirectory);

        var slug = Convert
            .ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"{baseUrl}/{slug}/session/{Uri.EscapeDataString(sid)}";
    }
}
