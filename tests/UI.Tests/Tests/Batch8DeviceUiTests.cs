using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Dua hal yang hanya terbukti lewat layar sungguhan: halaman Perangkat Kasir membaca daftar
/// printer dari server (bukan daftar contoh), dan kolom pindai mengisi produk serta total dari
/// katalog saat kode diketik lalu Enter — persis perilaku pemindai keyboard wedge.
/// </summary>
[Collection("UI Tests")]
public sealed class Batch8DeviceUiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public Batch8DeviceUiTests(PlaywrightFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task HalamanPerangkat_MenampilkanPrinterDariServerDanMenyimpanPilihan()
    {
        var page = _fixture.Page;
        await AppLogin.SignInAsync(page, _fixture.BaseUrl);

        await page.Locator("aside :text('Perangkat Kasir')").First.ClickAsync();
        await page.WaitForSelectorAsync("h1:has-text('Perangkat Kasir')",
            new PageWaitForSelectorOptions { Timeout = 10_000 });

        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        using var dok = JsonDocument.Parse(await http.GetStringAsync("/api/devices"));
        var akar = dok.RootElement;
        var jumlahPrinter = akar.GetProperty("printers").GetArrayLength();
        var didukung = akar.GetProperty("perangkatDidukung").GetBoolean();

        var opsi = page.Locator("select option");
        (await opsi.CountAsync()).Should().Be(jumlahPrinter + 1,
            "daftar pilihan harus persis sama dengan printer yang dilaporkan server, tanpa nama karangan");

        if (didukung && jumlahPrinter == 0)
        {
            (await page.Locator("text=Pasang printer beserta drivernya lebih dulu").CountAsync())
                .Should().BeGreaterThan(0, "printer kosong harus dikatakan apa adanya");
        }

        if (jumlahPrinter > 0)
        {
            var nama = akar.GetProperty("printers")[0].GetProperty("name").GetString()!;
            await page.SelectOptionAsync("select", nama);
        }
        else
        {
            await page.SelectOptionAsync("select", "");
        }

        await page.Locator("button:has-text('Simpan Pengaturan')").ClickAsync();
        await page.WaitForSelectorAsync("text=Pengaturan perangkat disimpan",
            new PageWaitForSelectorOptions { Timeout = 10_000 });

        using var sesudah = JsonDocument.Parse(await http.GetStringAsync("/api/devices"));
        var tersimpan = sesudah.RootElement.GetProperty("settings").GetProperty("printerName").GetString();
        if (jumlahPrinter > 0)
        {
            var dipilih = akar.GetProperty("printers")[0].GetProperty("name").GetString();
            tersimpan.Should().Be(dipilih, "pilihan printer harus benar-benar tersimpan di server");
        }
        else
        {
            tersimpan.Should().BeNullOrEmpty("tanpa printer terpasang, tidak ada nama yang boleh dipalsukan");
        }
    }

    [Fact]
    public async Task PindaiBarcode_MengisiProdukDanTotalDariKatalog()
    {
        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var barcode = $"UJI{Guid.NewGuid():N}"[..16];
        var nama = $"Pindai Uji {Guid.NewGuid():N}"[..24];
        using (var buat = await http.PostAsJsonAsync("/api/products", new
        {
            name = nama,
            category = "Uji Batch-8",
            sellingPriceMinorUnits = 1_250_000,
            barcode,
            lines = Array.Empty<object>(),
        }))
        {
            buat.IsSuccessStatusCode.Should().BeTrue(await buat.Content.ReadAsStringAsync());
        }

        using var katalog = JsonDocument.Parse(await http.GetStringAsync("/api/products"));
        var produk = katalog.RootElement.EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == nama);
        var id = produk.GetProperty("id").GetInt32();

        var page = _fixture.Page;
        await AppLogin.SignInAsync(page, _fixture.BaseUrl);
        await page.Locator("aside :text('Daftar Pesanan & Penjualan')").First.ClickAsync();
        await page.Locator("button:has-text('Tambah Transaksi')").First.ClickAsync();

        const string kolomPindai = "input[placeholder*='tembakkan pemindai']";
        await page.WaitForSelectorAsync(kolomPindai);

        // Keyboard wedge mengetik kode ke kolom aktif lalu Enter. Itu yang ditiru di sini.
        await page.FillAsync(kolomPindai, barcode);
        await page.PressAsync(kolomPindai, "Enter");

        var nilaiProduk = await page.Locator("select").First.InputValueAsync();
        nilaiProduk.Should().Be(id.ToString(), "barcode harus memilih produk yang barcode-nya cocok");

        var total = await page.InputValueAsync("input[placeholder='Nominal dalam Rupiah...']");
        total.Should().Be("12500", "total diisi dari harga katalog Rp 12.500, bukan diketik kasir");

        await page.FillAsync(kolomPindai, $"TIDAK-ADA-{Guid.NewGuid():N}"[..20]);
        await page.PressAsync(kolomPindai, "Enter");
        await page.WaitForSelectorAsync("text=tidak cocok dengan produk mana pun",
            new PageWaitForSelectorOptions { Timeout = 10_000 });
    }
}
