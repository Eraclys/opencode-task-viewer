using System.Globalization;

namespace TaskViewer.Server.Domain;

public static class TimeParser
{
    public static string? ParseIsoTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var dateTime)
            ? dateTime.ToUniversalTime().ToString("O")
            : null;
    }
}
