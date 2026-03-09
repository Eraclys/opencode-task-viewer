using System.Net;
using System.Text;

namespace TaskViewer.SonarQube.Tests;

public sealed class SonarQubeHttpClientTests
{
    [Fact]
    public async Task SearchIssuesAsync_BuildsQueryAndAuthenticationHeader()
    {
        HttpRequestMessage? capturedRequest = null;

        using var handler = new StubHandler(request =>
        {
            capturedRequest = request;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{" +
                    "\"paging\":{\"pageIndex\":2,\"pageSize\":25,\"total\":1}," +
                    "\"issues\":[{\"key\":\"sq-1\",\"rule\":\"csharpsquid:S100\"}]" +
                    "}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var client = CreateClient(handler);

        var response = await client.SearchIssuesAsync(
            new SearchIssuesQuery
            {
                ComponentKey = "alpha",
                Types = ["CODE_SMELL"],
                PageIndex = 1,
                PageSize = 100
            });

        Assert.Equal(2, response.PageIndex);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(1, response.Total);
        Assert.Single(response.Issues);
        Assert.NotNull(capturedRequest);
        Assert.Contains("componentKeys=alpha", capturedRequest!.RequestUri!.Query);
        Assert.Equal("Basic", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("secret:")), capturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SearchIssuesAsync_UsesQueryPagingAsFallbackAndSerializesIssueKeys()
    {
        HttpRequestMessage? capturedRequest = null;

        using var handler = new StubHandler(request =>
        {
            capturedRequest = request;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"issues\":[{\"key\":\"sq-1\"}]}", Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);

        var response = await client.SearchIssuesAsync(
            new SearchIssuesQuery
            {
                ComponentKey = "alpha",
                Branch = "main",
                PageIndex = 3,
                PageSize = 7,
                IssueKeys = ["sq-1", "sq-2"]
            });

        Assert.Equal(3, response.PageIndex);
        Assert.Equal(7, response.PageSize);
        Assert.NotNull(capturedRequest);
        Assert.Contains("branch=main", capturedRequest!.RequestUri!.Query);
        Assert.Contains("issues=sq-1%2csq-2", capturedRequest.RequestUri.Query);
    }

    [Fact]
    public async Task GetRuleAsync_ThrowsWhenRequestFails()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("denied", Encoding.UTF8, "text/plain")
        });

        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetRuleAsync("csharpsquid:S100"));

        Assert.Contains("SonarQube request failed", error.Message);
    }

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    static SonarQubeHttpClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), new SonarQubeClientOptions("http://sonar.local", "secret"));
}
