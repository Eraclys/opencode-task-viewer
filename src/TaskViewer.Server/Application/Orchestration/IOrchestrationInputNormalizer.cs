using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application.Orchestration;

internal interface IOrchestrationInputNormalizer
{
    List<string> NormalizeRuleKeys(object? value);
    (int PageIndex, int PageSize) ParseIssuePaging(object? page, object? pageSize);
    bool HasSingleSpecificRule(IReadOnlyList<string> ruleKeys);
}

internal sealed class OrchestrationInputNormalizer : IOrchestrationInputNormalizer
{
    public List<string> NormalizeRuleKeys(object? value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        if (value is JsonArray arr)
        {
            foreach (var n in arr)
            {
                var key = n?.ToString()?.Trim();

                if (!string.IsNullOrWhiteSpace(key))
                    set.Add(key);
            }

            return [.. set];
        }

        var csv = value?.ToString() ?? string.Empty;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                set.Add(part);
        }

        return [.. set];
    }

    public (int PageIndex, int PageSize) ParseIssuePaging(object? page, object? pageSize)
    {
        var p = Math.Clamp(ParseIntSafe(page, 1), 1, int.MaxValue);
        var ps = Math.Clamp(ParseIntSafe(pageSize, 100), 1, 500);
        return (p, ps);
    }

    public bool HasSingleSpecificRule(IReadOnlyList<string> ruleKeys)
    {
        return ruleKeys.Count == 1 && !string.Equals(ruleKeys[0], "all", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseIntSafe(object? value, int fallback)
    {
        if (value is null)
            return fallback;

        if (value is int i)
            return i;

        if (value is long l && l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }
}
