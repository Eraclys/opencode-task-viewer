using System.Text.Json.Nodes;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class SonarIssuesReadServiceTests
{
    [Fact]
    public async Task ListIssuesAsync_MapsPagingAndIssueFields()
    {
        var gateway = new FakeGateway();
        var service = new SonarIssuesReadService(gateway);

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

        var result = await service.ListIssuesAsync(
            mapping,
            "code_smell",
            "major",
            "open",
            2,
            100,
            []);

        Assert.Equal(2, result.PageIndex);
        Assert.Equal(100, result.PageSize);
        Assert.Equal(1, result.Total);
        Assert.Single(result.Issues);

        var issue = result.Issues[0];
        Assert.Equal("sq-1", issue.Key);
        Assert.Equal("CODE_SMELL", issue.Type);
        Assert.Equal("src/file.js", issue.RelativePath);
        Assert.Equal("C:/Work/Alpha/src/file.js", issue.AbsolutePath);
    }

    sealed class FakeGateway : ISonarGateway
    {
        public Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query)
        {
            return Task.FromResult<JsonNode?>(
                new JsonObject
                {
                    ["paging"] = new JsonObject
                    {
                        ["pageIndex"] = 2,
                        ["pageSize"] = 100,
                        ["total"] = 1
                    },
                    ["issues"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["key"] = "sq-1",
                            ["type"] = "CODE_SMELL",
                            ["rule"] = "javascript:S1126",
                            ["message"] = "Remove this",
                            ["component"] = "alpha-key:src/file.js",
                            ["line"] = 11,
                            ["status"] = "OPEN"
                        }
                    }
                });
        }
    }
}
