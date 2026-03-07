using TaskViewer.OpenCode;
using TaskViewer.Server.Infrastructure.Orchestration;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Tests;

public sealed class SonarOrchestratorSonarGatewayTests
{
    [Fact]
    public async Task ListIssues_UsesInjectedSonarGateway_WhenProvided()
    {
        var gateway = new FakeSonarGateway();

        await using var orchestrator = new SonarOrchestrator(
            new SonarOrchestratorOptions
            {
                SonarUrl = string.Empty,
                SonarToken = string.Empty,
                SonarQubeService = gateway,
                DbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-sonar-gateway-{Guid.NewGuid():N}.sqlite"),
                MaxActive = 1,
                PollMs = 1000,
                MaxAttempts = 1,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeStatusReader = new DisabledOpenCodeStatusReader(),
                OpenCodeDispatchClient = new DisabledOpenCodeDispatchClient(),
                NormalizeDirectory = value => value,
                BuildOpenCodeSessionUrl = (_, _) => null,
                OnChange = () => { }
            });

        var mapping = await orchestrator.UpsertMapping(
            new UpsertMappingRequest(
                Id: null,
                SonarProjectKey: "alpha-key",
                Directory: "C:/Work/Alpha",
                Branch: null,
                Enabled: true));

        var result = await orchestrator.ListIssues(
            mapping.Id,
            "CODE_SMELL",
            null,
            null,
            "1",
            "20",
            null);

        Assert.Single(result.Issues);
        Assert.True(gateway.Calls > 0);
    }

    sealed class FakeSonarGateway : ISonarQubeService
    {
        public int Calls { get; private set; }

        public Task<SonarIssuesSearchResponse> SearchIssuesAsync(
            Dictionary<string, string?> query,
            int fallbackPageIndex,
            int fallbackPageSize)
        {
            Calls++;

            return Task.FromResult(
                new SonarIssuesSearchResponse(
                    1,
                    20,
                    1,
                    [
                        new SonarIssueTransport(
                            "sq-1",
                            null,
                            "CODE_SMELL",
                            null,
                            "MAJOR",
                            "javascript:S1126",
                            "Remove redundant code",
                            "alpha-key:src/file.js",
                            null,
                            "5",
                            "OPEN")
                    ]));
        }

        public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey)
            => Task.FromResult(new SonarRuleDetailsResponse(ruleKey));
    }
}
