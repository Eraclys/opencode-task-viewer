using System.Text.Json.Nodes;
using Microsoft.Playwright;
using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace TaskViewer.E2E.Tests;

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
            await Expect(page.GetByTestId("orchestrator-panel")).ToBeVisibleAsync();

            await SetupGammaMappingAsync(page);
            await LoadCodeSmellIssuesAsync(page);

            var firstIssue = page.Locator(".orch-issue-row").First;
            await firstIssue.Locator("input[type=\"checkbox\"]").CheckAsync();

            await page.GetByTestId("orch-instructions").FillAsync("Keep changes minimal and only address this single Sonar warning.");
            await page.GetByTestId("orch-enqueue-btn").ClickAsync();

            await Expect(page.GetByTestId("orch-issues-status")).ToContainTextAsync("Queued 1 issue");
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("[CODE_SMELL]");
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("javascript:S1126");

            await Expect(page.GetByTestId("column-pending"))
                .Not.ToContainTextAsync(
                    "[Queued]",
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
            await Expect(page.GetByTestId("orchestrator-panel")).ToBeVisibleAsync();

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
            await Expect(page.GetByTestId("orchestrator-panel")).ToBeVisibleAsync();

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
            await Expect(page.GetByTestId("orchestrator-panel")).ToBeVisibleAsync();

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
            Assert.Equal(0, AsInt(queueData["stats"]?["queued"]));
            Assert.True(AsInt(queueData["stats"]?["cancelled"]) > 0);
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
            var queueId = AsInt(queuedItem!["id"]);

            var failedItem = await WaitForQueueItemStateByIdAsync(queueId, "failed", TimeSpan.FromSeconds(20));
            Assert.Contains("OpenCode request failed", failedItem["lastError"]?.ToString());

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/queue/retry-failed",
                new
                {
                });

            var sessionCreated = await WaitForQueueItemStateByIdAsync(queueId, "running", TimeSpan.FromSeconds(20));
            Assert.False(string.IsNullOrWhiteSpace(sessionCreated["sessionId"]?.ToString()));
            Assert.Contains("/session/", sessionCreated["openCodeUrl"]?.ToString());

            await page.ReloadAsync();
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("javascript:S1126");
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
            await Expect(page.GetByTestId("orchestrator-panel")).ToBeVisibleAsync();

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
            var queueId = AsInt(queuedItem!["id"]);

            await _fixture.PostJsonAsync(
                $"{_fixture.ViewerUrl}/api/orch/queue/{queueId}/cancel",
                new
                {
                });

            var cancelled = await WaitForQueueItemStateByIdAsync(queueId, "cancelled", TimeSpan.FromSeconds(20));
            Assert.False(string.IsNullOrWhiteSpace(cancelled["cancelledAt"]?.ToString()));

            await Task.Delay(3200);
            var latest = await GetQueueItemByIdAsync(queueId);
            Assert.NotNull(latest);
            Assert.Equal("cancelled", latest!["state"]?.ToString());

            var sessions = await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/sessions?limit=all");
            var array = sessions as JsonArray ?? [];
            var queueCard = array.FirstOrDefault(item => item?["id"]?.ToString() == $"queue-{queueId}");
            Assert.Null(queueCard);
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

    static async Task LoadCodeSmellIssuesAsync(IPage page)
    {
        await page.GetByTestId("orch-issue-type").SelectOptionAsync("CODE_SMELL");
        await page.GetByTestId("orch-load-issues-btn").ClickAsync();
        await Expect(page.Locator(".orch-issue-row")).ToHaveCountAsync(3);
    }

    async Task<JsonNode> GetQueueResponseAsync() => await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/orch/queue?limit=500");

    async Task<JsonNode?> GetLatestQueueItemForRuleAsync(string rule)
    {
        var data = await GetQueueResponseAsync();
        var items = data["items"] as JsonArray ?? [];

        var matches = items
            .Where(item => string.Equals(item?["rule"]?.ToString(), rule, StringComparison.Ordinal))
            .OrderByDescending(item => AsInt(item?["id"]))
            .ToList();

        return matches.FirstOrDefault();
    }

    async Task<JsonNode?> GetQueueItemByIdAsync(int queueId)
    {
        var data = await GetQueueResponseAsync();
        var items = data["items"] as JsonArray ?? [];
        return items.FirstOrDefault(item => AsInt(item?["id"]) == queueId);
    }

    async Task<JsonNode> WaitForQueuedCountAtLeastAsync(int minQueued, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var data = await GetQueueResponseAsync();
            var queued = AsInt(data["stats"]?["queued"]);

            if (queued >= minQueued)
                return data;

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out waiting for queued count >= {minQueued}");
    }

    async Task<JsonNode> WaitForQueueItemStateByIdAsync(int queueId, string expectedState, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await _fixture.GetJsonAsync($"{_fixture.ViewerUrl}/api/orch/queue?limit=200");
            var items = response["items"] as JsonArray ?? [];
            var match = items.FirstOrDefault(item => AsInt(item?["id"]) == queueId);

            if (match is not null &&
                string.Equals(match["state"]?.ToString(), expectedState, StringComparison.Ordinal))
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
            var queued = AsInt(data["stats"]?["queued"]);
            var dispatching = AsInt(data["stats"]?["dispatching"]);

            if (queued == 0 &&
                dispatching == 0)
                return;

            await Task.Delay(250);
        }

        throw new TimeoutException("Timed out waiting for queue to become idle");
    }

    static int AsInt(JsonNode? node)
    {
        if (node is null)
            return 0;

        if (int.TryParse(node.ToString(), out var parsed))
            return parsed;

        return 0;
    }

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
