using TaskViewer.Domain.Orchestration;
using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.Persistence;
using TaskViewer.OpenCode;
using TaskViewer.Persistence;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Tests;

public sealed class SonarOrchestratorQueueTests
{
    const string GammaDirectory = "C:/Temp/TaskViewerTests/Gamma";

    [Fact]
    public async Task Tick_CreatesSessionAndMarksItemSessionCreated()
    {
        Directory.CreateDirectory(GammaDirectory);
        Directory.CreateDirectory(Path.Combine(GammaDirectory, ".git"));
        Directory.CreateDirectory(Path.Combine(GammaDirectory, "src"));
        File.WriteAllText(Path.Combine(GammaDirectory, "src", "worker.js"), "// worker\n");

        await using var orchestrator = CreateOrchestrator((path, request) =>
        {
            if (path == "/session" &&
                string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<string?>("""{ "id": "sess-created-1" }""");

            if (path == "/session/sess-created-1/prompt_async" &&
                string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<string?>("""{ "ok": true }""");

            if (path == "/session/status")
                return Task.FromResult<string?>("{}");

            return Task.FromResult<string?>(null);
        });

        var mapping = await orchestrator.UpsertMapping(
            new UpsertMappingRequest(
                Id: null,
                SonarProjectKey: "gamma-key",
                Directory: GammaDirectory,
                Branch: null,
                Enabled: true));

        await orchestrator.EnqueueIssues(
            new EnqueueIssuesRequest(
                MappingId: mapping.Id,
                IssueType: "CODE_SMELL",
                Instructions: "keep it focused",
                Issues:
                [
                    new SonarIssueTransport(
                        "sq-gamma-001",
                        null,
                        "CODE_SMELL",
                        null,
                        "MAJOR",
                        "javascript:S1126",
                        "Remove this redundant assignment",
                        "gamma-key:src/worker.js",
                        null,
                        "42",
                        null)
                ]));

        await orchestrator.Tick();

        var item = await WaitForSingleQueueItem(orchestrator, "running", TimeSpan.FromSeconds(5));
        Assert.Equal("sess-created-1", item.SessionId);
        Assert.Contains("/session/sess-created-1", item.OpenCodeUrl);
    }

    [Fact]
    public async Task Tick_FailedDispatch_MarksItemFailed_WhenAttemptsExhausted()
    {
        Directory.CreateDirectory(GammaDirectory);
        Directory.CreateDirectory(Path.Combine(GammaDirectory, ".git"));
        Directory.CreateDirectory(Path.Combine(GammaDirectory, "src"));
        File.WriteAllText(Path.Combine(GammaDirectory, "src", "fail.js"), "// fail\n");

        await using var orchestrator = CreateOrchestrator(
            (path, request) =>
            {
                if (path == "/session" &&
                    string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("OpenCode request failed: injected error");

                return Task.FromResult<string?>(null);
            },
            1);

        var mapping = await orchestrator.UpsertMapping(
            new UpsertMappingRequest(
                Id: null,
                SonarProjectKey: "gamma-key",
                Directory: GammaDirectory,
                Branch: null,
                Enabled: true));

        await orchestrator.EnqueueIssues(
            new EnqueueIssuesRequest(
                MappingId: mapping.Id,
                IssueType: "CODE_SMELL",
                Instructions: null,
                Issues:
                [
                    new SonarIssueTransport("sq-gamma-err", null, null, null, null, null, null, "gamma-key:src/fail.js", null, null, null)
                ]));

        await orchestrator.Tick();

        var item = await WaitForSingleQueueItem(orchestrator, "failed", TimeSpan.FromSeconds(5));
        Assert.Contains("injected error", item.LastError);

        var retried = await orchestrator.RetryFailed();
        Assert.Equal(1, retried);

        var queuedAgain = await WaitForSingleQueueItem(orchestrator, "queued", TimeSpan.FromSeconds(2));
        Assert.Equal("queued", queuedAgain.State);
    }

    [Fact]
    public async Task CancelQueueItem_CancelsQueuedItem_AndPreventsDispatch()
    {
        Directory.CreateDirectory(GammaDirectory);
        Directory.CreateDirectory(Path.Combine(GammaDirectory, ".git"));
        Directory.CreateDirectory(Path.Combine(GammaDirectory, "src"));
        File.WriteAllText(Path.Combine(GammaDirectory, "src", "cancel.js"), "// cancel\n");

        await using var orchestrator = CreateOrchestrator((_, _) => Task.FromResult<string?>("""{ "id": "unused" }"""));

        var mapping = await orchestrator.UpsertMapping(
            new UpsertMappingRequest(
                Id: null,
                SonarProjectKey: "gamma-key",
                Directory: GammaDirectory,
                Branch: null,
                Enabled: true));

        await orchestrator.EnqueueIssues(
            new EnqueueIssuesRequest(
                MappingId: mapping.Id,
                IssueType: "CODE_SMELL",
                Instructions: null,
                Issues:
                [
                    new SonarIssueTransport("sq-gamma-cancel", null, null, null, null, null, null, "gamma-key:src/cancel.js", null, null, null)
                ]));

        var queued = await WaitForSingleQueueItem(orchestrator, "queued", TimeSpan.FromSeconds(2));
        var cancelled = await orchestrator.CancelQueueItem(queued.Id);
        Assert.True(cancelled);

        await orchestrator.Tick();

        var final = await WaitForSingleQueueItem(orchestrator, "cancelled", TimeSpan.FromSeconds(2));
        Assert.Equal("cancelled", final.State);
    }

    [Fact]
    public async Task EnqueueIssues_SkipsInvalidAndDuplicateIssues()
    {
        Directory.CreateDirectory(GammaDirectory);
        Directory.CreateDirectory(Path.Combine(GammaDirectory, ".git"));
        Directory.CreateDirectory(Path.Combine(GammaDirectory, "src"));
        File.WriteAllText(Path.Combine(GammaDirectory, "src", "dupe.js"), "// dupe\n");

        await using var orchestrator = CreateOrchestrator((_, _) => Task.FromResult<string?>(null));

        var mapping = await orchestrator.UpsertMapping(
            new UpsertMappingRequest(
                Id: null,
                SonarProjectKey: "gamma-key",
                Directory: GammaDirectory,
                Branch: null,
                Enabled: true));

        var first = await orchestrator.EnqueueIssues(
            new EnqueueIssuesRequest(
                MappingId: mapping.Id,
                IssueType: "CODE_SMELL",
                Instructions: null,
                Issues:
                [
                    new SonarIssueTransport("sq-gamma-dupe", null, null, null, null, null, null, "gamma-key:src/dupe.js", null, null, null)
                ]));

        var firstCreated = first.Created;
        Assert.Equal(1, firstCreated);

        var second = await orchestrator.EnqueueIssues(
            new EnqueueIssuesRequest(
                MappingId: mapping.Id,
                IssueType: "CODE_SMELL",
                Instructions: null,
                Issues:
                [
                    new SonarIssueTransport(null, null, null, null, null, null, null, null, null, null, null),
                    new SonarIssueTransport("sq-gamma-dupe", null, null, null, null, null, null, "gamma-key:src/dupe.js", null, null, null)
                ]));

        var created = second.Created;
        var skipped = second.Skipped;

        Assert.Equal(0, created);
        Assert.Equal(2, skipped.Count);
        Assert.Contains(skipped, item => item.Reason == "invalid-issue");
        Assert.Contains(skipped, item => item.Reason.StartsWith("already-", StringComparison.Ordinal));
    }

    static async Task<QueueItemRecord> WaitForSingleQueueItem(SonarOrchestrator orchestrator, string expectedState, TimeSpan timeout)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            var items = await orchestrator.ListQueue(expectedState, 10);

            if (items.Count > 0)
                return items[0];

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for queue state '{expectedState}'");
    }

    static SonarOrchestrator CreateOrchestrator(
        Func<string, OpenCodeRequest, Task<string?>> openCodeFetch,
        int maxAttempts = 2)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-orch-queue-{Guid.NewGuid():N}.sqlite");

        return new SonarOrchestrator(
            new SonarOrchestratorOptions
            {
                SonarUrl = "http://sonar.local",
                SonarToken = "token",
                DbPath = dbPath,
                Persistence = new SqliteOrchestrationPersistence(dbPath, () => { }),
                MaxActive = 1,
                PerProjectMaxActive = 1,
                PollMs = 1000,
                LeaseSeconds = 180,
                MaxAttempts = maxAttempts,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeApiClient = new DelegateOpenCodeService(openCodeFetch),
                TaskReadinessGate = new TestTaskReadinessGate(),
                NormalizeDirectory = directory => directory,
                BuildOpenCodeSessionUrl = (sessionId, _) => $"http://opencode.local/session/{sessionId}",
                OnChange = () => { }
            });
    }
}
