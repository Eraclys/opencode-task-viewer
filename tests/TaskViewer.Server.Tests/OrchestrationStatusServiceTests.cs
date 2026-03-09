using TaskViewer.Domain.Orchestration;
using TaskViewer.SonarQube;

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

        Assert.True(result.Configured);
        Assert.Equal(2, result.MaxActive);
        Assert.Equal(1000, result.PollMs);
        Assert.Equal(3, result.MaxAttempts);
        Assert.Equal(11, result.MaxWorkingGlobal);
        Assert.Equal(7, result.WorkingResumeBelow);
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
            SampleAt: DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"));

        var result = sut.BuildWorkerState(3, 2, state);

        Assert.Equal(3, result.InFlightDispatches);
        Assert.Equal(2, result.MaxActiveDispatches);
        Assert.True(result.PausedByWorking);
        Assert.Equal(4, result.WorkingCount);
        Assert.Equal(10, result.MaxWorkingGlobal);
        Assert.Equal(5, result.WorkingResumeBelow);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"), result.WorkingSampleAt);
    }

    private sealed class FakeSonarGateway : ISonarQubeService
    {
        public Task<SonarIssuesSearchResponse> SearchIssuesAsync(SearchIssuesQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new SonarIssuesSearchResponse(query.PageIndex, query.PageSize, 0, []));

        public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
            => Task.FromResult(new SonarRuleDetailsResponse(null));
    }
}
