namespace SonarQube.Client;

public sealed class SonarQubeService : ISonarQubeService
{
    readonly Func<SonarQubeHttpClient> _createClient;

    public SonarQubeService(Func<SonarQubeHttpClient> createClient)
    {
        _createClient = createClient;
    }

    public Task<SonarIssuesSearchResponse> SearchIssuesAsync(SearchIssuesQuery query, CancellationToken cancellationToken = default)
        => _createClient().SearchIssuesAsync(query, cancellationToken);

    public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
        => _createClient().GetRuleAsync(ruleKey, cancellationToken);
}
