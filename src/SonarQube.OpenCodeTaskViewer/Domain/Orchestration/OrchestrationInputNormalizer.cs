namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

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

    public (int PageIndex, int PageSize) ParseIssuePaging(int? page, int? pageSize)
    {
        var p = Math.Clamp(page.GetValueOrDefault(1), 1, int.MaxValue);
        var ps = Math.Clamp(pageSize.GetValueOrDefault(100), 1, 500);

        return (p, ps);
    }

    public bool HasSingleSpecificRule(IReadOnlyList<string> ruleKeys) => ruleKeys.Count == 1 && !string.Equals(ruleKeys[0], "all", StringComparison.OrdinalIgnoreCase);
}
