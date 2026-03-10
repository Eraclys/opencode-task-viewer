using TaskViewer.SonarQube;

namespace TaskViewer.Infrastructure.Orchestration;

public sealed class FakeSonarQubeService : ISonarQubeService
{
    static readonly Dictionary<string, string> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["javascript:S1126"] = "Assignments should not be redundant",
        ["javascript:S3776"] = "Cognitive Complexity of functions should not be too high",
        ["javascript:S5144"] = "Constructing URLs from user input is security-sensitive",
        ["javascript:S1481"] = "Unused local variables should be removed"
    };

    static readonly IReadOnlyList<SonarIssueTransport> Issues =
    [
        new("sq-gamma-001", null, "CODE_SMELL", null, "MAJOR", "javascript:S1126", "Remove this redundant assignment.", "gamma-key:src/worker.js", null, "42", "OPEN"),
        new("sq-gamma-002", null, "CODE_SMELL", null, "CRITICAL", "javascript:S3776", "Refactor this function to reduce Cognitive Complexity.", "gamma-key:src/server.js", null, "17", "CONFIRMED"),
        new("sq-gamma-003", null, "VULNERABILITY", null, "BLOCKER", "javascript:S5144", "Review this URL construction for SSRF risk.", "gamma-key:src/auth.js", null, "10", "OPEN"),
        new("sq-gamma-004", null, "CODE_SMELL", null, "MAJOR", "javascript:S3776", "Reduce the Cognitive Complexity of this function.", "gamma-key:src/jobs.js", null, "91", "OPEN"),
        new("sq-alpha-001", null, "CODE_SMELL", null, "MINOR", "javascript:S1481", "Remove this unused local variable.", "alpha-key:src/index.js", null, "7", "OPEN")
    ];

    public Task<SonarIssuesSearchResponse> SearchIssuesAsync(SearchIssuesQuery query, CancellationToken cancellationToken = default)
    {
        var componentKeys = NormalizeSet([query.ComponentKey]);
        var types = NormalizeSet(query.Types.Select(type => type.Value).ToList());
        var severities = NormalizeSet(query.Severities.Select(severity => severity.Value).ToList());
        var statuses = NormalizeSet(query.Statuses.Select(status => status.Value).ToList());
        var rules = NormalizeSet(query.RuleKeys, preserveCase: true);
        var issueKeys = NormalizeSet(query.IssueKeys, preserveCase: true);
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Max(1, query.PageSize);

        var filtered = Issues
            .Where(issue => componentKeys.Count == 0 || componentKeys.Contains(ParseProjectKey(issue.Component)))
            .Where(issue => types.Count == 0 || types.Contains(SonarIssueType.FromRaw(issue.Type).OrNull() ?? string.Empty))
            .Where(issue => severities.Count == 0 || severities.Contains(SonarIssueSeverity.FromRaw(issue.Severity).OrNull() ?? string.Empty))
            .Where(issue => statuses.Count == 0 || statuses.Contains(SonarIssueStatus.FromRaw(issue.Status).OrNull() ?? string.Empty))
            .Where(issue => rules.Count == 0 || rules.Contains((issue.Rule ?? string.Empty).Trim()))
            .Where(issue => issueKeys.Count == 0 || issueKeys.Contains((issue.Key ?? string.Empty).Trim()))
            .ToList();

        var total = filtered.Count;
        var paged = filtered
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new SonarIssuesSearchResponse(pageIndex, pageSize, total, paged));
    }

    public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
    {
        Rules.TryGetValue(ruleKey.Trim(), out var name);
        return Task.FromResult(new SonarRuleDetailsResponse(name));
    }

    static HashSet<string> NormalizeSet(IReadOnlyList<string> raw, bool preserveCase = false)
    {
        var comparer = preserveCase ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var values = new HashSet<string>(comparer);

        if (raw.Count == 0)
            return values;

        foreach (var part in raw.Where(part => !string.IsNullOrWhiteSpace(part)))
            values.Add(preserveCase ? part : part.ToUpperInvariant());

        return values;
    }

    static string ParseProjectKey(string? component)
    {
        var raw = component ?? string.Empty;
        var separator = raw.IndexOf(':');
        return separator >= 0 ? raw[..separator] : raw;
    }
}
