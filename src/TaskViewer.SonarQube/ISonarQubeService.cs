namespace TaskViewer.SonarQube;

public interface ISonarQubeService
{
    Task<SonarIssuesSearchResponse> SearchIssuesAsync(
        Dictionary<string, string?> query,
        int fallbackPageIndex,
        int fallbackPageSize);

    Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey);
}
