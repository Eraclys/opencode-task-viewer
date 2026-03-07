using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Infrastructure.Orchestration;

sealed class FallbackSonarRuleReadService : ISonarRuleReadService
{
    public Task<string> GetRuleDisplayName(string ruleKey) => Task.FromResult((ruleKey ?? string.Empty).Trim());
}
