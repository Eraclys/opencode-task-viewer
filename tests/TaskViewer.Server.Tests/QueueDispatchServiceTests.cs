using TaskViewer.Domain.Orchestration;
using TaskViewer.Infrastructure.Persistence;
using TaskViewer.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class QueueDispatchServiceTests
{
    [Fact]
    public async Task DispatchAsync_CreatesSessionThenPostsPrompt()
    {
        var client = new FakeOpenCodeService();

        var sut = new QueueDispatchService(
            client,
            (sessionId, directory) => $"http://opencode.local/session/{sessionId}?dir={directory}");

        var result = await sut.DispatchAsync(
            new QueueItemRecord
            {
                TaskKey = "alpha::main::src/file.js::javascript:S1126",
                TaskUnit = "project+file+rule",
                IssueKey = "sq-11",
                IssueCount = 1,
                Directory = "C:/Work/Alpha",
                SonarProjectKey = "alpha",
                IssueType = "CODE_SMELL",
                Severity = "MAJOR",
                Rule = "javascript:S1126",
                IssueStatus = "OPEN",
                RelativePath = "src/file.js",
                Line = 12,
                Message = "Remove this",
                Instructions = "  keep patch tiny  "
            },
            [
                new NormalizedIssue
                {
                    Key = "sq-11",
                    Type = "CODE_SMELL",
                    Rule = "javascript:S1126",
                    Message = "Remove this",
                    RelativePath = "src/file.js",
                    AbsolutePath = "C:/Work/Alpha/src/file.js",
                    Line = 12,
                    Status = "OPEN"
                }
            ]);

        Assert.Equal("sess-123", result.SessionId);
        Assert.Equal("http://opencode.local/session/sess-123?dir=C:/Work/Alpha", result.OpenCodeUrl);
        var createCall = Assert.Single(client.CreateCalls);
        var promptCall = Assert.Single(client.PromptCalls);
        Assert.Equal("C:/Work/Alpha", createCall.Directory);
        Assert.Equal("[CODE_SMELL] javascript:S1126 :: src/file.js", createCall.Title);
        Assert.Equal("C:/Work/Alpha", promptCall.Directory);
        Assert.Equal("sess-123", promptCall.SessionId);
        var text = promptCall.Prompt;
        Assert.Contains("Task key: alpha::main::src/file.js::javascript:S1126", text);
        Assert.Contains("Issue count: 1", text);
        Assert.Contains("Primary file: src/file.js", text);
        Assert.Contains("Additional instructions:", text);
        Assert.Contains("keep patch tiny", text);
    }

    [Fact]
    public async Task DispatchAsync_ThrowsWhenSessionIdMissing()
    {
        var sut = new QueueDispatchService(
            new MissingSessionIdOpenCodeService(),
            (sessionId, _) => sessionId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DispatchAsync(
            new QueueItemRecord
            {
                IssueKey = "sq-missing",
                Directory = "C:/Work"
            },
            []));

        Assert.Contains("session id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    sealed class FakeOpenCodeService : IOpenCodeService
    {
        public List<(string Directory, string Title)> CreateCalls { get; } = [];

        public List<(string Directory, string SessionId, string Prompt)> PromptCalls { get; } = [];

        public string? BuildSessionUrl(string sessionId, string? directory) => null;

        public Task<Dictionary<string, string>> ReadWorkingStatusMapAsync(string directory, CancellationToken cancellationToken = default)
            => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal));

        public Task<List<OpenCodeTodo>> ReadTodosAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<OpenCodeTodo>());

        public Task<List<OpenCodeMessage>> ReadMessagesAsync(string sessionId, int? limit = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<OpenCodeMessage>());

        public Task<List<OpenCodeProject>> ReadProjectsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<OpenCodeProject>());

        public Task<List<OpenCodeSession>> ReadSessionsAsync(string directory, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<OpenCodeSession>());

        public Task<OpenCodeSession?> ReadSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
            => Task.FromResult<OpenCodeSession?>(null);

        public Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
            => Task.FromResult<DateTimeOffset?>(null);

        public Task<string> CreateSessionAsync(string directory, string title, CancellationToken cancellationToken = default)
        {
            CreateCalls.Add((directory, title));
            return Task.FromResult("sess-123");
        }

        public Task SendPromptAsync(string directory, string sessionId, string prompt, CancellationToken cancellationToken = default)
        {
            PromptCalls.Add((directory, sessionId, prompt));
            return Task.CompletedTask;
        }
    }

    sealed class MissingSessionIdOpenCodeService : DisabledOpenCodeService
    {
        public override Task<string> CreateSessionAsync(string directory, string title, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("OpenCode did not return a session id");
    }
}
