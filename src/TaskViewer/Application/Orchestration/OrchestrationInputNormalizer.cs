namespace TaskViewer.Application.Orchestration;

public sealed class OrchestrationInputNormalizer : IOrchestrationInputNormalizer
{
    public List<string> NormalizeRuleKeys(string? csv)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var value = csv ?? string.Empty;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                set.Add(part);
        }

        return [.. set];
    }

    public (int PageIndex, int PageSize) ParseIssuePaging(string? page, string? pageSize)
    {
        var p = Math.Clamp(ParseIntSafe(page, 1), 1, int.MaxValue);
        var ps = Math.Clamp(ParseIntSafe(pageSize, 100), 1, 500);
        return (p, ps);
    }

    public bool HasSingleSpecificRule(IReadOnlyList<string> ruleKeys)
    {
        return ruleKeys.Count == 1 && !string.Equals(ruleKeys[0], "all", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseIntSafe(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
