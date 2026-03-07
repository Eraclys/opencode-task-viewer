using System.Net;
using System.Text;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class HttpSonarGatewayTests
{
    [Fact]
    public async Task Fetch_BuildsQueryAndParsesJson()
    {
        HttpRequestMessage? capturedRequest = null;

        using var handler = new StubHandler(request =>
        {
            capturedRequest = request;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var gateway = new HttpSonarGateway("http://sonar.local", "secret", httpClient);

        var json = await gateway.Fetch(
            "/api/issues/search",
            new Dictionary<string, string?>
            {
                ["componentKeys"] = "alpha",
                ["types"] = "CODE_SMELL"
            });

        Assert.Equal("true", json?["ok"]?.ToString());
        Assert.NotNull(capturedRequest);
        Assert.Contains("componentKeys=alpha", capturedRequest!.RequestUri!.Query);
        Assert.Equal("Basic", capturedRequest.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task Fetch_Throws_WhenHttpStatusIsFailure()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("denied", Encoding.UTF8, "text/plain")
        });

        using var httpClient = new HttpClient(handler);
        var gateway = new HttpSonarGateway("http://sonar.local", "secret", httpClient);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.Fetch("/api/issues/search", new Dictionary<string, string?>()));
        Assert.Contains("SonarQube request failed", error.Message);
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
}
