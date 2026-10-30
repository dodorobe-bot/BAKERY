using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Helpers;

/// <summary>
/// xUnit fixture that creates a shared Playwright browser and page for each test class.
/// Reads configuration from environment variables:
///   BAKERY_TEST_URL       — base URL of an app the caller started (optional)
///   PLAYWRIGHT_HEADLESS   — set to "false" to run in headed mode (default: headless)
/// Without BAKERY_TEST_URL the fixture starts its own host on a throwaway database, so a
/// test run can no longer touch the shop's real data.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IPage Page { get; private set; } = null!;

    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("BAKERY_TEST_URL");
        BaseUrl = string.IsNullOrWhiteSpace(external)
            ? await TestHost.StartAsync()
            : external;

        var headlessEnv = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS");
        var headless = !string.Equals(headlessEnv, "false", StringComparison.OrdinalIgnoreCase);

        _playwright = await Playwright.CreateAsync();

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless
        });

        Page = await _browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (Page is not null)
        {
            await Page.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}
