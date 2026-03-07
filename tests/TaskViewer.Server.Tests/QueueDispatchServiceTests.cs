using System.Text.Json.Nodes;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class QueueDispatchServiceTests
{
    [Fact]
    public async Task DispatchAsync_CreatesSessionThenPostsPrompt()
    {
        var calls = new List<(string Path, OpenCodeRequest Request)>();

        var sut = new QueueDispatchService(
            (path, request) =>
            {
                calls.Add((path, request));

                if (string.Equals(path, "/session", StringComparison.Ordinal))
                    return Task.FromResult<JsonNode?>(
                        new JsonObject
                        {
                            ["id"] = "sess-123"
                        });

                return Task.FromResult<JsonNode?>(
                    new JsonObject
                    {
                        ["ok"] = true
                    });
            },
            (sessionId, directory) => $"http://opencode.local/session/{sessionId}?dir={directory}");

        var result = await sut.DispatchAsync(
            new QueueItemRecord
            {
                IssueKey = "sq-11",
                Directory = "C:/Work/Alpha",
                IssueType = "CODE_SMELL",
                Severity = "MAJOR",
                Rule = "javascript:S1126",
                IssueStatus = "OPEN",
                RelativePath = "src/file.js",
                Line = 12,
                Message = "Remove this",
                Instructions = "  keep patch tiny  "
            });

        Assert.Equal("sess-123", result.SessionId);
        Assert.Equal("http://opencode.local/session/sess-123?dir=C:/Work/Alpha", result.OpenCodeUrl);
        Assert.Equal(2, calls.Count);
        Assert.Equal("/session", calls[0].Path);
        Assert.Equal("POST", calls[0].Request.Method);
        Assert.Equal("C:/Work/Alpha", calls[0].Request.Directory);
        Assert.Equal("[CODE_SMELL] sq-11", calls[0].Request.JsonBody?["title"]?.ToString());

        Assert.Equal("/session/sess-123/prompt_async", calls[1].Path);
        var text = calls[1].Request.JsonBody?["parts"]?[0]?["text"]?.ToString() ?? string.Empty;
        Assert.Contains("Issue key: sq-11", text);
        Assert.Contains("Issue type: CODE_SMELL", text);
        Assert.Contains("File: src/file.js", text);
        Assert.Contains("Additional instructions:", text);
        Assert.Contains("keep patch tiny", text);
    }

    [Fact]
    public async Task DispatchAsync_ThrowsWhenSessionIdMissing()
    {
        var sut = new QueueDispatchService(
            (_, _) => Task.FromResult<JsonNode?>(new JsonObject()),
            (sessionId, _) => sessionId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DispatchAsync(
            new QueueItemRecord
            {
                IssueKey = "sq-missing",
                Directory = "C:/Work"
            }));

        Assert.Contains("session id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
