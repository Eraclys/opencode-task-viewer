using System.Text.RegularExpressions;

namespace TaskViewer.Server.Domain;

public static class DirectoryPath
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var s = value.Trim();

        if (s is "/" or "\\")
            return s;

        if (Regex.IsMatch(s, "^[a-zA-Z]:[\\\\/]$"))
            return s;

        return s.TrimEnd('/', '\\');
    }

    public static string? ToForwardSlash(string? value)
    {
        var normalized = Normalize(value);

        return normalized?.Replace('\\', '/');
    }

    public static string GetCacheKey(string? value) => ToForwardSlash(value) ?? string.Empty;

    public static List<string> GetVariants(string? value)
    {
        var normalized = Normalize(value);

        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        var variants = new List<string>
        {
            normalized
        };

        var forward = normalized.Replace('\\', '/');
        var backward = normalized.Replace('/', '\\');

        if (!variants.Contains(forward, StringComparer.Ordinal))
            variants.Add(forward);

        if (!variants.Contains(backward, StringComparer.Ordinal))
            variants.Add(backward);

        return variants;
    }
}
