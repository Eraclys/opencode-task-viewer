using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.OpenCode;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Tests;

public sealed class SonarOrchestratorSonarGatewayTests
{
    [Fact]
    public async Task ListIssues_UsesInjectedSonarGateway_WhenProvided()
    {
        var gateway = new FakeSonarGateway();
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-sonar-gateway-{Guid.NewGuid():N}.sqlite");

        await using var orchestrator = new SonarOrchestrator(
            new SonarOrchestratorOptions
            {
                SonarUrl = string.Empty,
                SonarToken = string.Empty,
                SonarQubeService = gateway,
                DbPath = dbPath,
                Persistence = new SqliteOrchestrationPersistence(dbPath, () => { }),
                MaxActive = 1,
                PerProjectMaxActive = 1,
                PollMs = 1000,
                LeaseSeconds = 180,
                MaxAttempts = 1,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeApiClient = new DisabledOpenCodeService(),
                TaskReadinessGate = new TestTaskReadinessGate(),
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

        public Task<SonarIssuesSearchResponse> SearchIssuesAsync(SearchIssuesQuery query, CancellationToken cancellationToken = default)
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

        public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
            => Task.FromResult(new SonarRuleDetailsResponse(ruleKey));
    }
}
