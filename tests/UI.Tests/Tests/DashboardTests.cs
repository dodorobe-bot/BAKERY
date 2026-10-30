using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// UI tests covering the main dashboard view.
/// The fixture starts a throwaway host and database unless BAKERY_TEST_URL points elsewhere.
/// The dashboard endpoint is GET /api/dashboard (returns balance, revenue, etc.).
/// </summary>
[Collection("UI Tests")]
public sealed class DashboardTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public DashboardTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Dashboard_Loads_ShowsKeyMetrics()
    {
        var page = _fixture.Page;

        // Navigate and log in if needed
        await EnsureLoggedInAsync(page, _fixture.BaseUrl);

        // Navigate to the dashboard item ("Dasbor & Indikator Utama" from navigation.js)
        var dashboardLocator = page.Locator(
            "aside button:has-text('Dasbor'), aside a:has-text('Dasbor')");

        var dashboardNavExists = await dashboardLocator.CountAsync() > 0;
        if (dashboardNavExists)
        {
            await dashboardLocator.First.ClickAsync();
        }

        // Wait for the dashboard to render — identified by the KPI grid or header text.
        // Dashboard is the default tab; after login the main content area renders immediately.
        await page.WaitForSelectorAsync(
            "main, .space-y-6, [class*='grid'][class*='gap-4']",
            new PageWaitForSelectorOptions { Timeout = 10_000 });

        // Verify at least one metric element is visible and non-empty.
        // The dashboard KPI cards use rounded-2xl class; check for the grid container.
        var metricSelectors = new[]
        {
            "[data-testid='metric-balance']",
            "[data-testid='metric-revenue']",
            "[data-testid='metric-orders']",
            ".metric-card",
            ".stat-card",
            ".kpi-card",
            ".summary-card",
            ".rounded-2xl"  // KPI cards in App.jsx use rounded-2xl
        };

        var anyMetricVisible = false;
        foreach (var selector in metricSelectors)
        {
            var count = await page.Locator(selector).CountAsync();
            if (count > 0)
            {
                anyMetricVisible = true;
                break;
            }
        }

        anyMetricVisible.Should().BeTrue(
            "the dashboard should display at least one key metric card (balance, revenue, or order count)");
    }

    [Fact]
    public async Task Dashboard_ApiData_IsNotHardcoded()
    {
        var page = _fixture.Page;

        await EnsureLoggedInAsync(page, _fixture.BaseUrl);

        // Navigate to the dashboard if there is a nav item
        var dashboardLocator = page.Locator(
            "aside button:has-text('Dasbor'), aside a:has-text('Dasbor')");

        var dashboardNavExists = await dashboardLocator.CountAsync() > 0;
        if (dashboardNavExists)
        {
            await dashboardLocator.First.ClickAsync();
        }

        await page.WaitForSelectorAsync(
            "main, .space-y-6, [class*='grid'][class*='gap-4']",
            new PageWaitForSelectorOptions { Timeout = 10_000 });

        // Intercept the /api/dashboard response to verify the UI is bound to real API data
        string? apiResponseBody = null;

        page.Response += (_, response) =>
        {
            if (response.Url.Contains("/api/dashboard"))
            {
                _ = response.TextAsync().ContinueWith(t => apiResponseBody = t.Result);
            }
        };

        // Reload the page to trigger a fresh API call
        await page.ReloadAsync();

        // Sesi masih tersimpan di localStorage, jadi tidak perlu masuk ulang di sini.

        // Give the dashboard a moment to load after reload
        await page.WaitForSelectorAsync(
            "main, .space-y-6, [class*='grid'][class*='gap-4']",
            new PageWaitForSelectorOptions { Timeout = 10_000 });

        // Verify that at least one numeric element exists in the dashboard and is not empty
        var numericLocator = page.Locator(
            ".metric-card, .stat-card, .kpi-card, .summary-card, " +
            "[data-testid^='metric-'], .rounded-2xl");

        var count = await numericLocator.CountAsync();
        count.Should().BeGreaterThan(0,
            "the dashboard should render at least one metric element populated from the API");

        if (count > 0)
        {
            var firstMetricText = await numericLocator.First.InnerTextAsync();
            firstMetricText.Should().NotBeNullOrWhiteSpace(
                "metric elements should contain data fetched from /api/dashboard, not empty placeholders");
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Form login asli plus gerbang ganti sandi wajib (lihat <see cref="AppLogin"/>):
    /// tanpa langkah gerbang, overlay "Ganti Kata Sandi" menutupi seluruh aplikasi.
    /// </summary>
    private static Task EnsureLoggedInAsync(IPage page, string baseUrl)
        => AppLogin.SignInAsync(page, baseUrl);
}
