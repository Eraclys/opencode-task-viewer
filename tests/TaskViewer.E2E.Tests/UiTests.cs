using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace TaskViewer.E2E.Tests;

[Collection(E2eCollection.Name)]
public sealed class UiTests
{
    private readonly E2eEnvironmentFixture _fixture;

    public UiTests(E2eEnvironmentFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoadsWithNoSidebarAndShowsAllSessionsBoard()
    {
        await _fixture.ResetOpenCodeAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);

            await Expect(page.Locator("aside.sidebar")).ToHaveCountAsync(0);
            await Expect(page.GetByTestId("connection-status")).ToContainTextAsync("Connected");
            await Expect(page.GetByText("All Sessions")).ToBeVisibleAsync();

            await Expect(page.GetByTestId("count-pending")).ToHaveTextAsync("1");
            await Expect(page.GetByTestId("count-in-progress")).ToHaveTextAsync("2");
            await Expect(page.GetByTestId("count-completed")).ToHaveTextAsync("1");
            await Expect(page.GetByTestId("count-cancelled")).ToHaveTextAsync("0");

            await Expect(page.GetByTestId("column-in-progress")).ToContainTextAsync("Busy Session");
            await Expect(page.GetByTestId("column-in-progress")).ToContainTextAsync("Retrying Session");
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("Recently Updated");
            await Expect(page.GetByTestId("column-completed")).ToContainTextAsync("Stale Session");

            var titles = await page
                .GetByTestId("column-in-progress")
                .Locator("[data-testid=\"session-card\"] .task-title")
                .AllTextContentsAsync();
            Assert.Equal(["Retrying Session", "Busy Session"], titles.Select(x => x.Trim()).ToArray());

            await Expect(page.GetByText("Archived Session (Should Not Show)")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task IncludesSessionsDiscoveredViaProjectSandboxes()
    {
        await _fixture.ResetOpenCodeAsync();
        await _fixture.PostJsonAsync($"{_fixture.MockUrl}/__test__/addSandboxSession", new
        {
            projectWorktree = @"C:\Work\Alpha",
            sandboxPath = @"C:\Work\Alpha\SandboxOnly",
            directory = @"C:\Work\Alpha\SandboxOnly",
            sessionId = "sess-sandbox-only",
            title = "Sandbox Only Session"
        });

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("Sandbox Only Session");

            await page.GetByTestId("project-filter").SelectOptionAsync("C:/Work/Alpha");
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("Sandbox Only Session");
        });
    }

    [Fact]
    public async Task ProjectFilterAppliesAndPersistsViaLocalStorage()
    {
        await _fixture.ResetOpenCodeAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);

            await page.GetByTestId("project-filter").SelectOptionAsync("C:/Work/Gamma");
            await Expect(page.GetByTestId("count-pending")).ToHaveTextAsync("1");
            await Expect(page.GetByTestId("count-in-progress")).ToHaveTextAsync("0");
            await Expect(page.GetByTestId("count-completed")).ToHaveTextAsync("1");
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("Recently Updated");
            await Expect(page.GetByTestId("column-completed")).ToContainTextAsync("Stale Session");
            await Expect(page.GetByTestId("column-in-progress")).Not.ToContainTextAsync("Busy Session");

