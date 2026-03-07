using TaskViewer.Server.Infrastructure.Orchestration;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Tests;

public sealed class CachedSonarRuleReadServiceTests
{
    [Fact]
    public async Task GetRuleDisplayName_UsesCache_ForRepeatedKeys()
    {
        var gateway = new FakeSonarGateway();
        var service = new CachedSonarRuleReadService(gateway);

        var first = await service.GetRuleDisplayName("javascript:S1126");
        var second = await service.GetRuleDisplayName("javascript:S1126");

        Assert.Equal("No collapsible if statements", first);
        Assert.Equal(first, second);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public async Task GetRuleDisplayName_FallsBackToRuleKey_OnGatewayFailure()
    {
        var gateway = new ThrowingGateway();
        var service = new CachedSonarRuleReadService(gateway);

        var name = await service.GetRuleDisplayName("javascript:S3776");

        Assert.Equal("javascript:S3776", name);
    }

    sealed class FakeSonarGateway : ISonarQubeService
    {
        public int CallCount { get; private set; }

        public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey)
        {
            CallCount++;
            return Task.FromResult(new SonarRuleDetailsResponse("No collapsible if statements"));
        }

        public Task<SonarIssuesSearchResponse> SearchIssuesAsync(Dictionary<string, string?> query, int fallbackPageIndex, int fallbackPageSize)
            => Task.FromResult(new SonarIssuesSearchResponse(fallbackPageIndex, fallbackPageSize, 0, []));
    }

    sealed class ThrowingGateway : ISonarQubeService
    {
        public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey) => throw new InvalidOperationException("boom");

        public Task<SonarIssuesSearchResponse> SearchIssuesAsync(Dictionary<string, string?> query, int fallbackPageIndex, int fallbackPageSize)
            => throw new InvalidOperationException("boom");
    }
}
