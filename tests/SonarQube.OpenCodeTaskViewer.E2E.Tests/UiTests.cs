using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace SonarQube.OpenCodeTaskViewer.E2E.Tests;

[Collection(E2eCollection.Name)]
public sealed class UiTests
{
    readonly E2eEnvironmentFixture _fixture;

    public UiTests(E2eEnvironmentFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoadsTaskNativeBoardAndShowsEmptyStateWhenNoQueueItemsExist()
    {
        await _fixture.ResetMocksAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);

            await Expect(page.Locator("aside.sidebar")).ToHaveCountAsync(0);
            await Expect(page.GetByText("All Tasks")).ToBeVisibleAsync();
            await Expect(page.GetByText("No tasks found")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("count-pending")).ToHaveTextAsync("0");
            await Expect(page.GetByTestId("count-in-progress")).ToHaveTextAsync("0");
            await Expect(page.GetByTestId("count-completed")).ToHaveTextAsync("0");
            await Expect(page.GetByTestId("count-cancelled")).ToHaveTextAsync("0");
        });
    }

    [Fact]
    public async Task ProjectFilterAppliesToTaskBackedBoard()
    {
        await _fixture.ResetMocksAsync();

        var gammaMapping = await _fixture.PostJsonAndReadAsync(
            $"{_fixture.ViewerUrl}/api/orch/mappings",
            new
            {
                sonarProjectKey = "gamma-key",
                directory = _fixture.GammaDirectory,
                enabled = true
            });

        var alphaMapping = await _fixture.PostJsonAndReadAsync(
            $"{_fixture.ViewerUrl}/api/orch/mappings",
            new
            {
                sonarProjectKey = "alpha-key",
                directory = _fixture.AlphaDirectory,
                enabled = true
            });

        var gammaMappingId = AsInt(gammaMapping.TryGetProperty("id", out var gammaId) ? gammaId : null);
        var alphaMappingId = AsInt(alphaMapping.TryGetProperty("id", out var alphaId) ? alphaId : null);

        await _fixture.PostJsonAsync(
            $"{_fixture.ViewerUrl}/api/orch/enqueue",
            new
            {
                mappingId = gammaMappingId,
                issueType = "CODE_SMELL",
                instructions = "Fix safely",
                issues = new object[]
                {
                    new
                    {
                        key = "sq-gamma-001",
                        type = "CODE_SMELL",
                        severity = "MAJOR",
                        rule = "javascript:S1126",
                        message = "Remove this redundant assignment.",
                        component = "gamma-key:src/worker.js",
                        line = 42,
                        status = "OPEN",
                        relativePath = "src/worker.js",
                        absolutePath = $"{_fixture.GammaDirectory}/src/worker.js"
                    }
                }
            });

        await _fixture.PostJsonAsync(
            $"{_fixture.ViewerUrl}/api/orch/enqueue",
            new
            {
                mappingId = alphaMappingId,
                issueType = "CODE_SMELL",
                instructions = "Fix safely",
                issues = new object[]
                {
                    new
                    {
                        key = "sq-alpha-001",
                        type = "CODE_SMELL",
                        severity = "MINOR",
                        rule = "javascript:S1481",
                        message = "Remove this unused local variable.",
                        component = "alpha-key:src/index.js",
                        line = 7,
                        status = "OPEN",
                        relativePath = "src/index.js",
                        absolutePath = $"{_fixture.AlphaDirectory}/src/index.js"
                    }
                }
            });

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);

            await Expect(page.Locator("[data-testid=\"session-card\"]")).ToHaveCountAsync(2);
            await Expect(page.Locator("[data-testid=\"session-card\"]").Nth(0)).ToContainTextAsync("sq-alpha-001");
            await Expect(page.Locator("[data-testid=\"session-card\"]").Nth(1)).ToContainTextAsync("sq-gamma-001");

            var gammaOption = page
                .GetByTestId("project-filter")
                .Locator("option")
                .Filter(
                    new LocatorFilterOptions
                    {
                        HasTextString = Path.GetFileName(_fixture.GammaDirectory)
                    })
                .First;

            var gammaValue = await gammaOption.GetAttributeAsync("value");
            Assert.False(string.IsNullOrWhiteSpace(gammaValue));

            await page.GetByTestId("project-filter").SelectOptionAsync(gammaValue);
            await Expect(page.Locator("[data-testid=\"session-card\"]")).ToHaveCountAsync(1);
            await Expect(page.Locator("[data-testid=\"session-card\"]").First).ToContainTextAsync("sq-gamma-001");
        });
    }

    [Fact]
    public async Task OrchestrationSettingsCanRemoveMapping()
    {
        await _fixture.ResetMocksAsync();

        await WithPage(async page =>
        {
            await page.GotoAsync(_fixture.ViewerUrl);
            await Expect(page.Locator("#orchestrator-panel.visible")).ToBeVisibleAsync();

            await page.GetByTestId("orch-settings-toggle").ClickAsync();
            await page.GetByTestId("orch-new-project-key").FillAsync("gamma-key");
            await page.GetByTestId("orch-new-directory").FillAsync(_fixture.GammaDirectory);
            await page.GetByTestId("orch-save-mapping-btn").ClickAsync();

            await page.GetByTestId("orch-settings-toggle").ClickAsync();
            await Expect(page.GetByTestId("orch-delete-mapping-select")).ToBeVisibleAsync();

            await page
                .GetByTestId("orch-delete-mapping-select")
                .SelectOptionAsync(
                    new[]
                    {
                        "1"
                    });

            page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();
            await page.GetByTestId("orch-delete-mapping-btn").ClickAsync();

            await page.GetByTestId("orch-settings-toggle").ClickAsync();
            await Expect(page.GetByTestId("orch-delete-mapping-select")).ToHaveValueAsync(string.Empty);
        });
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

    static int AsInt(JsonElement? value)
    {
        if (!value.HasValue)
            return 0;

        var element = value.Value;

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out var parsedElement))
            return parsedElement;

        return int.TryParse(element.ToString(), out var parsed) ? parsed : 0;
    }
}
