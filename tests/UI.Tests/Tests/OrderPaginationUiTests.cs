using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Daftar pesanan dulu hanya memuat 100 baris tanpa cara melihat sisanya. Tes ini menjalankan
/// host sementara sendiri, mengisi 101 pesanan nyata lewat API, lalu menekan "Muat lagi" di layar
/// dan memastikan baris ke-101 benar-benar datang dari server — bukan dari data yang sudah ada
/// di peramban.
///
/// Host terpisah dipakai supaya 101 pesanan uji tidak mengubah hitungan pada kelas tes lain yang
/// berbagi basis data.
/// </summary>
[Collection("UI Tests")]
public sealed class OrderPaginationUiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public OrderPaginationUiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private sealed record Sesi(string Token, string UserJson);

    [Fact]
    public async Task MuatLagi_MengambilHalamanKeduaDariServer()
    {
        var (proses, baseUrl, dbPath, keyPath) = await MulaiHostAsync();
        var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var sesi = await SiapkanSesiAsync(http);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesi.Token);

            for (var i = 1; i <= 101; i++)
            {
                var respons = await http.PostAsJsonAsync("/api/orders", new
                {
                    customerName = $"Muat Lagi {i:D3}",
                    description = "Uji halaman kedua daftar pesanan",
                    pickupDate = "2020-01-01",
                    totalAmount = 1_000,
                    downPayment = 0,
                    productTemplateId = (int?)null,
                    quantity = 1,
                });
                respons.StatusCode.Should().Be(HttpStatusCode.Created,
                    await respons.Content.ReadAsStringAsync());
            }

            using (var cek = await http.GetAsync("/api/orders?limit=1"))
                cek.Headers.GetValues("X-Total-Count").Single().Should().Be("101");

            var page = _fixture.Page;
            await page.GotoAsync(baseUrl);
            await page.EvaluateAsync(
                $"(() => {{ localStorage.setItem('s22_token', {JsonSerializer.Serialize(sesi.Token)}); " +
                $"localStorage.setItem('s22_user', {JsonSerializer.Serialize(sesi.UserJson)}); }})()");
            await page.ReloadAsync();
            await page.WaitForSelectorAsync("aside", new PageWaitForSelectorOptions { Timeout = 10_000 });
            await page.Locator("aside :text('Daftar Pesanan & Penjualan')").First.ClickAsync();

            // Halaman pertama: 100 baris, baris tertua (id 1) belum ada.
            await page.WaitForSelectorAsync("text=(baru 100 dari 101 dimuat)",
                new PageWaitForSelectorOptions { Timeout = 15_000 });
            (await page.Locator("tbody tr", new PageLocatorOptions { HasTextString = "Muat Lagi 001" })
                .CountAsync()).Should().Be(0, "baris ke-101 berada di halaman kedua");

            var minta = new List<string>();
            void Tangkap(object? _, IRequest request)
            {
                if (request.Method == "GET" && request.Url.Contains("/api/orders?")
                    && request.Url.Contains("offset="))
                    minta.Add(request.Url);
            }
            page.Request += Tangkap;
            try
            {
                await page.Locator("button:has-text('Muat lagi')").First.ClickAsync();
                await page.Locator("tbody tr", new PageLocatorOptions { HasTextString = "Muat Lagi 001" })
                    .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            }
            finally
            {
                page.Request -= Tangkap;
            }

            minta.Should().ContainSingle("satu tekan meminta tepat satu halaman tambahan")
                .Which.Should().Contain("limit=100").And.Contain("offset=100");

            (await page.Locator("tbody tr").CountAsync()).Should().Be(101,
                "seluruh pesanan harus terbaca setelah halaman kedua dimuat");
            (await page.Locator("button:has-text('Muat lagi')").CountAsync()).Should().Be(0,
                "tombol hilang setelah semua baris termuat");
        }
        finally
        {
            http.Dispose();
            await HentikanAsync(proses);
            TryDelete(dbPath);
            TryDelete(keyPath);
        }
    }

    private static async Task<(Process Proses, string BaseUrl, string DbPath, string KeyPath)> MulaiHostAsync()
    {
        var root = TestHost.RepositoryRoot;
        var hostDll = TestHost.ResolveHostDll(root);
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var dbPath = Path.Combine(Path.GetTempPath(), $"seruni-paging-{stamp}.db");
        var keyPath = Path.Combine(Path.GetTempPath(), $"seruni-paging-{stamp}.key");
        var baseUrl = $"http://127.0.0.1:{FreePort()}";

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(hostDll);
        psi.ArgumentList.Add($"--Database:Path={dbPath}");
        psi.ArgumentList.Add($"--Desktop:StaticFilesPath={Path.Combine(root, "desktop")}");
        psi.ArgumentList.Add($"--Jwt:SigningKeyFile={keyPath}");
        psi.ArgumentList.Add($"--urls={baseUrl}");

        var proses = Process.Start(psi)
            ?? throw new InvalidOperationException("Host uji gagal dijalankan.");
        var log = new System.Text.StringBuilder();
        proses.OutputDataReceived += (_, e) => { if (e.Data is not null) log.AppendLine(e.Data); };
        proses.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.AppendLine(e.Data); };
        proses.BeginOutputReadLine();
        proses.BeginErrorReadLine();

        await WaitHealthyAsync(baseUrl, proses, log);
        return (proses, baseUrl, dbPath, keyPath);
    }

    private static async Task HentikanAsync(Process proses)
    {
        try
        {
            if (!proses.HasExited) proses.Kill(entireProcessTree: true);
            proses.WaitForExit(5000);
        }
        catch { /* proses sudah selesai sendiri */ }
        proses.Dispose();
    }

    /// <summary>
    /// Akun seed masih wajib ganti sandi; gerbangnya harus dilewati lewat API produksi sebelum
    /// token boleh dipakai membaca dan menulis pesanan di host sementara ini.
    /// </summary>
    private static async Task<Sesi> SiapkanSesiAsync(HttpClient http)
    {
        var seed = await LoginAsync(http, AppLogin.SeededPassword);
        if (seed.GetProperty("mustChangePassword").GetBoolean())
        {
            using var ganti = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
            {
                Content = JsonContent.Create(new
                {
                    currentPassword = AppLogin.SeededPassword,
                    newPassword = AppLogin.TestPassword,
                }),
            };
            ganti.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", seed.GetProperty("token").GetString());
            var hasil = await http.SendAsync(ganti);
            hasil.StatusCode.Should().Be(HttpStatusCode.OK, await hasil.Content.ReadAsStringAsync());
        }

        var sesi = await LoginAsync(http, AppLogin.TestPassword);
        var userJson = JsonSerializer.Serialize(new
        {
            id = sesi.GetProperty("userId").GetInt32(),
            username = sesi.GetProperty("username").GetString(),
            role = sesi.GetProperty("role"),
            capabilities = sesi.TryGetProperty("capabilities", out var cap) ? cap : default,
            mustChangePassword = sesi.GetProperty("mustChangePassword").GetBoolean(),
        });
        return new Sesi(sesi.GetProperty("token").GetString()!, userJson);
    }

    private static async Task<JsonElement> LoginAsync(HttpClient http, string password)
    {
        var respons = await http.PostAsync("/api/auth/login",
            JsonContent.Create(new { username = AppLogin.Username, password }));
        var isi = await respons.Content.ReadAsStringAsync();
        respons.StatusCode.Should().Be(HttpStatusCode.OK, isi);
        using var dokumen = JsonDocument.Parse(isi);
        return dokumen.RootElement.Clone();
    }

    private static async Task WaitHealthyAsync(string baseUrl, Process proses, System.Text.StringBuilder log)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(2) };
        var tenggat = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < tenggat)
        {
            if (proses.HasExited)
                throw new InvalidOperationException(
                    $"Host uji berhenti lebih dulu (exit {proses.ExitCode}). Log:{Environment.NewLine}{log}");
            try
            {
                using var respons = await http.GetAsync("/health");
                if (respons.IsSuccessStatusCode) return;
            }
            catch { /* host belum mendengarkan */ }
            await Task.Delay(250);
        }
        throw new InvalidOperationException($"Host uji tidak siap di {baseUrl}. Log:{Environment.NewLine}{log}");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void TryDelete(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); }
            catch { /* Windows membersihkan temp sendiri */ }
        }
    }
}
