using System.Text.Json.Nodes;
using TaskViewer.Server.Application.Orchestration;

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
                SonarGateway = gateway,
                DbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-sonar-gateway-{Guid.NewGuid():N}.sqlite"),
                MaxActive = 1,
                PollMs = 1000,
                MaxAttempts = 1,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeFetch = (_, _) => Task.FromResult<JsonNode?>(null),
                NormalizeDirectory = value => value,
                BuildOpenCodeSessionUrl = (_, _) => null,
                OnChange = () => { }
            });

        var mapping = await orchestrator.UpsertMapping(
            new JsonObject
            {
                ["sonarProjectKey"] = "alpha-key",
                ["directory"] = "C:/Work/Alpha",
                ["enabled"] = true
            });

        var result = await orchestrator.ListIssues(
            mapping.Id,
            "CODE_SMELL",
            null,
            null,
            1,
            20,
            null);

        var issues = (IEnumerable<object>?)result.GetType().GetProperty("issues")?.GetValue(result);

        Assert.NotNull(issues);
        Assert.Single(issues!);
        Assert.True(gateway.Calls > 0);
    }

    sealed class FakeSonarGateway : ISonarGateway
    {
        public int Calls { get; private set; }

        public Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query)
        {
            Calls++;

            if (endpointPath == "/api/issues/search")
            {
                return Task.FromResult<JsonNode?>(
                    new JsonObject
                    {
                        ["paging"] = new JsonObject
                        {
                            ["pageIndex"] = 1,
                            ["pageSize"] = 20,
                            ["total"] = 1
                        },
                        ["issues"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["key"] = "sq-1",
                                ["type"] = "CODE_SMELL",
                                ["severity"] = "MAJOR",
                                ["rule"] = "javascript:S1126",
                                ["message"] = "Remove redundant code",
                                ["component"] = "alpha-key:src/file.js",
                                ["line"] = 5,
                                ["status"] = "OPEN"
                            }
                        }
                    });
            }

            return Task.FromResult<JsonNode?>(null);
        }
    }
}
