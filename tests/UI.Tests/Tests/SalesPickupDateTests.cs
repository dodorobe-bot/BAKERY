using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Regresi P2: pickup pernah memakai input tanggal+jam lalu dikirim sebagai ISO UTC, sehingga
/// pickup 00:00–06:59 WIB tersimpan sehari lebih awal. Sekarang form hanya meminta tanggal dan
/// mengirim "YYYY-MM-DD" apa adanya. Tes ini merah pada kode lama: di sana input yang sama
/// menghasilkan "2030-05-04T17:30:00.000Z" untuk pilihan 05 Mei pukul 00:30 WIB.
/// </summary>
[Collection("UI Tests")]
public sealed class SalesPickupDateTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public SalesPickupDateTests(PlaywrightFixture fixture) => _fixture = fixture;

    private const string Tanggal = "2030-05-05";

    [Fact]
    public async Task FormMengirimTanggalPickup_TanpaJamDanTanpaGeserHari()
    {
        var page = _fixture.Page;
        await AppLogin.SignInAsync(page, _fixture.BaseUrl);
        await page.Locator("aside :text('Daftar Pesanan & Penjualan')").First.ClickAsync();
        await page.Locator("button:has-text('Tambah Transaksi')").First.ClickAsync();
        await page.WaitForSelectorAsync("input[placeholder='Nama pelanggan atau instansi...']");

        var nama = $"Pickup Tanggal {Guid.NewGuid():N}"[..30];
        await page.FillAsync("input[placeholder='Nama pelanggan atau instansi...']", nama);
        await page.FillAsync("textarea[placeholder='Contoh: 100 roti tawar, 50 croissant coklat...']",
            "Uji tanggal pickup tanpa konversi zona waktu");

        // Kode lama memakai datetime-local; pilihan jam 00:30 adalah kondisi yang dulu bergeser.
        var inputTanggal = page.Locator("form input[type='date'], form input[type='datetime-local']").First;
        await inputTanggal.WaitForAsync();
        var jenis = await inputTanggal.GetAttributeAsync("type");
        await inputTanggal.FillAsync(jenis == "datetime-local" ? $"{Tanggal}T00:30" : Tanggal);

        await page.FillAsync("input[placeholder='Nominal dalam Rupiah...']", "25000");

        string? badan = null;
        void Tangkap(object? _, IRequest request)
        {
            if (request.Method == "POST" && request.Url.EndsWith("/api/orders"))
                badan = request.PostData;
        }

        page.Request += Tangkap;
        try
        {
            await page.Locator("button[type='submit']:has-text('Simpan Pesanan')").First.ClickAsync();
            await page.WaitForSelectorAsync("input[placeholder='Nominal dalam Rupiah...']",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 15_000 });
        }
        finally
        {
            page.Request -= Tangkap;
        }

        badan.Should().NotBeNullOrWhiteSpace("form harus mengirim pesanan ke server");
        using var dokumenKiriman = JsonDocument.Parse(badan!);
        dokumenKiriman.RootElement.GetProperty("pickupDate").GetString().Should().Be(Tanggal,
            "tanggal pickup harus dikirim sebagai tanggal lokal apa adanya, bukan ISO UTC yang bisa mundur sehari");

        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        using var daftar = JsonDocument.Parse(await http.GetStringAsync("/api/orders"));
        var pesanan = daftar.RootElement.EnumerateArray()
            .Single(o => o.GetProperty("customerNameSnapshot").GetString() == nama);
        pesanan.GetProperty("pickupDate").GetString().Should().StartWith(Tanggal,
            "yang tersimpan harus tanggal yang dipilih kasir, bukan tanggal hasil pergeseran zona waktu");
    }

    [Fact]
    public async Task Api_MenerimaTanggalSaja_DanMenyimpannyaUtuh()
    {
        using var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var nama = $"Api Tanggal {Guid.NewGuid():N}"[..28];

        using var buat = await owner.PostAsJsonAsync("/api/orders", new
        {
            customerName = nama,
            description = "Uji kontrak tanggal saja",
            pickupDate = Tanggal,
            totalAmount = 1_000_000,
            downPayment = 0,
            quantity = 1,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var baru = JsonDocument.Parse(teks);
        var id = baru.RootElement.GetProperty("id").GetInt32();

        using var lengkap = JsonDocument.Parse(await owner.GetStringAsync($"/api/orders/{id}"));
        lengkap.RootElement.GetProperty("pickupDate").GetString().Should().StartWith(Tanggal,
            "kontrak API harus menerima tanggal tanpa jam dan menyimpannya pada hari yang sama");
    }
}
