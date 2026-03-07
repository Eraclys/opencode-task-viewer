namespace TaskViewer.SonarQube;

public sealed class SonarQubeApiClient : ISonarQubeService
{
    readonly Func<SonarQubeTypedHttpClient> _createClient;

    public SonarQubeApiClient(Func<SonarQubeTypedHttpClient> createClient)
    {
        _createClient = createClient;
    }

    public Task<SonarIssuesSearchResponse> SearchIssuesAsync(Dictionary<string, string?> query, int fallbackPageIndex, int fallbackPageSize, CancellationToken cancellationToken = default)
        => _createClient().SearchIssuesAsync(query, fallbackPageIndex, fallbackPageSize, cancellationToken);

    Task<SonarIssuesSearchResponse> ISonarQubeService.SearchIssuesAsync(Dictionary<string, string?> query, int fallbackPageIndex, int fallbackPageSize)
        => SearchIssuesAsync(query, fallbackPageIndex, fallbackPageSize);

    public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
        => _createClient().GetRuleAsync(ruleKey, cancellationToken);

    Task<SonarRuleDetailsResponse> ISonarQubeService.GetRuleAsync(string ruleKey)
        => GetRuleAsync(ruleKey);
}
