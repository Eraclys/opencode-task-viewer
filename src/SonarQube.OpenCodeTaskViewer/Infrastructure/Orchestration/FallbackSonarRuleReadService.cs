using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;

sealed class FallbackSonarRuleReadService : ISonarRuleReadService
{
    public Task<string> GetRuleDisplayName(string ruleKey) => Task.FromResult((ruleKey ?? string.Empty).Trim());
}
