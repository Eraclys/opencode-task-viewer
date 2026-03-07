using System.Text.Json.Nodes;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

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

    sealed class FakeSonarGateway : ISonarGateway
    {
        public int CallCount { get; private set; }

        public Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query)
        {
            CallCount++;

            return Task.FromResult<JsonNode?>(
                new JsonObject
                {
                    ["rule"] = new JsonObject
                    {
                        ["name"] = "No collapsible if statements"
                    }
                });
        }
    }

    sealed class ThrowingGateway : ISonarGateway
    {
        public Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query) => throw new InvalidOperationException("boom");
    }
}
