using System.Net;
using System.Text;

namespace TaskViewer.SonarQube.Tests;

public sealed class SonarQubeServiceTests
{
    [Fact]
    public async Task SearchIssuesAsync_BuildsQueryAndParsesJson()
    {
        HttpRequestMessage? capturedRequest = null;

        using var handler = new StubHandler(request =>
        {
            capturedRequest = request;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"paging\":{\"pageIndex\":3,\"pageSize\":25,\"total\":1},\"issues\":[{\"key\":\"sq-1\"}]}", Encoding.UTF8, "application/json")
            };
        });

        var gateway = CreateGateway(handler);

        var response = await gateway.SearchIssuesAsync(
            new SearchIssuesQuery
            {
                ComponentKey = "alpha",
                Types = ["CODE_SMELL"],
                PageIndex = 1,
                PageSize = 50
            });

        Assert.Equal(3, response.PageIndex);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(1, response.Total);
        Assert.Single(response.Issues);
        Assert.Equal("sq-1", response.Issues[0].Key);
        Assert.NotNull(capturedRequest);
        Assert.Contains("componentKeys=alpha", capturedRequest!.RequestUri!.Query);
        Assert.Equal("Basic", capturedRequest.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task SearchIssuesAsync_Throws_WhenHttpStatusIsFailure()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("denied", Encoding.UTF8, "text/plain")
        });

        var gateway = CreateGateway(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.SearchIssuesAsync(new SearchIssuesQuery { ComponentKey = "alpha", PageIndex = 1, PageSize = 100 }));
        Assert.Contains("SonarQube request failed", error.Message);
    }

    [Fact]
    public async Task GetRuleAsync_ParsesRuleName()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"rule\":{\"name\":\" Cognitive Complexity \"}}", Encoding.UTF8, "application/json")
        });

        var gateway = CreateGateway(handler);

        var response = await gateway.GetRuleAsync("javascript:S3776");

        Assert.Equal("Cognitive Complexity", response.Name);
    }

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(_handler(request));
    }

    static SonarQubeService CreateGateway(HttpMessageHandler handler)
        => new(() => new SonarQubeHttpClient(new HttpClient(handler), new SonarQubeClientOptions("http://sonar.local", "secret")));
}
