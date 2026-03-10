namespace SonarQube.Client;

public readonly record struct SonarIssueSeverity
{
    SonarIssueSeverity(string value, bool alreadyNormalized)
    {
        Value = alreadyNormalized ? value : Normalize(value);
    }

    public string Value { get; }
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public static SonarIssueSeverity Blocker { get; } = new("BLOCKER", true);
    public static SonarIssueSeverity Critical { get; } = new("CRITICAL", true);
    public static SonarIssueSeverity Major { get; } = new("MAJOR", true);
    public static SonarIssueSeverity Minor { get; } = new("MINOR", true);

    public int PriorityScore => Value switch
    {
        "BLOCKER" => 60,
        "CRITICAL" => 45,
        "MAJOR" => 30,
        "MINOR" => 15,
        _ => 5
    };

    public static SonarIssueSeverity FromRaw(string? value)
    {
        var normalized = Normalize(value);

        return string.IsNullOrWhiteSpace(normalized)
            ? default
            : new SonarIssueSeverity(normalized, true);
    }

    public static IReadOnlyList<SonarIssueSeverity> ParseCsv(string? value)
    {
        var result = new List<SonarIssueSeverity>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in SplitCsv(value))
        {
            var item = FromRaw(part);

            if (!item.HasValue ||
                !seen.Add(item.Value))
                continue;

            result.Add(item);
        }

        return result;
    }

    public string? OrNull() => HasValue ? Value : null;

    public string? Or(string? fallback) => HasValue ? Value : fallback;

    public IReadOnlyList<SonarIssueSeverity> ToFilterList() => HasValue ? [this] : [];

    public override string ToString() => Value;

    static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    static IEnumerable<string> SplitCsv(string? value)
        => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
