namespace TaskViewer.SonarQube;

public readonly record struct SonarIssueStatus
{
    SonarIssueStatus(string value, bool alreadyNormalized)
    {
        Value = alreadyNormalized ? value : Normalize(value);
    }

    public string Value { get; }
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public static SonarIssueStatus Open { get; } = new("OPEN", true);
    public static SonarIssueStatus Confirmed { get; } = new("CONFIRMED", true);
    public static SonarIssueStatus Reopened { get; } = new("REOPENED", true);
    public static SonarIssueStatus Resolved { get; } = new("RESOLVED", true);
    public static SonarIssueStatus Closed { get; } = new("CLOSED", true);

    public static SonarIssueStatus FromRaw(string? value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? default
            : new SonarIssueStatus(normalized, true);
    }

    public static IReadOnlyList<SonarIssueStatus> ParseCsv(string? value)
    {
        var result = new List<SonarIssueStatus>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in SplitCsv(value))
        {
            var item = FromRaw(part);
            if (!item.HasValue || !seen.Add(item.Value))
                continue;

            result.Add(item);
        }

        return result;
    }

    public string? OrNull() => HasValue ? Value : null;

    public string? Or(string? fallback) => HasValue ? Value : fallback;

    public IReadOnlyList<SonarIssueStatus> ToFilterList() => HasValue ? [this] : [];

    public override string ToString() => Value;

    static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    static IEnumerable<string> SplitCsv(string? value)
        => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
