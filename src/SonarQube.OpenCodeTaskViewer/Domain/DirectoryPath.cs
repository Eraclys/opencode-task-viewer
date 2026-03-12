using System.Text.RegularExpressions;

namespace SonarQube.OpenCodeTaskViewer.Domain;

public readonly record struct DirectoryPath
{
    DirectoryPath(string normalized)
    {
        Value = normalized;
    }

    public string Value { get; }

    public bool IsRoot => Value is "/" or "\\";

    public string CacheKey => ToForwardSlash(Value) ?? string.Empty;

    public List<string> Variants => GetVariants(Value);

    public static DirectoryPath? Parse(string? value)
    {
        var normalized = Normalize(value);

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : new DirectoryPath(normalized);
    }

    public override string ToString() => Value;

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
