using TaskViewer.Domain.Orchestration;

namespace TaskViewer.Infrastructure.Orchestration;

sealed class FallbackSonarRuleReadService : ISonarRuleReadService
{
    public Task<string> GetRuleDisplayName(string ruleKey) => Task.FromResult((ruleKey ?? string.Empty).Trim());
}
