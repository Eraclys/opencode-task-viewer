using System.Text.Json.Nodes;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class OrchestrationStatusServiceTests
{
    [Fact]
    public void IsConfigured_UsesGatewayOrCredentials()
    {
        var sut = new OrchestrationStatusService();

        Assert.True(sut.IsConfigured(new FakeSonarGateway(), string.Empty, string.Empty));
        Assert.True(sut.IsConfigured(null, "http://sonar.local", "token"));
        Assert.False(sut.IsConfigured(null, " ", "token"));
        Assert.False(sut.IsConfigured(null, "http://sonar.local", " "));
    }

    [Fact]
    public void BuildPublicConfig_ProducesExpectedShape()
    {
        var sut = new OrchestrationStatusService();
        var result = sut.BuildPublicConfig(
            configured: true,
            maxActive: 2,
            pollMs: 1000,
            maxAttempts: 3,
            maxWorkingGlobal: 11,
            workingResumeBelow: 7);

        Assert.Equal("True", result.GetType().GetProperty("configured")?.GetValue(result)?.ToString());
        Assert.Equal("2", result.GetType().GetProperty("maxActive")?.GetValue(result)?.ToString());
        Assert.Equal("1000", result.GetType().GetProperty("pollMs")?.GetValue(result)?.ToString());
        Assert.Equal("3", result.GetType().GetProperty("maxAttempts")?.GetValue(result)?.ToString());
        Assert.Equal("11", result.GetType().GetProperty("maxWorkingGlobal")?.GetValue(result)?.ToString());
        Assert.Equal("7", result.GetType().GetProperty("workingResumeBelow")?.GetValue(result)?.ToString());
    }

    [Fact]
    public void BuildWorkerState_ProducesExpectedShape()
    {
        var sut = new OrchestrationStatusService();
        var state = new WorkloadBackpressureState(
            Paused: true,
            WorkingCount: 4,
            MaxWorkingGlobal: 10,
            WorkingResumeBelow: 5,
            SampleAt: "2026-01-01T00:00:00.0000000+00:00");

        var result = sut.BuildWorkerState(3, 2, state);

        Assert.Equal("3", result.GetType().GetProperty("inFlightDispatches")?.GetValue(result)?.ToString());
        Assert.Equal("2", result.GetType().GetProperty("maxActiveDispatches")?.GetValue(result)?.ToString());
        Assert.Equal("True", result.GetType().GetProperty("pausedByWorking")?.GetValue(result)?.ToString());
        Assert.Equal("4", result.GetType().GetProperty("workingCount")?.GetValue(result)?.ToString());
        Assert.Equal("10", result.GetType().GetProperty("maxWorkingGlobal")?.GetValue(result)?.ToString());
        Assert.Equal("5", result.GetType().GetProperty("workingResumeBelow")?.GetValue(result)?.ToString());
        Assert.Equal("2026-01-01T00:00:00.0000000+00:00", result.GetType().GetProperty("workingSampleAt")?.GetValue(result)?.ToString());
    }

    private sealed class FakeSonarGateway : ISonarGateway
    {
        public Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query)
            => Task.FromResult<JsonNode?>(null);
    }
}
