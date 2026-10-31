using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Pembatalan pesanan dulu hanya bisa lewat API; layar kasir tidak punya tombolnya, sehingga
/// stok dan jurnal yang harus dikembalikan tertinggal tanpa jalan keluar yang wajar. Tes ini
/// menekan alur layar yang baru: alasan wajib diisi, satu permintaan berangkat, dan pesanan
/// benar-benar berstatus Cancelled — bukan hanya modal yang tertutup.
/// </summary>
[Collection("UI Tests")]
public sealed class OrderCancelUiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public OrderCancelUiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private sealed record Tangkapan(string Kunci, int Status);

    private static async Task<(int Id, string Nama)> BuatPesananAsync(HttpClient http)
    {
        var nama = $"Batal UI {Guid.NewGuid():N}"[..26];
        var response = await http.PostAsJsonAsync("/api/orders", new
        {
            customerName = nama,
            description = "Uji pembatalan dari layar kasir",
            pickupDate = "2030-10-10",
            totalAmount = 75_000,
            downPayment = 0,
            productTemplateId = (int?)null,
            quantity = 1,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var dokumen = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (dokumen.RootElement.GetProperty("id").GetInt32(), nama);
    }

    private static async Task<JsonElement> PesananAsync(HttpClient http, int id)
    {
        using var daftar = JsonDocument.Parse(await http.GetStringAsync("/api/orders"));
        return daftar.RootElement.EnumerateArray()
            .Single(o => o.GetProperty("id").GetInt32() == id)
            .Clone();
    }

    [Fact]
    public async Task BatalkanLewatLayar_AlasanWajib_DanPesananMenjadiDibatalkan()
    {
        var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var (id, nama) = await BuatPesananAsync(http);

        var page = _fixture.Page;
        await AppLogin.SignInAsync(page, _fixture.BaseUrl);
        await page.Locator("aside :text('Daftar Pesanan & Penjualan')").First.ClickAsync();

        var baris = page.Locator("tbody tr", new PageLocatorOptions { HasTextString = nama });
        await baris.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await baris.Locator("button:has-text('Batalkan')").First.ClickAsync();
        await page.WaitForSelectorAsync("h3:has-text('Batalkan Pesanan')");

        var kiriman = new List<Tangkapan>();
        void Tangkap(object? _, IResponse e)
        {
            if (e.Request.Method == "PUT" && e.Url.EndsWith($"/api/orders/{id}/status"))
                kiriman.Add(new Tangkapan(
                    e.Request.Headers.TryGetValue("idempotency-key", out var k) ? k : "", e.Status));
        }
        page.Response += Tangkap;
        try
        {
            // Tanpa alasan, form tidak boleh mengirim apa pun: pembatalan membalik stok dan jurnal,
            // dan alasan itulah satu-satunya jejak mengapa hal itu terjadi.
            await page.Locator("button[type='submit']:has-text('Batalkan Pesanan')").ClickAsync();
            await page.WaitForTimeoutAsync(700);
            kiriman.Should().BeEmpty("alasan pembatalan masih kosong");
            (await page.Locator("h3:has-text('Batalkan Pesanan')").IsVisibleAsync())
                .Should().BeTrue("modal tetap terbuka selama alasan belum diisi");

            await page.FillAsync("textarea[placeholder^='Contoh: pelanggan membatalkan']",
                "Pelanggan membatalkan lewat telepon");
            await page.Locator("button[type='submit']:has-text('Batalkan Pesanan')").ClickAsync();
            await page.WaitForSelectorAsync("h3:has-text('Batalkan Pesanan')",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 15_000 });
        }
        finally
        {
            page.Response -= Tangkap;
        }

        kiriman.Should().HaveCount(1, "satu tekan simpan menghasilkan satu perubahan status");
        kiriman[0].Status.Should().Be(200);
        kiriman[0].Kunci.Should().StartWith($"batal-pesanan-{id}",
            "satu modal pembatalan memakai satu kunci, sehingga klik ganda tidak membalik stok dua kali");

        var tersimpan = await PesananAsync(http, id);
        tersimpan.GetProperty("status").GetInt32().Should().Be(4, "OrderStatus.Cancelled");
        tersimpan.GetProperty("cancelReason").GetString().Should().Be("Pelanggan membatalkan lewat telepon",
            "alasan yang diketik kasir harus tersimpan apa adanya sebagai jejak pembatalan");

        // Baris di layar ikut berubah tanpa perlu memuat ulang manual: badge status diganti.
        await baris.Locator("span:has-text('Dibatalkan')").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        (await baris.Locator("button:has-text('Batalkan')").CountAsync()).Should().Be(0,
            "pesanan yang sudah dibatalkan tidak menawarkan pembatalan kedua");
    }
}
