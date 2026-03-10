using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace SonarQube.OpenCodeTaskViewer.E2E.Tests;

[Collection(E2eCollection.Name)]
public sealed class OrchestrationTests
{
    readonly E2eEnvironmentFixture _fixture;

    public OrchestrationTests(E2eEnvironmentFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OrchestrationFlowQueuesPerIssueAndCreatesOpenCodeSessions()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await LoadCodeSmellIssuesAsync(page);

            var firstIssue = page.Locator(".orch-issue-row").First;
            await firstIssue.Locator("input[type=\"checkbox\"]").CheckAsync();

            await page.GetByTestId("orch-instructions").FillAsync("Keep changes minimal and only address this single Sonar warning.");
            await page.GetByTestId("orch-enqueue-btn").ClickAsync();

            await Expect(page.GetByTestId("orch-issues-status")).ToContainTextAsync("Queued 1 issue");
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("sq-gamma-001");
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("javascript:S1126");

            await Expect(page.GetByTestId("column-pending"))
                .Not.ToContainTextAsync(
                    "[CODE_SMELL]",
                    new LocatorAssertionsToContainTextOptions
                    {
                        Timeout = 15_000
                    });
        });
    }

    [Fact]
    public async Task RuleFilterShowsReadableLabelsOrderedByCountAndFiltersByExactKey()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await page.GetByTestId("orch-issue-type").SelectOptionAsync("CODE_SMELL");

            var ruleFilter = page.GetByTestId("orch-rule-filter");
            await Expect(ruleFilter.Locator("option")).ToHaveCountAsync(3);

            var firstRuleText = await ruleFilter.Locator("option").Nth(1).TextContentAsync();
            var secondRuleText = await ruleFilter.Locator("option").Nth(2).TextContentAsync();
            Assert.Contains("Cognitive Complexity of functions should not be too high (javascript:S3776) - 2", firstRuleText);
            Assert.Contains("Assignments should not be redundant (javascript:S1126) - 1", secondRuleText);

            await ruleFilter.SelectOptionAsync("javascript:S1126");
            await page.GetByTestId("orch-load-issues-btn").ClickAsync();

            var issueRows = page.Locator(".orch-issue-row");
            await Expect(issueRows).ToHaveCountAsync(1);
            await Expect(issueRows.First).ToContainTextAsync("javascript:S1126");
            await Expect(issueRows.First).Not.ToContainTextAsync("javascript:S3776");
        });
    }

    [Fact]
    public async Task QueueAllMatchingIsEnabledOnlyForSpecificRuleKey()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await page.GetByTestId("orch-issue-type").SelectOptionAsync("CODE_SMELL");

            var queueAll = page.GetByTestId("orch-enqueue-all-btn");
            await Expect(queueAll).ToBeDisabledAsync();

            await page.GetByTestId("orch-rule-filter").SelectOptionAsync("javascript:S3776");
            await Expect(queueAll).ToBeEnabledAsync();

            await page.GetByTestId("orch-instructions").FillAsync("Queue-all rule selection test");
            await queueAll.ClickAsync();
            await Expect(page.GetByTestId("orch-issues-status")).ToContainTextAsync("Queued 2 of 2 matching issue(s)");
        });
    }

    [Fact]
    public async Task ClearQueueCancelsQueuedItemsOnly()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await _fixture.PostJsonAsync(
            $"{_fixture.MockUrl}/__test__/setFailures",
            new
            {
                sessionCreateCount = 0,
                promptAsyncCount = 0,
                promptDelayMs = 3000
            });

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await page.GetByTestId("orch-issue-type").SelectOptionAsync("CODE_SMELL");
            await page.GetByTestId("orch-rule-filter").SelectOptionAsync("javascript:S3776");
            await Expect(page.GetByTestId("orch-enqueue-all-btn")).ToBeEnabledAsync();

            await page.GetByTestId("orch-enqueue-all-btn").ClickAsync();
            await Expect(page.GetByTestId("orch-issues-status")).ToContainTextAsync("Queued 2 of 2 matching issue(s)");

            await WaitForQueuedCountAtLeastAsync(1, TimeSpan.FromSeconds(20));

            page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();
            await page.GetByTestId("orch-clear-queue-btn").ClickAsync();
            await Expect(page.GetByTestId("orch-issues-status")).ToContainTextAsync("Cleared ");

            var queueData = await GetQueueResponseAsync();
            Assert.Equal(0, AsInt(GetNestedProperty(queueData, "stats", "queued")));
            Assert.True(AsInt(GetNestedProperty(queueData, "stats", "cancelled")) > 0);
        });
    }

    [Fact]
    public async Task FailedQueueItemCanBeRetriedAndThenCreatesSession()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await _fixture.PostJsonAsync(
            $"{_fixture.MockUrl}/__test__/setFailures",
            new
            {
                sessionCreateCount = 1,
                promptAsyncCount = 0
            });

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.GetByTestId("orchestrator-panel")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await LoadCodeSmellIssuesAsync(page);

            var firstIssue = page.Locator(".orch-issue-row").First;
            var firstIssueText = await firstIssue.TextContentAsync() ?? string.Empty;
            await firstIssue.Locator("input[type=\"checkbox\"]").CheckAsync();
            await page.GetByTestId("orch-instructions").FillAsync("Retry path test instruction.");
            await page.GetByTestId("orch-enqueue-btn").ClickAsync();

            await Expect(page.GetByTestId("orch-issues-status")).ToContainTextAsync("Queued 1 issue");

            var queuedItem = await GetLatestQueueItemForRuleAsync("javascript:S1126");
            Assert.NotNull(queuedItem);
            var queueId = AsInt(GetProperty(queuedItem.Value, "id"));

            var failedItem = await WaitForQueueItemStateByIdAsync(queueId, "failed", TimeSpan.FromSeconds(20));
            Assert.Contains("OpenCode request failed", GetString(failedItem, "lastError"));

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/queue/retry-failed",
                new
                {
                });

            var sessionCreated = await WaitForQueueItemStateByIdAsync(queueId, "running", TimeSpan.FromSeconds(20));
            Assert.False(string.IsNullOrWhiteSpace(GetString(sessionCreated, "sessionId")));
            Assert.Contains("/session/", GetString(sessionCreated, "openCodeUrl"));

            await page.ReloadAsync();
            await Expect(page.GetByTestId("column-in-progress")).ToContainTextAsync("javascript:S1126");
        });
    }

    [Fact]
    public async Task QueueItemCanBeCancelledWhileQueuedOrDispatching()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await _fixture.PostJsonAsync(
            $"{_fixture.MockUrl}/__test__/setFailures",
            new
            {
                sessionCreateCount = 0,
                promptAsyncCount = 0,
                promptDelayMs = 2500
            });

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await LoadCodeSmellIssuesAsync(page);

            var firstIssue = page.Locator(".orch-issue-row").First;
            var firstIssueText = await firstIssue.TextContentAsync() ?? string.Empty;
            await firstIssue.Locator("input[type=\"checkbox\"]").CheckAsync();
            await page.GetByTestId("orch-instructions").FillAsync("Cancellation flow test instruction.");
            await page.GetByTestId("orch-enqueue-btn").ClickAsync();
            await Expect(page.GetByTestId("orch-issues-status")).ToContainTextAsync("Queued 1 issue");

            var queuedItem = await GetLatestQueueItemForRuleAsync("javascript:S1126");
            Assert.NotNull(queuedItem);
            var queueId = AsInt(GetProperty(queuedItem.Value, "id"));

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/queue/{queueId}/cancel",
                new
                {
                });

            var cancelled = await WaitForQueueItemStateByIdAsync(queueId, "cancelled", TimeSpan.FromSeconds(20));
            Assert.False(string.IsNullOrWhiteSpace(GetString(cancelled, "cancelledAt")));

            await Task.Delay(3200);
            var latest = await GetQueueItemByIdAsync(queueId);
            Assert.NotNull(latest);
            Assert.Equal("cancelled", GetString(latest.Value, "state"));

            var sessions = await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/tasks/board?limit=all");
            var array = sessions.ValueKind == JsonValueKind.Array ? sessions.EnumerateArray().ToList() : [];
            var queueCard = array.FirstOrDefault(item => GetString(item, "id") == $"queue-{queueId}");
            Assert.True(queueCard.ValueKind != JsonValueKind.Undefined && queueCard.ValueKind != JsonValueKind.Null);
            Assert.Equal("cancelled", GetString(queueCard, "status"));
        });
    }

    [Fact]
    public async Task AwaitingReviewTaskCanBeApprovedRejectedAndRequeued()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await _fixture.PostJsonAsync(
            $"{_fixture.MockUrl}/__test__/setFailures",
            new
            {
                sessionCreateCount = 0,
                promptAsyncCount = 0,
                promptDelayMs = 0
            });

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await LoadCodeSmellIssuesAsync(page);

            var firstIssue = page.Locator(".orch-issue-row").First;
            await firstIssue.Locator("input[type=\"checkbox\"]").CheckAsync();
            await page.GetByTestId("orch-enqueue-btn").ClickAsync();

            var awaitingReview = await WaitForLatestTaskStateByRuleAsync("javascript:S1126", "awaiting_review", TimeSpan.FromSeconds(20));
            Assert.NotNull(awaitingReview);
            var taskId = AsInt(GetProperty(awaitingReview.Value, "id"));

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/tasks/{taskId}/reject",
                new
                {
                    reason = "Needs prompt tuning"
                });

            var rejected = await WaitForQueueItemStateByIdAsync(taskId, "rejected", TimeSpan.FromSeconds(10));
            Assert.Equal("rejected", GetString(rejected, "state"));

            var historyAfterReject = await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/orch/tasks/{taskId}/review-history");
            var rejectItems = GetArray(historyAfterReject, "items");
            Assert.NotEmpty(rejectItems);
            Assert.Equal("rejected", GetString(rejectItems[0], "action"));

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/tasks/{taskId}/requeue",
                new
                {
                    reason = "Retry with updated instructions"
                });

            var requeued = await WaitForQueueItemStateByIdAsync(taskId, "queued", TimeSpan.FromSeconds(10));
            Assert.Equal("queued", GetString(requeued, "state"));

            var historyAfterRequeue = await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/orch/tasks/{taskId}/review-history");
            var requeueItems = GetArray(historyAfterRequeue, "items");
            Assert.NotEmpty(requeueItems);
            Assert.Equal("requeue", GetString(requeueItems[0], "action"));

            await WaitForQueueItemStateByIdAsync(taskId, "awaiting_review", TimeSpan.FromSeconds(20));

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/tasks/{taskId}/approve",
                new
                {
                });

            var queueData = await GetQueueResponseAsync();
            Assert.True(AsInt(GetNestedProperty(queueData, "review", "rejected")) >= 0);
        });
    }

    [Fact]
    public async Task RejectedTaskCanBeRepromptedWithUpdatedInstructions()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await _fixture.PostJsonAsync(
            $"{_fixture.MockUrl}/__test__/setFailures",
            new
            {
                sessionCreateCount = 0,
                promptAsyncCount = 0,
                promptDelayMs = 0
            });

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await LoadCodeSmellIssuesAsync(page);

            var firstIssue = page.Locator(".orch-issue-row").First;
            await firstIssue.Locator("input[type=\"checkbox\"]").CheckAsync();
            await page.GetByTestId("orch-instructions").FillAsync("Initial task instructions for reprompt flow.");
            await page.GetByTestId("orch-enqueue-btn").ClickAsync();

            var awaitingReview = await WaitForLatestTaskStateByRuleAsync("javascript:S1126", "awaiting_review", TimeSpan.FromSeconds(20));
            Assert.NotNull(awaitingReview);
            var taskId = AsInt(GetProperty(awaitingReview.Value, "id"));

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/tasks/{taskId}/reject",
                new
                {
                    reason = "Need tighter instructions before retry"
                });

            await WaitForQueueItemStateByIdAsync(taskId, "rejected", TimeSpan.FromSeconds(10));

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/tasks/{taskId}/reprompt",
                new
                {
                    instructions = "Retry with a smaller patch and only change the target file.",
                    reason = "Previous pass was too broad"
                });

            var requeued = await WaitForQueueItemStateByIdAsync(taskId, "queued", TimeSpan.FromSeconds(10));
            Assert.Equal("Retry with a smaller patch and only change the target file.", GetString(requeued, "instructions"));
            Assert.Equal("reprompt", GetString(requeued, "lastReviewAction"));

            var historyAfterReprompt = await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/orch/tasks/{taskId}/review-history");
            var repromptItems = GetArray(historyAfterReprompt, "items");
            Assert.NotEmpty(repromptItems);
            Assert.Equal("reprompt", GetString(repromptItems[0], "action"));
        });
    }

    [Fact]
    public async Task TaskDetailLoadsLastAssistantMessageViaTaskRoute()
    {
        await _fixture.ResetMocksAsync();
        await WaitForNoActiveQueueAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await LoadCodeSmellIssuesAsync(page);

            var firstIssue = page.Locator(".orch-issue-row").First;
            await firstIssue.Locator("input[type=\"checkbox\"]").CheckAsync();
            await page.GetByTestId("orch-instructions").FillAsync("Keep changes minimal and only address this single Sonar warning.");
            await page.GetByTestId("orch-enqueue-btn").ClickAsync();

            await WaitForLatestTaskStateByRuleAsync("javascript:S1126", "running", TimeSpan.FromSeconds(20));
            await page.ReloadAsync();

            await page
                .GetByTestId("session-card")
                .Filter(
                    new LocatorFilterOptions
                    {
                        HasTextString = "javascript:S1126"
                    })
                .First.ClickAsync();

            await Expect(page.GetByTestId("detail-opencode-link")).ToBeVisibleAsync();
            var href = await page.GetByTestId("detail-opencode-link").GetAttributeAsync("href");
            Assert.Contains("/session/", href ?? string.Empty, StringComparison.Ordinal);
            await Expect(page.GetByTestId("detail-last-agent-message")).Not.ToContainTextAsync("Unable to load the last agent message.");
        });
    }

    async Task SetupGammaMappingAsync(IPage page)
    {
        await Expect(page.GetByTestId("orch-settings-toggle")).ToBeVisibleAsync();
        await page.GetByTestId("orch-settings-toggle").ClickAsync();
        await Expect(page.GetByTestId("orch-settings-modal")).ToBeVisibleAsync();
        await page.GetByTestId("orch-new-project-key").FillAsync("gamma-key");
        await page.GetByTestId("orch-new-directory").FillAsync(_fixture.GammaDirectory);
        await page.GetByTestId("orch-save-mapping-btn").ClickAsync();
        await Expect(page.GetByTestId("orch-settings-modal")).Not.ToBeVisibleAsync();
        await Expect(page.GetByTestId("orch-mapping-select")).ToHaveValueAsync(new Regex("\\d+"));
    }

    static async Task LoadCodeSmellIssuesAsync(IPage page, int expectedCount = 3)
    {
        await page.GetByTestId("orch-issue-type").SelectOptionAsync("CODE_SMELL");
        await page.GetByTestId("orch-load-issues-btn").ClickAsync();
        await Expect(page.Locator(".orch-issue-row")).ToHaveCountAsync(expectedCount);
    }

    async Task<JsonElement> GetQueueResponseAsync() => await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/orch/queue?limit=500");

    async Task<JsonElement?> GetLatestQueueItemForRuleAsync(string rule)
    {
        var data = await GetQueueResponseAsync();
        var items = GetArray(data, "items");

        var matches = items
            .Where(item => string.Equals(GetString(item, "rule"), rule, StringComparison.Ordinal))
            .OrderByDescending(item => AsInt(GetProperty(item, "id")))
            .ToList();

        return matches.FirstOrDefault();
    }

    async Task<JsonElement?> GetQueueItemByIdAsync(int queueId)
    {
        var data = await GetQueueResponseAsync();
        var items = GetArray(data, "items");

        return items.FirstOrDefault(item => AsInt(GetProperty(item, "id")) == queueId);
    }

    async Task<JsonElement?> WaitForLatestTaskStateByRuleAsync(string rule, string expectedState, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var candidate = await GetLatestQueueItemForRuleAsync(rule);

            if (candidate is not null &&
                string.Equals(GetString(candidate.Value, "state"), expectedState, StringComparison.Ordinal))
                return candidate;

            await Task.Delay(250);
        }

        return null;
    }

    async Task<JsonElement> WaitForQueuedCountAtLeastAsync(int minQueued, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var data = await GetQueueResponseAsync();
            var queued = AsInt(GetNestedProperty(data, "stats", "queued"));

            if (queued >= minQueued)
                return data;

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out waiting for queued count >= {minQueued}");
    }

    async Task<JsonElement> WaitForQueueItemStateByIdAsync(int queueId, string expectedState, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/orch/queue?limit=200");
            var items = GetArray(response, "items");
            var match = items.FirstOrDefault(item => AsInt(GetProperty(item, "id")) == queueId);

            if (match.ValueKind != JsonValueKind.Undefined &&
                match.ValueKind != JsonValueKind.Null &&
                string.Equals(GetString(match, "state"), expectedState, StringComparison.Ordinal))
                return match;

            await Task.Delay(250);
        }

        throw new TimeoutException($"Timed out waiting for task {queueId} to reach state {expectedState}");
    }

    async Task WaitForNoActiveQueueAsync()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/queue/clear",
                new
                {
                });

            var data = await GetQueueResponseAsync();
            var queued = AsInt(GetNestedProperty(data, "stats", "queued"));
            var dispatching = AsInt(GetNestedProperty(data, "stats", "dispatching"));

            if (queued == 0 &&
                dispatching == 0)
                return;

            await Task.Delay(250);
        }

        throw new TimeoutException("Timed out waiting for queue to become idle");
    }

    static int AsInt(JsonElement? node)
    {
        if (!node.HasValue)
            return 0;

        var value = node.Value;

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number))
            return number;

        if (int.TryParse(value.ToString(), out var parsed))
            return parsed;

        return 0;
    }

    static List<JsonElement> GetArray(JsonElement parent, string propertyName)
        => parent.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray().ToList()
            : [];

    static JsonElement? GetProperty(JsonElement parent, string propertyName)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out var property) ? property : null;

    static JsonElement? GetNestedProperty(JsonElement parent, string first, string second)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(first, out var firstProperty) && firstProperty.ValueKind == JsonValueKind.Object && firstProperty.TryGetProperty(second, out var secondProperty)
            ? secondProperty
            : null;

    static string? GetString(JsonElement parent, string propertyName)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out var property) ? property.ToString() : null;

    static async Task WithPage(Func<IPage, Task> test)
    {
        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = true
            });

        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await test(page);
    }
}
