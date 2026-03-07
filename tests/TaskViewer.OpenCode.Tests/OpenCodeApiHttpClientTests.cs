using System.Net;
using System.Text;

namespace TaskViewer.OpenCode.Tests;

public sealed class OpenCodeApiHttpClientTests
{
    [Fact]
    public async Task ReadTodosAsync_ParsesAndNormalizesTodoFields()
    {
        HttpRequestMessage? capturedRequest = null;

        using var handler = new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    [
                      {"text":"ship tests","state":"in progress","priority":"p2"},
                      {"title":"cleanup","status":"done","priority":"P3"}
                    ]
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var client = CreateClient(handler);

        var todos = await client.ReadTodosAsync("session-1", "C:/Work");

        Assert.Equal(2, todos.Count);
        Assert.Equal("ship tests", todos[0].Content);
        Assert.Equal("in_progress", todos[0].Status);
        Assert.Equal("medium", todos[0].Priority);
        Assert.Equal("cleanup", todos[1].Content);
        Assert.Equal("completed", todos[1].Status);
        Assert.Equal("low", todos[1].Priority);
        Assert.NotNull(capturedRequest);
        Assert.Equal("/session/session-1/todo", capturedRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("directory=C%3A%2FWork", capturedRequest.RequestUri.Query);
    }

    [Fact]
    public async Task ReadMessagesAsync_ParsesNestedTextAndTimestamp()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "info": {
                        "role": "assistant",
                        "time": {
                          "created": 1710000000000
                        }
                      },
                      "parts": [
                        {
                          "type": "text",
                          "text": "hello from OpenCode"
                        }
                      ]
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var client = CreateClient(handler);

        var messages = await client.ReadMessagesAsync("session-2", limit: 5);

        var message = Assert.Single(messages);
        Assert.Equal("assistant", message.Role);
        Assert.Equal("hello from OpenCode", message.Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1710000000000), message.CreatedAt);
    }

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    static OpenCodeApiHttpClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), new OpenCodeClientOptions("http://localhost:4096", "opencode", string.Empty));
}
