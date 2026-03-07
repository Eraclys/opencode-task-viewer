using TaskViewer.OpenCode;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class QueueDispatchServiceTests
{
    [Fact]
    public async Task DispatchAsync_CreatesSessionThenPostsPrompt()
    {
        var client = new FakeOpenCodeDispatchClient();

        var sut = new QueueDispatchService(
            client,
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
        var createCall = Assert.Single(client.CreateCalls);
        var promptCall = Assert.Single(client.PromptCalls);
        Assert.Equal("C:/Work/Alpha", createCall.Directory);
        Assert.Equal("[CODE_SMELL] sq-11", createCall.Title);
        Assert.Equal("C:/Work/Alpha", promptCall.Directory);
        Assert.Equal("sess-123", promptCall.SessionId);
        var text = promptCall.Prompt;
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
            new MissingSessionIdDispatchClient(),
            (sessionId, _) => sessionId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DispatchAsync(
            new QueueItemRecord
            {
                IssueKey = "sq-missing",
                Directory = "C:/Work"
            }));

        Assert.Contains("session id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    sealed class FakeOpenCodeDispatchClient : IOpenCodeDispatchClient
    {
        public List<(string Directory, string Title)> CreateCalls { get; } = [];

        public List<(string Directory, string SessionId, string Prompt)> PromptCalls { get; } = [];

        public Task<string> CreateSessionAsync(string directory, string title)
        {
            CreateCalls.Add((directory, title));
            return Task.FromResult("sess-123");
        }

        public Task SendPromptAsync(string directory, string sessionId, string prompt)
        {
            PromptCalls.Add((directory, sessionId, prompt));
            return Task.CompletedTask;
        }
    }

    sealed class MissingSessionIdDispatchClient : IOpenCodeDispatchClient
    {
        public Task<string> CreateSessionAsync(string directory, string title)
            => throw new InvalidOperationException("OpenCode did not return a session id");

        public Task SendPromptAsync(string directory, string sessionId, string prompt)
            => Task.CompletedTask;
    }
}
