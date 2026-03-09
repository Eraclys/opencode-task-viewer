using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using TaskViewer.Application.Sessions;
using TaskViewer.Infrastructure.OpenCode;
using TaskViewer.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class OpenCodeEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_TodoUpdatedForSession_InvalidatesTodoCacheAndBroadcastsSessionUpdate()
    {
        var cacheCoordinator = new OpenCodeViewerCacheCoordinator(new OpenCodeViewerState(), new OpenCodeViewerCachePolicy());
        cacheCoordinator.StoreTodos("C:/Work", "sess-1", [new SessionTodoDto("todo-1", "open", "task")], DateTimeOffset.UtcNow);

        using var responseBody = new MemoryStream();
        var (hub, client, cts) = CreateSseHub(responseBody);
        var sut = new OpenCodeEventHandler(cacheCoordinator, hub, new OpenCodeCacheInvalidationPolicy());

        await sut.HandleAsync(
            new OpenCodeSseEvent
            {
                Directory = "C:/Work",
                Payload = new OpenCodeSsePayload
                {
                    Type = "todo.updated",
                    Properties = new OpenCodeSseProperties
                    {
                        LegacySessionId = "sess-1"
                    }
                }
            });

        await DrainClientAsync(client, cts);

        Assert.False(cacheCoordinator.TryGetFreshTodos("C:/Work", "sess-1", out _));
        var payload = ReadBroadcastPayload(responseBody);
        Assert.Equal("update", payload?.Type);
        Assert.Equal("sess-1", payload?.SessionId);
    }

    [Fact]
    public async Task HandleAsync_SessionStatus_StoresOverrideAndInvalidatesTaskOverview()
    {
        var cacheCoordinator = new OpenCodeViewerCacheCoordinator(new OpenCodeViewerState(), new OpenCodeViewerCachePolicy());
        cacheCoordinator.StoreAllTasks(
            [
                new GlobalViewerTaskDto
                {
                    Id = "task-1",
                    Subject = "todo",
                    Status = "open",
                    SessionId = "sess-1"
                }
            ],
            DateTimeOffset.UtcNow);

        using var responseBody = new MemoryStream();
        var (hub, client, cts) = CreateSseHub(responseBody);
        var sut = new OpenCodeEventHandler(cacheCoordinator, hub, new OpenCodeCacheInvalidationPolicy());

        await sut.HandleAsync(
            new OpenCodeSseEvent
            {
                Directory = "C:/Work",
                Payload = new OpenCodeSsePayload
                {
                    Type = "session.status",
                    Properties = new OpenCodeSseProperties
                    {
                        LegacySessionId = "sess-1",
                        Status = new OpenCodeSseStatus
                        {
                            Type = "working"
                        }
                    }
                }
            });

        await DrainClientAsync(client, cts);

        Assert.True(cacheCoordinator.TryGetRecentStatusOverride("C:/Work", "sess-1", out var type));
        Assert.Equal("working", type);
        Assert.Null(cacheCoordinator.GetFreshAllTasks());
        var payload = ReadBroadcastPayload(responseBody);
        Assert.Equal("sess-1", payload?.SessionId);
    }

    [Fact]
    public async Task HandleAsync_MessageUpdated_ClearsAssistantPresenceAndBroadcastsGlobalUpdate()
    {
        var cacheCoordinator = new OpenCodeViewerCacheCoordinator(new OpenCodeViewerState(), new OpenCodeViewerCachePolicy());
        cacheCoordinator.CompleteAssistantPresenceLookup("sess-1", true, DateTimeOffset.UtcNow);

        using var responseBody = new MemoryStream();
        var (hub, client, cts) = CreateSseHub(responseBody);
        var sut = new OpenCodeEventHandler(cacheCoordinator, hub, new OpenCodeCacheInvalidationPolicy());

        await sut.HandleAsync(
            new OpenCodeSseEvent
            {
                Payload = new OpenCodeSsePayload
                {
                    Type = "message.updated"
                }
            });

        await DrainClientAsync(client, cts);

        Assert.False(cacheCoordinator.TryGetFreshAssistantPresence("sess-1", out _));
        var payload = ReadBroadcastPayload(responseBody);
        Assert.Equal("update", payload?.Type);
        Assert.Null(payload?.SessionId);
    }

    static async Task DrainClientAsync(SseClient client, CancellationTokenSource cts)
    {
        await Task.Delay(50);
        cts.Cancel();
        await client.Completion;
    }

    static (ISseHub Hub, SseClient Client, CancellationTokenSource Cts) CreateSseHub(Stream responseBody)
    {
        var hub = new SseHub();
        var context = new DefaultHttpContext();
        context.Response.Body = responseBody;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var client = hub.AddClient(context.Response, cts.Token);
        return (hub, client, cts);
    }

    static BroadcastPayload? ReadBroadcastPayload(MemoryStream responseBody)
    {
        var text = Encoding.UTF8.GetString(responseBody.ToArray());
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single(x => x.StartsWith("data: ", StringComparison.Ordinal));
        return JsonSerializer.Deserialize<BroadcastPayload>(line[6..]);
    }

    sealed class BroadcastPayload
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("sessionId")]
        public string? SessionId { get; init; }
    }
}
