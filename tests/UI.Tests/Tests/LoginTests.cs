using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// UI tests covering the login flow.
/// The fixture starts a throwaway host and database unless BAKERY_TEST_URL points elsewhere.
/// </summary>
[Collection("UI Tests")]
public sealed class LoginTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public LoginTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_OnLoad_HasNoPrefilledCredentials()
    {
        var page = _fixture.Page;

        // Tes lain bisa meninggalkan sesi di halaman yang sama: bentuk tanpa sesi dulu.
        await page.GotoAsync(_fixture.BaseUrl);
        await page.EvaluateAsync("() => { localStorage.removeItem('s22_token'); localStorage.removeItem('s22_user'); }");
        await page.ReloadAsync();

        await page.WaitForSelectorAsync("input[type='password']");
        // Kolom login tidak memakai atribut name, jadi ambil dari dalam form-nya.
        var username = page.Locator("form input[type='text']").First;
        var password = page.Locator("form input[type='password']").First;

        (await username.CountAsync()).Should().Be(1,
            "layar login hanya punya satu kolom nama pengguna; kalau lebih, seleksi tes ini tidak layak dipercaya");

        // Input React mengisi nilai lewat properti, bukan atribut value: baca nilai aktual.
        (await username.InputValueAsync()).Should().BeEmpty("bundel tidak boleh membawa nama pengguna pemilik");
        (await password.InputValueAsync()).Should().BeEmpty("kata sandi tidak boleh ikut terkirim di dalam bundel");

        // Bukti kedua: berkas bundel yang benar-benar dikirim ke pengguna tidak memuat sandi bawaan.
        var bundle = Path.Combine(TestHost.RepositoryRoot, "desktop", "assets");
        var js = Directory.GetFiles(bundle, "*.js");
        js.Should().NotBeEmpty("bundel produksi harus ada di desktop/assets");
        foreach (var file in js)
        {
            var content = await File.ReadAllTextAsync(file);
            content.Should().NotContain("admin123",
                $"{Path.GetFileName(file)} adalah berkas yang diunduh pengguna; sandi bawaan di dalamnya memberi tahu penyerang akun awal yang valid");
        }
    }

    [Fact]
    public async Task Login_WithValidCredentials_RedirectsToDashboard()
    {
        var page = _fixture.Page;

        await page.GotoAsync(_fixture.BaseUrl);

        // Fill login form using standard HTML selectors
        await page.WaitForSelectorAsync("input[name='username'], input[type='text']");

        var usernameInput = await page.QuerySelectorAsync("input[name='username']")
                            ?? await page.QuerySelectorAsync("input[type='text']");
        usernameInput.Should().NotBeNull("login form should have a username field");

        await usernameInput!.FillAsync(AppLogin.Username);

        var passwordInput = await page.QuerySelectorAsync("input[type='password']");
        passwordInput.Should().NotBeNull("login form should have a password field");

        await passwordInput!.FillAsync(await AppLogin.EnsureUsablePasswordAsync(_fixture.BaseUrl));

        var submitButton = await page.QuerySelectorAsync("button[type='submit']");
        submitButton.Should().NotBeNull("login form should have a submit button");

        await submitButton!.ClickAsync();

        // After login the dashboard or a main navigation element should be visible.
        // The app renders an <aside> sidebar element (Sidebar.jsx) — no .sidebar class or nav element.
        await page.WaitForSelectorAsync("aside, [data-testid='dashboard'], [data-testid='sidebar']", new PageWaitForSelectorOptions
        {
            Timeout = 10_000
        });

        var url = page.Url;
        url.Should().NotBe(_fixture.BaseUrl + "/login",
            "a successful login should navigate away from the login page");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShowsError()
    {
        var page = _fixture.Page;

        // Clear any existing auth so we land on the login form
        await page.GotoAsync(_fixture.BaseUrl);
        await page.EvaluateAsync("() => { localStorage.removeItem('s22_token'); localStorage.removeItem('s22_user'); }");
        await page.ReloadAsync();

        await page.WaitForSelectorAsync("input[name='username'], input[type='text']");

        var usernameInput = await page.QuerySelectorAsync("input[name='username']")
                            ?? await page.QuerySelectorAsync("input[type='text']");
        await usernameInput!.FillAsync("wronguser");

        var passwordInput = await page.QuerySelectorAsync("input[type='password']");
        await passwordInput!.FillAsync("wrongpassword");

        var submitButton = await page.QuerySelectorAsync("button[type='submit']");
        await submitButton!.ClickAsync();

        // An error message should appear — the login screen renders a div with class text-rose-400
        await page.WaitForSelectorAsync(
            "[role='alert'], .error, .alert-error, [data-testid='login-error'], .text-rose-400",
            new PageWaitForSelectorOptions { Timeout = 8_000 });

        var errorVisible = await page.IsVisibleAsync(
            "[role='alert'], .error, .alert-error, [data-testid='login-error'], .text-rose-400");

        errorVisible.Should().BeTrue("an error message should be shown for invalid credentials");
    }
}
