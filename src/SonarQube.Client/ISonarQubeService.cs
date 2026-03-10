namespace SonarQube.Client;

public interface ISonarQubeService
{
    Task<SonarIssuesSearchResponse> SearchIssuesAsync(SearchIssuesQuery query, CancellationToken cancellationToken = default);

    Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default);
}