            await page.ReloadAsync();
            await Expect(page.GetByTestId("project-filter")).ToHaveValueAsync("C:/Work/Gamma");
            await Expect(page.GetByTestId("count-pending")).ToHaveTextAsync("1");
            await Expect(page.GetByTestId("count-completed")).ToHaveTextAsync("1");
        });
    }

    [Fact]
    public async Task RefreshesBoardAfterSessionStatusSse()
    {
        await _fixture.ResetOpenCodeAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.GetByTestId("column-pending")).ToContainTextAsync("Recently Updated");

            await _fixture.PostJsonAsync($"{_fixture.MockUrl}/__test__/setStatus", new
            {
                directory = @"C:\Work\Gamma",
                sessionId = "sess-recent",
                type = "busy"
            });

            await _fixture.PostJsonAsync($"{_fixture.MockUrl}/__test__/emit", new
            {
                directory = @"C:\Work\Gamma",
                type = "session.status",
                properties = new
                {
                    sessionID = "sess-recent",
                    status = new { type = "busy" }
                }
            });

            await Expect(page.GetByTestId("column-in-progress")).ToContainTextAsync("Recently Updated");
            await Expect(page.GetByTestId("count-in-progress")).ToHaveTextAsync("3");
            await Expect(page.GetByTestId("count-pending")).ToHaveTextAsync("0");
        });
    }

    [Fact]
    public async Task DoesNotCrashIfSessionsApiFails()
    {
        await _fixture.ResetOpenCodeAsync();

        await WithPage(async page =>
        {
            await page.RouteAsync("**/api/sessions**", async route =>
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 500,
                    ContentType = "application/json",
                    Body = "{\"error\":\"Injected failure\"}"
                });
            });

            await page.GotoAsync(_fixture.ViewerUrl);

            await Expect(page.GetByText("No sessions found")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("count-pending")).ToHaveTextAsync("0");
            await Expect(page.GetByTestId("count-in-progress")).ToHaveTextAsync("0");
            await Expect(page.GetByTestId("count-completed")).ToHaveTextAsync("0");
            await Expect(page.GetByTestId("count-cancelled")).ToHaveTextAsync("0");
        });
    }

    [Fact]
    public async Task CanArchiveSingleSelectedSessionViaBulkAction()
    {
        await _fixture.ResetOpenCodeAsync();

        await WithPage(async page =>
        {
            page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();

            await page.GotoAsync(_fixture.ViewerUrl);

            var card = page.GetByTestId("session-card").Filter(new LocatorFilterOptions { HasTextString = "Recently Updated" }).First;
            await card.HoverAsync();
            await card.Locator("input.card-select").CheckAsync();
            await Expect(page.GetByTestId("bulk-archive-btn")).ToHaveTextAsync("Archive selected (1)");
            await page.GetByTestId("bulk-archive-btn").ClickAsync();

            await Expect(page.GetByText("Recently Updated")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task CanClearSelectedSessions()
    {
        await _fixture.ResetOpenCodeAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);

            var recentCard = page.GetByTestId("session-card").Filter(new LocatorFilterOptions { HasTextString = "Recently Updated" }).First;
            var staleCard = page.GetByTestId("session-card").Filter(new LocatorFilterOptions { HasTextString = "Stale Session" }).First;

            await recentCard.HoverAsync();
            await recentCard.Locator("input.card-select").CheckAsync();
            await staleCard.HoverAsync();
            await staleCard.Locator("input.card-select").CheckAsync();

            await Expect(page.GetByTestId("bulk-archive-btn")).ToHaveTextAsync("Archive selected (2)");
            await Expect(page.GetByTestId("clear-selection-btn")).ToBeEnabledAsync();

            await page.GetByTestId("clear-selection-btn").ClickAsync();

            await Expect(page.GetByTestId("bulk-archive-btn")).ToHaveTextAsync("Archive selected (0)");
            await Expect(page.GetByTestId("bulk-archive-btn")).ToBeDisabledAsync();
            await Expect(page.GetByTestId("clear-selection-btn")).ToBeDisabledAsync();
            await Expect(page.Locator("input.card-select:checked")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task CanBulkArchiveSelectedSessions()
    {
        await _fixture.ResetOpenCodeAsync();

        await WithPage(async page =>
        {
            page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();

            await page.GotoAsync(_fixture.ViewerUrl);
            await page.GetByTestId("project-filter").SelectOptionAsync("C:/Work/Gamma");

            var recentCard = page.GetByTestId("session-card").Filter(new LocatorFilterOptions { HasTextString = "Recently Updated" }).First;
            var staleCard = page.GetByTestId("session-card").Filter(new LocatorFilterOptions { HasTextString = "Stale Session" }).First;

            await recentCard.HoverAsync();
            await recentCard.Locator("input.card-select").CheckAsync();
            await staleCard.HoverAsync();
            await staleCard.Locator("input.card-select").CheckAsync();

            await Expect(page.GetByTestId("bulk-archive-btn")).ToHaveTextAsync("Archive selected (2)");
            await page.GetByTestId("bulk-archive-btn").ClickAsync();

            await Expect(page.GetByText("Recently Updated")).ToHaveCountAsync(0);
            await Expect(page.GetByText("Stale Session")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task DetailsShowLastAgentMessageAndOpenCodeLink()
    {
        await _fixture.ResetOpenCodeAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);

            await page.GetByTestId("session-card").Filter(new LocatorFilterOptions { HasTextString = "Stale Session" }).First.ClickAsync();

            await Expect(page.GetByTestId("detail-last-agent-message")).ToContainTextAsync("Diagnostics complete; all checks passed.");
            await Expect(page.GetByTestId("detail-opencode-link")).ToHaveAttributeAsync(
                "href",
                $"{_fixture.MockUrl}/QzovV29yay9HYW1tYQ/session/sess-stale");
        });
    }

    private static async Task WithPage(Func<IPage, Task> test)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await test(page);
    }
}
