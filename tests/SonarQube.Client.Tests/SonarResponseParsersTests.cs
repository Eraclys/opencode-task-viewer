namespace SonarQube.Client.Tests;

public sealed class SonarResponseParsersTests
{
    [Fact]
    public void ParseIssuesSearchResponse_ReturnsFallbackPagingForMalformedPayload()
    {
        var response = SonarResponseParsers.ParseIssuesSearchResponse("{not-json}", 4, 25);

        Assert.Equal(4, response.PageIndex);
        Assert.Equal(25, response.PageSize);
        Assert.Null(response.Total);
        Assert.Empty(response.Issues);
    }

    [Fact]
    public void ParseIssuesSearchResponse_ParsesPagingAndIssues()
    {
        var payload =
            """
            {
              "paging": {
                "pageIndex": 3,
                "pageSize": 50,
                "total": 7
              },
              "issues": [
                {
                  "key": "sq-1",
                  "rule": "csharpsquid:S100",
                  "message": "Rename this method",
                  "status": "OPEN"
                }
              ]
            }
            """;

        var response = SonarResponseParsers.ParseIssuesSearchResponse(payload, 1, 100);

        Assert.Equal(3, response.PageIndex);
        Assert.Equal(50, response.PageSize);
        Assert.Equal(7, response.Total);
        var issue = Assert.Single(response.Issues);
        Assert.Equal("sq-1", issue.Key);
        Assert.Equal("csharpsquid:S100", issue.Rule);
        Assert.Equal("Rename this method", issue.Message);
        Assert.Equal("OPEN", issue.Status);
    }

    [Fact]
    public void ParseRuleDetails_TrimsRuleName()
    {
        var payload = """{ "rule": { "name": " Cognitive Complexity " } }""";

        var response = SonarResponseParsers.ParseRuleDetails(payload);

        Assert.Equal("Cognitive Complexity", response.Name);
    }
}
