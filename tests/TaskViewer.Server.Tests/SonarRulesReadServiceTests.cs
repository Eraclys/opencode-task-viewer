using TaskViewer.Application.Orchestration;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Tests;

public sealed class SonarRulesReadServiceTests
{
    [Fact]
    public async Task SummarizeRulesAsync_AggregatesAndSortsRules()
    {
        var gateway = new FakeGateway();
        var ruleRead = new FakeRuleReadService();
        var service = new SonarRulesReadService(gateway, ruleRead);

        var mapping = new MappingRecord
        {
            Id = 1,
            SonarProjectKey = "alpha-key",
            Directory = "C:/Work/Alpha",
            Branch = "main",
            Enabled = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        var summary = await service.SummarizeRulesAsync(
            mapping,
            "code_smell",
            "open",
            5000);

        Assert.Equal("CODE_SMELL", summary.IssueType);
        Assert.Equal("OPEN", summary.IssueStatus);
        Assert.Equal(3, summary.ScannedIssues);
        Assert.False(summary.Truncated);
        Assert.Equal(2, summary.Rules.Count);
        Assert.Equal("javascript:S1126", summary.Rules[0].Key);
        Assert.Equal(2, summary.Rules[0].Count);
    }

    sealed class FakeGateway : ISonarQubeService
    {
        public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey)
            => Task.FromResult(new SonarRuleDetailsResponse(null));

        public Task<SonarIssuesSearchResponse> SearchIssuesAsync(
            Dictionary<string, string?> query,
            int fallbackPageIndex,
            int fallbackPageSize)
            => Task.FromResult(
                new SonarIssuesSearchResponse(
                    fallbackPageIndex,
                    fallbackPageSize,
                    3,
                    [
                        new SonarIssueTransport(null, null, null, null, null, "javascript:S1126", null, null, null, null, null),
                        new SonarIssueTransport(null, null, null, null, null, "javascript:S1126", null, null, null, null, null),
                        new SonarIssueTransport(null, null, null, null, null, "javascript:S3776", null, null, null, null, null)
                    ]));
    }

    sealed class FakeRuleReadService : ISonarRuleReadService
    {
        public Task<string> GetRuleDisplayName(string ruleKey) => Task.FromResult($"Name for {ruleKey}");
    }
}
