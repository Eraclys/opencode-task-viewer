namespace TaskViewer.Application.Orchestration;

public interface ISonarRuleReadService
{
    Task<string> GetRuleDisplayName(string ruleKey);
}
