using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using TaskViewer.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class OpenCodeUpstreamSseServiceTests
{
    [Fact]
    public async Task RunAsync_ProcessesValidJsonEvent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        JsonNode? captured = null;
        var service = CreateService(
            CreateHandler(
                _ => CreateResponse(
                    "data: {\"payload\":{\"type\":\"todo.updated\"},\"directory\":\"C:/Work\"}\n\n")));

        await service.RunAsync(
            evt =>
            {
                captured = evt;
                cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token);

        Assert.Equal("todo.updated", captured?["payload"]?["type"]?.ToString());
        Assert.Equal("C:/Work", captured?["directory"]?.ToString());
    }

    [Fact]
    public async Task RunAsync_IgnoresMalformedPayloadBeforeValidEvent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new List<string>();
        var service = CreateService(
            CreateHandler(
                _ => CreateResponse(
                    "data: {not-json}\n\n" +
                    "data: {\"payload\":{\"type\":\"session.updated\"}}\n\n")));

        await service.RunAsync(
            evt =>
            {
                received.Add(evt["payload"]?["type"]?.ToString() ?? string.Empty);
                cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token);

        Assert.Equal(["session.updated"], received);
    }

    [Fact]
    public async Task RunAsync_AppliesBasicAuthenticationWhenPasswordConfigured()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        AuthenticationHeaderValue? authorization = null;
        var service = CreateService(
            CreateHandler(
                request =>
                {
                    authorization = request.Headers.Authorization;
                    return CreateResponse("data: {\"payload\":{\"type\":\"message.updated\"}}\n\n");
                }),
            password: "secret");

        await service.RunAsync(
            _ =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token);

        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:secret")), authorization.Parameter);
    }

    static OpenCodeUpstreamSseService CreateService(HttpMessageHandler handler, string password = "")
    {
        return new OpenCodeUpstreamSseService(
            () => new OpenCodeSseHttpClient(new HttpClient(handler), new OpenCodeClientOptions("http://localhost:4096", "opencode", password)));
    }

    static HttpMessageHandler CreateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => new StubHttpMessageHandler(responder);

    static HttpResponseMessage CreateResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
        };
    }

    sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
