using System.Net;
using System.Text;

namespace TaskViewer.OpenCode.Tests;

public sealed class OpenCodeHttpClientTests
{
    [Fact]
    public async Task ReadWorkingStatusMapAsync_ParsesTypedStatusPayload()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "session-a": { "type": " Working " },
                  "session-b": { "type": "COMPLETED" },
                  "session-c": { "ignored": true }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var client = CreateClient(handler);

        var statuses = await client.ReadWorkingStatusMapAsync("C:/Work");

        Assert.Equal(2, statuses.Count);
        Assert.Equal("working", statuses["session-a"].Type);
        Assert.Equal("completed", statuses["session-b"].Type);
    }

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

    [Fact]
    public async Task ReadProjectsAsync_ParsesWorktreeAndNestedSandboxes()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "worktree": "C:/Work/Main/",
                      "sandboxes": [
                        "C:/Work/SandboxA/",
                        { "directory": "C:/Work/SandboxB/" },
                        { "root": "C:/Work/SandboxC/" }
                      ]
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var client = CreateClient(handler);

        var project = Assert.Single(await client.ReadProjectsAsync());

        Assert.Equal("C:/Work/Main", project.Worktree);
        Assert.Equal(["C:/Work/SandboxA", "C:/Work/SandboxB", "C:/Work/SandboxC"], project.SandboxDirectories);
    }

    [Fact]
    public async Task ReadSessionAsync_ParsesTypedSessionTransport()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "id": " session-9 ",
                  "name": "Fix parser",
                  "project": {
                    "worktree": "C:/Work/Repo/"
                  },
                  "time": {
                    "created": 1710000000000,
                    "updated": "2024-03-10T12:30:00Z",
                    "archived": 1710000005000
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var client = CreateClient(handler);

        var session = await client.ReadSessionAsync("session-9", "C:/Fallback/");

        Assert.NotNull(session);
        Assert.Equal("session-9", session!.Id);
        Assert.Equal("Fix parser", session.Name);
        Assert.Equal("C:/Fallback", session.Directory);
        Assert.Equal("C:/Work/Repo", session.Project);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1710000000000), session.CreatedAt);
        Assert.Equal(DateTimeOffset.Parse("2024-03-10T12:30:00Z"), session.UpdatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1710000005000), session.ArchivedAt);
    }

    [Fact]
    public async Task CreateSessionAsync_ParsesTypedCreateResponse()
    {
        HttpRequestMessage? capturedRequest = null;

        using var handler = new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "id": " created-1 " }""", Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);

        var sessionId = await client.CreateSessionAsync("C:/Work", "Typed session");

        Assert.Equal("created-1", sessionId);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/session", capturedRequest.RequestUri!.AbsolutePath);
    }

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    static OpenCodeHttpClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), new OpenCodeClientOptions("http://localhost:4096", "opencode", string.Empty));
}
