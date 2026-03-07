using System.Text.Json.Nodes;

namespace TaskViewer.Server.Tests;

public sealed class SonarOrchestratorQueueTests
{
    [Fact]
    public async Task Tick_CreatesSessionAndMarksItemSessionCreated()
    {
        await using var orchestrator = CreateOrchestrator(async (path, request) =>
        {
            if (path == "/session" &&
                string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return new JsonObject
                {
                    ["id"] = "sess-created-1"
                };

            if (path == "/session/sess-created-1/prompt_async" &&
                string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return new JsonObject
                {
                    ["ok"] = true
                };

            if (path == "/session/status")
                return new JsonObject();

            return null;
        });

        var mapping = await orchestrator.UpsertMapping(
            new JsonObject
            {
                ["sonarProjectKey"] = "gamma-key",
                ["directory"] = "C:/Work/Gamma",
                ["enabled"] = true
            });

        await orchestrator.EnqueueIssues(
            mapping.Id,
            "CODE_SMELL",
            "keep it focused",
            new JsonArray
            {
                new JsonObject
                {
                    ["key"] = "sq-gamma-001",
                    ["type"] = "CODE_SMELL",
                    ["severity"] = "MAJOR",
                    ["rule"] = "javascript:S1126",
                    ["message"] = "Remove this redundant assignment",
                    ["component"] = "gamma-key:src/worker.js",
                    ["line"] = 42
                }
            });

        await orchestrator.Tick();

        var item = await WaitForSingleQueueItem(orchestrator, "session_created", TimeSpan.FromSeconds(5));
        Assert.Equal("sess-created-1", item.SessionId);
        Assert.Contains("/session/sess-created-1", item.OpenCodeUrl);
    }

    [Fact]
    public async Task Tick_FailedDispatch_MarksItemFailed_WhenAttemptsExhausted()
    {
        await using var orchestrator = CreateOrchestrator(
            (path, request) =>
            {
                if (path == "/session" &&
                    string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("OpenCode request failed: injected error");

                return Task.FromResult<JsonNode?>(null);
            },
            1);

        var mapping = await orchestrator.UpsertMapping(
            new JsonObject
            {
                ["sonarProjectKey"] = "gamma-key",
                ["directory"] = "C:/Work/Gamma",
                ["enabled"] = true
            });

        await orchestrator.EnqueueIssues(
            mapping.Id,
            "CODE_SMELL",
            null,
            new JsonArray
            {
                new JsonObject
                {
                    ["key"] = "sq-gamma-err",
                    ["component"] = "gamma-key:src/fail.js"
                }
            });

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
        await using var orchestrator = CreateOrchestrator((_, _) => Task.FromResult<JsonNode?>(
            new JsonObject
            {
                ["id"] = "unused"
            }));

        var mapping = await orchestrator.UpsertMapping(
            new JsonObject
            {
                ["sonarProjectKey"] = "gamma-key",
                ["directory"] = "C:/Work/Gamma",
                ["enabled"] = true
            });

        await orchestrator.EnqueueIssues(
            mapping.Id,
            "CODE_SMELL",
            null,
            new JsonArray
            {
                new JsonObject
                {
                    ["key"] = "sq-gamma-cancel",
                    ["component"] = "gamma-key:src/cancel.js"
                }
            });

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
        await using var orchestrator = CreateOrchestrator((_, _) => Task.FromResult<JsonNode?>(null));

        var mapping = await orchestrator.UpsertMapping(
            new JsonObject
            {
                ["sonarProjectKey"] = "gamma-key",
                ["directory"] = "C:/Work/Gamma",
                ["enabled"] = true
            });

        var first = await orchestrator.EnqueueIssues(
            mapping.Id,
            "CODE_SMELL",
            null,
            new JsonArray
            {
                new JsonObject
                {
                    ["key"] = "sq-gamma-dupe",
                    ["component"] = "gamma-key:src/dupe.js"
                }
            });

        var firstCreated = (int)(first.GetType().GetProperty("created")?.GetValue(first) ?? 0);
        Assert.Equal(1, firstCreated);

        var second = await orchestrator.EnqueueIssues(
            mapping.Id,
            "CODE_SMELL",
            null,
            new JsonArray
            {
                new JsonObject(),
                new JsonObject
                {
                    ["key"] = "sq-gamma-dupe",
                    ["component"] = "gamma-key:src/dupe.js"
                }
            });

        var created = (int)(second.GetType().GetProperty("created")?.GetValue(second) ?? 0);
        var skipped = (IEnumerable<object>?)second.GetType().GetProperty("skipped")?.GetValue(second);

        Assert.Equal(0, created);
        Assert.NotNull(skipped);
        Assert.Equal(2, skipped!.Count());
        Assert.Contains(skipped, item => (item.GetType().GetProperty("reason")?.GetValue(item)?.ToString() ?? string.Empty) == "invalid-issue");
        Assert.Contains(skipped, item => (item.GetType().GetProperty("reason")?.GetValue(item)?.ToString() ?? string.Empty).StartsWith("already-", StringComparison.Ordinal));
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
        Func<string, OpenCodeRequest, Task<JsonNode?>> openCodeFetch,
        int maxAttempts = 2)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-orch-queue-{Guid.NewGuid():N}.sqlite");

        return new SonarOrchestrator(
            new SonarOrchestratorOptions
            {
                SonarUrl = "http://sonar.local",
                SonarToken = "token",
                DbPath = dbPath,
                MaxActive = 1,
                PollMs = 1000,
                MaxAttempts = maxAttempts,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeFetch = openCodeFetch,
                NormalizeDirectory = directory => directory,
                BuildOpenCodeSessionUrl = (sessionId, _) => $"http://opencode.local/session/{sessionId}",
                OnChange = () => { }
            });
    }
}
