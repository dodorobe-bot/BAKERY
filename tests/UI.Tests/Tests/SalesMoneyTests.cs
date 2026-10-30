using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Bukti ujung-ke-ujung untuk satuan uang di layar Penjualan.
///
/// Alasan tes ini ada: bug-nya tidak kelihatan dari layar saja. Kolom Total pernah mengirim
/// rupiah mentah ke API yang menyimpan unit minor (Rp x 100), sementara tabel membaca nilai
/// minor itu tanpa membagi 100 — jadi operator melihat "Rp 50.000" padahal yang tersimpan
/// Rp 500. Hanya pembacaan ulang lewat API yang membuktikan keduanya benar-benar sepaket.
/// </summary>
[Collection("UI Tests")]
public sealed class SalesMoneyTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public SalesMoneyTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OrderForm_RupiahTyped_IsStoredAsMinorUnitsAndShownBack()
    {
        var page = _fixture.Page;
        await AppLogin.SignInAsync(page, _fixture.BaseUrl);

        await page.Locator("aside :text('Daftar Pesanan & Penjualan')").First.ClickAsync();
        await page.Locator("button:has-text('Tambah Transaksi')").First.ClickAsync();
        await page.WaitForSelectorAsync("input[placeholder='Nama pelanggan atau instansi...']");

        // Nama unik: tes lain menulis pesanan ke basis data yang sama.
        var nama = $"Uang Uji {Guid.NewGuid():N}"[..28];
        await page.FillAsync("input[placeholder='Nama pelanggan atau instansi...']", nama);
        await page.FillAsync("textarea[placeholder='Contoh: 100 roti tawar, 50 croissant coklat...']",
            "Uji satuan uang");
        await page.FillAsync("form input[type='date']", "2026-11-05");
        await page.FillAsync("input[placeholder='Nominal dalam Rupiah...']", "50000");
        await page.FillAsync("input[placeholder='0 jika belum ada DP']", "10000");
        await page.Locator("button[type='submit']:has-text('Simpan Pesanan')").First.ClickAsync();

        await page.WaitForSelectorAsync("input[placeholder='Nominal dalam Rupiah...']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 15_000 });

        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var respons = await http.GetStringAsync("/api/orders");
        using var dokumen = JsonDocument.Parse(respons);
        var pesanan = dokumen.RootElement.EnumerateArray()
            .Single(o => o.GetProperty("customerNameSnapshot").GetString() == nama);

        var tersimpan = pesanan.GetProperty("totalAmount").GetInt64();
        var dp = pesanan.GetProperty("downPayment").GetInt64();
        tersimpan.Should().Be(5_000_000,
            "Rp 50.000 yang diketik user harus menjadi 5.000.000 unit minor, bukan 50.000");
        dp.Should().Be(1_000_000, "DP Rp 10.000 harus ikut dikali 100");

        // Baris tabel adalah tempat operator mengambil keputusan: tampilkan rupiah, bukan minor.
        var baris = page.Locator($"table tbody tr:has-text('{nama}')").First;
        var teks = await baris.TextContentAsync();
        teks.Should().Contain("50.000", $"kolom total harus menampilkan Rp 50.000, dapat: {teks}");
        teks.Should().NotContain("5.000.000",
            $"angka minor tidak boleh mentas ke layar, dapat: {teks}");
    }
}
