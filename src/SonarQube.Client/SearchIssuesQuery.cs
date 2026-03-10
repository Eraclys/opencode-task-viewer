namespace SonarQube.Client;

public sealed record class SearchIssuesQuery
{
    public required string ComponentKey { get; init; }
    public string? Branch { get; init; }
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 100;
    public IReadOnlyList<SonarIssueType> Types { get; init; } = [];
    public IReadOnlyList<SonarIssueSeverity> Severities { get; init; } = [];
    public IReadOnlyList<SonarIssueStatus> Statuses { get; init; } = [];
    public IReadOnlyList<string> RuleKeys { get; init; } = [];
    public IReadOnlyList<string> IssueKeys { get; init; } = [];
}
