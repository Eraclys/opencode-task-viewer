using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class SonarEnqueueAllIssuesReadServiceTests
{
    [Fact]
    public async Task CollectMatchingIssuesAsync_StopsAtScanLimitAndReportsMatchedFromPaging()
    {
        var gateway = new PagingGateway(firstTotal: 5);
        var service = new SonarEnqueueAllIssuesReadService(gateway);
        var mapping = CreateMapping();

        var result = await service.CollectMatchingIssuesAsync(
            mapping,
            "code_smell",
            "major",
            "open",
            ["javascript:S1126"],
            maxScanIssues: 2);

        Assert.Equal(5, result.Matched);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal("CODE_SMELL", gateway.LastIssueType);
        Assert.Equal("MAJOR", gateway.LastSeverity);
        Assert.Equal("OPEN", gateway.LastIssueStatus);
    }

    [Fact]
    public async Task CollectMatchingIssuesAsync_FetchesUntilEndWhenWithinLimit()
    {
        var gateway = new PagingGateway(firstTotal: 501, firstPageIssueCount: 500);
        var service = new SonarEnqueueAllIssuesReadService(gateway);

        var result = await service.CollectMatchingIssuesAsync(
            CreateMapping(),
            issueType: null,
            severity: null,
            issueStatus: null,
            ["javascript:S1126"],
            maxScanIssues: 1000);

        Assert.Equal(2, gateway.RequestCount);
        Assert.Equal(501, result.Matched);
        Assert.False(result.Truncated);
        Assert.Equal(501, result.Issues.Count);
    }

    private static MappingRecord CreateMapping()
    {
        return new MappingRecord
        {
            Id = 1,
            SonarProjectKey = "alpha-key",
            Directory = "C:/Work/Alpha",
            Branch = "main",
            Enabled = true,
            CreatedAt = "",
            UpdatedAt = ""
        };
    }

    private sealed class PagingGateway : ISonarGateway
    {
        private readonly int _firstTotal;
        private readonly int _firstPageIssueCount;

        public PagingGateway(int firstTotal, int firstPageIssueCount = 2)
        {
            _firstTotal = firstTotal;
            _firstPageIssueCount = firstPageIssueCount;
        }

        public int RequestCount { get; private set; }
        public string? LastIssueType { get; private set; }
        public string? LastSeverity { get; private set; }
        public string? LastIssueStatus { get; private set; }

        public Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query)
        {
            RequestCount += 1;
            LastIssueType = query.GetValueOrDefault("types");
            LastSeverity = query.GetValueOrDefault("severities");
            LastIssueStatus = query.GetValueOrDefault("statuses");

            if (RequestCount == 1)
            {
                var issues = new JsonArray();
                for (var i = 1; i <= _firstPageIssueCount; i += 1)
                    issues.Add(new JsonObject { ["key"] = $"sq-{i}" });

                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["paging"] = new JsonObject
                    {
                        ["pageIndex"] = 1,
                        ["pageSize"] = 500,
                        ["total"] = _firstTotal
                    },
                    ["issues"] = issues
                });
            }

            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["paging"] = new JsonObject
                {
                    ["pageIndex"] = 2,
                    ["pageSize"] = 500,
                    ["total"] = 3
                },
                ["issues"] = new JsonArray
                {
                    new JsonObject { ["key"] = "sq-3" }
                }
            });
        }
    }
}
