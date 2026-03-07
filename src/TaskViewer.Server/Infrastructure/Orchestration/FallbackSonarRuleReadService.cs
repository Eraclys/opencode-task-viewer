using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Infrastructure.Orchestration;

internal sealed class FallbackSonarRuleReadService : ISonarRuleReadService
{
    public Task<string> GetRuleDisplayName(string ruleKey)
    {
        return Task.FromResult((ruleKey ?? string.Empty).Trim());
    }
}
