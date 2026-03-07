using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Application.Orchestration;

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
            CreatedAt = "",
            UpdatedAt = ""
        };

        var summary = await service.SummarizeRulesAsync(mapping, "code_smell", "open", maxScanIssues: 5000);

        Assert.Equal("CODE_SMELL", summary.IssueType);
        Assert.Equal("OPEN", summary.IssueStatus);
        Assert.Equal(3, summary.ScannedIssues);
        Assert.False(summary.Truncated);
        Assert.Equal(2, summary.Rules.Count);
        Assert.Equal("javascript:S1126", summary.Rules[0].Key);
        Assert.Equal(2, summary.Rules[0].Count);
    }

    private sealed class FakeGateway : ISonarGateway
    {
        public Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query)
        {
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["paging"] = new JsonObject
                {
                    ["total"] = 3
                },
                ["issues"] = new JsonArray
                {
                    new JsonObject { ["rule"] = "javascript:S1126" },
                    new JsonObject { ["rule"] = "javascript:S1126" },
                    new JsonObject { ["rule"] = "javascript:S3776" }
                }
            });
        }
    }

    private sealed class FakeRuleReadService : ISonarRuleReadService
    {
        public Task<string> GetRuleDisplayName(string ruleKey)
        {
            return Task.FromResult($"Name for {ruleKey}");
        }
    }
}
