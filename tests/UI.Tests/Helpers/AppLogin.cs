using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Helpers;

/// <summary>
/// Sesi aplikasi untuk tes UI.
///
/// Akun seed 'admin' berangkat dengan MustChangePassword=1 dan server menolak token bawaan itu
/// untuk endpoint data (403), jadi satu-satunya cara yang jujur bagi tes adalah memenuhi gerbang
/// itu lewat API produksi: POST /api/auth/change-password, lalu login dengan sandi tes.
///
/// Itu mengubah sandi akun pemilik di basis data yang sedang diuji, jadi hanya boleh terjadi pada
/// basis data sementara milik TestHost. Kalau caller menunjuk BAKERY_TEST_URL ke host lain, helper
/// ini berhenti dan minta host-nya yang dibangun sendiri — tidak pernah menyentuh data pemakaian.
/// </summary>
public static class AppLogin
{
    public const string Username = "admin";
    public const string SeededPassword = "admin123";
    public const string TestPassword = "SandiTesUI2026";

    private static string? _usablePassword;

    /// <summary>Signs in through the auth API and opens the app carrying that session.</summary>
    public static async Task SignInAsync(IPage page, string baseUrl)
    {
        var password = await EnsureUsablePasswordAsync(baseUrl);
        var (token, userJson) = await RequestSessionAsync(baseUrl, password);

        await page.GotoAsync(baseUrl);
        await page.EvaluateAsync(
            $"(() => {{ " +
            $"localStorage.setItem('s22_token', {JsonSerializer.Serialize(token)}); " +
            $"localStorage.setItem('s22_user', {JsonSerializer.Serialize(userJson)}); " +
            "})()");
        await page.ReloadAsync();

        await page.WaitForSelectorAsync("aside", new PageWaitForSelectorOptions { Timeout = 10_000 });
    }

    /// <summary>
    /// Password that currently opens the seeded account on this host: the seed password while the
    /// server still lets it through, otherwise the test password established through the API.
    /// </summary>
    public static async Task<string> EnsureUsablePasswordAsync(string baseUrl)
    {
        if (_usablePassword is not null) return _usablePassword;

        using var http = NewClient(baseUrl);

        var lewatSandiTes = await LoginAsync(http, TestPassword);
        if (lewatSandiTes is not null)
        {
            _usablePassword = TestPassword;
            return _usablePassword;
        }

        var lewatSeed = await LoginAsync(http, SeededPassword);
        if (lewatSeed is null)
            throw new InvalidOperationException(
                $"Akun '{Username}' tidak bisa masuk di {baseUrl}: sandi bawaan dan sandi tes dua-duanya ditolak.");

        var seed = lewatSeed.Value;
        if (seed.GetProperty("mustChangePassword").GetBoolean())
        {
            await LepaskanGerbangAsync(http, baseUrl, seed.GetProperty("token").GetString()!);
            return TestPassword;
        }

        _usablePassword = SeededPassword;
        return _usablePassword;
    }

    private static async Task LepaskanGerbangAsync(HttpClient http, string baseUrl, string token)
    {
        if (!TestHost.Manages(baseUrl))
            throw new InvalidOperationException(
                $"Akun '{Username}' di {baseUrl} masih wajib ganti sandi dan server menolak token itu untuk " +
                "endpoint data. Jalankan tes tanpa BAKERY_TEST_URL supaya fixture memakai basis data sementara; " +
                "helper ini tidak akan mengganti sandi pada host yang bukan miliknya.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = SeededPassword, newPassword = TestPassword }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Ganti sandi bawaan gagal di {baseUrl} (HTTP {(int)response.StatusCode}): {body}");

        _usablePassword = TestPassword;
    }

    /// <summary>
    /// Klien HTTP yang sudah membawa token sesi. Dipakai tes untuk membaca kembali nilai yang
    /// baru saja ditulis lewat layar — tanpa ini, bug satuan uang di frontend tidak terdeteksi
    /// karena layar yang salah dan datanya yang salah tampak cocok satu sama lain.
    /// </summary>
    public static async Task<HttpClient> AuthorizedClientAsync(string baseUrl)
    {
        var password = await EnsureUsablePasswordAsync(baseUrl);
        var (token, _) = await RequestSessionAsync(baseUrl, password);
        var http = NewClient(baseUrl);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static HttpClient NewClient(string baseUrl) =>
        new() { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<JsonElement?> LoginAsync(HttpClient http, string password)
    {
        var response = await http.PostAsync("/api/auth/login",
            JsonContent.Create(new { username = Username, password }));
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<(string Token, string UserJson)> RequestSessionAsync(string baseUrl, string password)
    {
        using var http = NewClient(baseUrl);
        var session = await LoginAsync(http, password)
            ?? throw new InvalidOperationException(
                $"Login '{Username}' gagal di {baseUrl}. Jalankan tes dengan basis data uji yang ter-migrate.");

        // Sama dengan objek yang disimpan api.setAuth() setelah login form — termasuk daftar
        // kapabilitas, karena getUser() kini menolak sesi tanpa kapabilitas (menu harus disaring
        // dari data server, bukan ditebak dari peran).
        var userJson = JsonSerializer.Serialize(new
        {
            id = session.GetProperty("userId").GetInt32(),
            username = session.GetProperty("username").GetString(),
            role = session.GetProperty("role"),
            capabilities = session.TryGetProperty("capabilities", out var cap) ? cap : default,
            mustChangePassword = session.GetProperty("mustChangePassword").GetBoolean(),
        });

        return (session.GetProperty("token").GetString()!, userJson);
    }
}
