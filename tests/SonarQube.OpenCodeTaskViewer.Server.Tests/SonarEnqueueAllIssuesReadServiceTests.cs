using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class SonarEnqueueAllIssuesReadServiceTests
{
    [Fact]
    public async Task CollectMatchingIssuesAsync_StopsAtScanLimitAndReportsMatchedFromPaging()
    {
        var gateway = new PagingGateway(5);
        var service = new SonarEnqueueAllIssuesReadService(gateway);
        var mapping = CreateMapping();

        var result = await service.CollectMatchingIssuesAsync(
            mapping,
            [SonarIssueType.CodeSmell],
            [SonarIssueSeverity.Major],
            [SonarIssueStatus.Open],
            ["javascript:S1126"],
            2);

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
        var gateway = new PagingGateway(501, 500);
        var service = new SonarEnqueueAllIssuesReadService(gateway);

        var result = await service.CollectMatchingIssuesAsync(
            CreateMapping(),
            [],
            [],
            [],
            ["javascript:S1126"],
            1000);

        Assert.Equal(2, gateway.RequestCount);
        Assert.Equal(501, result.Matched);
        Assert.False(result.Truncated);
        Assert.Equal(501, result.Issues.Count);
    }

    static MappingRecord CreateMapping()
    {
        return new MappingRecord
        {
            Id = 1,
            SonarProjectKey = "alpha-key",
            Directory = "C:/Work/Alpha",
            Branch = "main",
            Enabled = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };
    }

    sealed class PagingGateway : ISonarQubeService
    {
        readonly int _firstPageIssueCount;
        readonly int _firstTotal;

        public PagingGateway(int firstTotal, int firstPageIssueCount = 2)
        {
            _firstTotal = firstTotal;
            _firstPageIssueCount = firstPageIssueCount;
        }

        public int RequestCount { get; private set; }
        public string? LastIssueType { get; private set; }
        public string? LastSeverity { get; private set; }
        public string? LastIssueStatus { get; private set; }

        public Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
            => Task.FromResult(new SonarRuleDetailsResponse(null));

        public Task<SonarIssuesSearchResponse> SearchIssuesAsync(SearchIssuesQuery query, CancellationToken cancellationToken = default)
        {
            RequestCount += 1;
            LastIssueType = query.Types.FirstOrDefault().Value;
            LastSeverity = query.Severities.FirstOrDefault().Value;
            LastIssueStatus = query.Statuses.FirstOrDefault().Value;

            if (RequestCount == 1)
            {
                var issues = new List<SonarIssueTransport>();

                for (var i = 1; i <= _firstPageIssueCount; i += 1)
                {
                    issues.Add(
                        new SonarIssueTransport(
                            $"sq-{i}",
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null));
                }

                return Task.FromResult(
                    new SonarIssuesSearchResponse(
                        1,
                        500,
                        _firstTotal,
                        issues));
            }

            return Task.FromResult(
                new SonarIssuesSearchResponse(
                    2,
                    500,
                    3,
                    [
                        new SonarIssueTransport(
                            "sq-3",
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null)
                    ]));
        }
    }
}
