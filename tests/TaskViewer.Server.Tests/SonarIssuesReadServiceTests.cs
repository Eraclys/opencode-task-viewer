using TaskViewer.Application.Orchestration;
using TaskViewer.SonarQube;

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
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
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

    sealed class FakeGateway : ISonarQubeService
    {
        public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
            => Task.FromResult(new SonarRuleDetailsResponse(null));

        public Task<SonarIssuesSearchResponse> SearchIssuesAsync(SearchIssuesQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(
                new SonarIssuesSearchResponse(
                    query.PageIndex,
                    query.PageSize,
                    1,
                    [
                        new SonarIssueTransport(
                            Key: "sq-1",
                            IssueKey: null,
                            Type: "CODE_SMELL",
                            IssueType: null,
                            Severity: null,
                            Rule: "javascript:S1126",
                            Message: "Remove this",
                            Component: "alpha-key:src/file.js",
                            File: null,
                            Line: "11",
                            Status: "OPEN")
                    ]));
    }
}
