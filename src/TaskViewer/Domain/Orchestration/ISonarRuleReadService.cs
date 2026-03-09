namespace TaskViewer.Domain.Orchestration;

public interface ISonarRuleReadService
{
    Task<string> GetRuleDisplayName(string ruleKey);
}
