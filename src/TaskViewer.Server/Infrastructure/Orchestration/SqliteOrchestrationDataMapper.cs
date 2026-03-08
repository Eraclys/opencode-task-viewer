namespace TaskViewer.Server.Infrastructure.Orchestration;

static class SqliteOrchestrationDataMapper
{
    public static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    public static DateTimeOffset ParseRequiredDateTime(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    public static DateTimeOffset? ParseOptionalDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;
    }
}
