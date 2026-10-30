using System.Net;
using System.Text;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Tes ini menjalankan peramban sungguhan: form pesanan diisi, tombol "Simpan" ditekan dua kali
/// secepat kilat, lalu dibuktikan bahwa hanya satu permintaan yang berangkat dan hanya satu baris
/// pesanan yang tersimpan. Bagian kedua membuktikan kunci berganti ketika form dibuka lagi —
/// kalau tidak, pesanan kedua akan dianggap pengulangan dan hilang tanpa jejak.
/// </summary>
[Collection("UI Tests")]
public sealed class IdempotencyUiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public IdempotencyUiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private sealed record Tangkapan(string Kunci, int Status);

    private static Task KlikGandaAsync(IPage page, string pemilihTombol) =>
        // Dua klik dalam satu putaran event: React belum sempat men-disable tombol, jadi inilah
        // persis kondisi klik ganda yang dulu menghasilkan dua dokumen.
        page.EvalOnSelectorAsync(pemilihTombol, "el => { el.click(); el.click(); }");

    private static async Task<IReadOnlyList<Tangkapan>> TungguAsync(
        IList<Tangkapan> tangkapan, int jumlah, TimeSpan tenggat)
    {
        var akhir = DateTime.UtcNow + tenggat;
        while (tangkapan.Count < jumlah && DateTime.UtcNow < akhir)
            await Task.Delay(50);
        return tangkapan.ToList();
    }

    private static async Task BukaLayarAsync(IPage page, string baseUrl, string menu)
    {
        await AppLogin.SignInAsync(page, baseUrl);
        await page.Locator($"aside button:has-text('{menu}'), nav button:has-text('{menu}')")
            .First.ClickAsync();
    }

    private static async Task<int> JumlahPesananAsync(HttpClient http, string nama)
    {
        var daftar = JsonDocument.Parse(await http.GetStringAsync("/api/orders"));
        return daftar.RootElement.EnumerateArray()
            .Count(o => o.GetProperty("customerNameSnapshot").GetString() == nama);
    }

    private static async Task IsiFormPesananAsync(IPage page, string nama)
    {
        await page.Locator("button:has-text('Tambah Transaksi / Pesanan')").First.ClickAsync();
        await page.WaitForSelectorAsync("input[placeholder='Nama pelanggan atau instansi...']");
        await page.FillAsync("input[placeholder='Nama pelanggan atau instansi...']", nama);
        await page.FillAsync("form input[type='date']", "2030-05-05");
        await page.FillAsync("textarea[placeholder='Contoh: 100 roti tawar, 50 croissant coklat...']",
            "Uji klik ganda kunci idempotensi");
        await page.FillAsync("input[placeholder='Nominal dalam Rupiah...']", "2500000");
    }

    [Fact]
    public async Task KlikGandaSimpanPesanan_HanyaSatuPermintaanDanSatuBarisPesanan()
    {
        var page = _fixture.Page;
        await BukaLayarAsync(page, _fixture.BaseUrl, "Daftar Pesanan");

        var nama = $"Klik Ganda UI {Guid.NewGuid():N}"[..28];
        await IsiFormPesananAsync(page, nama);

        var kiriman = new List<Tangkapan>();
        void Tangkap(object? _, IResponse e)
        {
            if (e.Request.Method == "POST" && e.Url.EndsWith("/api/orders"))
                kiriman.Add(new Tangkapan(
                    e.Request.Headers.TryGetValue("idempotency-key", out var k) ? k : "", e.Status));
        }
        page.Response += Tangkap;
        try
        {
            await KlikGandaAsync(page, "button[type='submit']:has-text('Simpan Pesanan')");
            await TungguAsync(kiriman, 1, TimeSpan.FromSeconds(10));
            // Beri ruang untuk permintaan kedua andaikan penguncinya bocor.
            await page.WaitForTimeoutAsync(1500);
        }
        finally
        {
            page.Response -= Tangkap;
        }

        var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

        kiriman.Should().HaveCount(1,
            "penghenti sinkron di form harus membuat klik kedua tidak mengirim apa pun; " +
            "kalau lolos, server membalasnya 409 'masih diproses' yang terbaca sebagai gagal simpan");
        kiriman[0].Kunci.Should().NotBeNullOrWhiteSpace(
            "form pesanan harus membawa kunci idempotensi di percobaan simpan");
        kiriman[0].Status.Should().Be(201);

        (await JumlahPesananAsync(http, nama)).Should().Be(1,
            "satu kali membuka form menghasilkan satu pesanan, bukan dua");
    }

    [Fact]
    public async Task MembukaFormLagi_KunciBergantiPesananKeduaTetapTersimpan()
    {
        var page = _fixture.Page;
        await BukaLayarAsync(page, _fixture.BaseUrl, "Daftar Pesanan");

        var pertama = $"Kunci Berganti A {Guid.NewGuid():N}"[..28];
        var kedua = $"Kunci Berganti B {Guid.NewGuid():N}"[..28];

        var kiriman = new List<Tangkapan>();
        void Tangkap(object? _, IResponse e)
        {
            if (e.Request.Method == "POST" && e.Url.EndsWith("/api/orders"))
                kiriman.Add(new Tangkapan(
                    e.Request.Headers.TryGetValue("idempotency-key", out var k) ? k : "", e.Status));
        }

        page.Response += Tangkap;
        try
        {
            await IsiFormPesananAsync(page, pertama);
            await page.ClickAsync("button[type='submit']:has-text('Simpan Pesanan')");
            await TungguAsync(kiriman, 1, TimeSpan.FromSeconds(10));

            await IsiFormPesananAsync(page, kedua);
            await page.ClickAsync("button[type='submit']:has-text('Simpan Pesanan')");
            await TungguAsync(kiriman, 2, TimeSpan.FromSeconds(10));
        }
        finally
        {
            page.Response -= Tangkap;
        }

        kiriman.Should().HaveCount(2);
        kiriman[0].Kunci.Should().NotBeNullOrWhiteSpace();
        kiriman[1].Kunci.Should().NotBeNullOrWhiteSpace();
        kiriman[0].Kunci.Should().NotBe(kiriman[1].Kunci,
            "kunci yang tidak berganti membuat pesanan kedua dianggap pengulangan dan tidak pernah tersimpan");

        var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        (await JumlahPesananAsync(http, pertama)).Should().Be(1);
        (await JumlahPesananAsync(http, kedua)).Should().Be(1);
    }
}
