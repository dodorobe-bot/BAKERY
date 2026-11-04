using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Modal "Selesaikan" dulu punya kolom bernama "Sisa Stok Dapur" yang sebenarnya menghitung
/// Standar Resep dikurangi Dipakai Aktual. Angka itu tidak pernah di bawah nol dan tidak ada
/// hubungannya dengan isi Dapur Produksi, padahal SPK justru mengurangi bahan dari lokasi itu:
/// juru masak melihat "0" yang tampak aman, lalu penolakan server datang setelah seluruh isian
/// dikerjakan.
///
/// Tes ini membuka layar yang sebenarnya, menyiapkan stok dapur lewat jalur resmi, dan menuntut
/// kolom "Stok Dapur" memperlihatkan jumlah riil di Dapur Produksi serta "Sisa Setelah Dipakai"
/// boleh negatif (dan merah) sebelum formulir dikirim.
/// </summary>
[Collection("UI Tests")]
public sealed class ProductionKitchenStockUiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public ProductionKitchenStockUiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private static string Nama(string awalan) => $"{awalan} {Guid.NewGuid():N}";

    private sealed record Spk(int BahanId, string NamaBahan, string NomorSpk);

    private static async Task<JsonElement> KirimAsync(HttpClient http, string path, object isi)
    {
        using var respons = await http.PostAsJsonAsync(path, isi);
        var teks = await respons.Content.ReadAsStringAsync();
        respons.IsSuccessStatusCode.Should().BeTrue($"{path} menolak: {teks}");
        return JsonDocument.Parse(teks).RootElement.Clone();
    }

    private static async Task<int> LokasiAsync(HttpClient http, string tipe)
    {
        using var dok = JsonDocument.Parse(await http.GetStringAsync("/api/inventory/locations"));
        return dok.RootElement.EnumerateArray()
            .First(l => l.GetProperty("type").GetString() == tipe)
            .GetProperty("id").GetInt32();
    }

    /// <summary>Isi stok satu lokasi lewat opname layar Inventaris, jalur resmi yang juga menjurnal.</summary>
    private static async Task IsiStokAsync(HttpClient http, int locationId, int ingredientId, decimal quantity)
    {
        using var respons = await http.PostAsJsonAsync("/api/inventory/adjust", new
        {
            locationId,
            ingredientId,
            newQuantity = quantity,
            reason = "Uji layar Selesaikan SPK",
        });
        var teks = await respons.Content.ReadAsStringAsync();
        respons.StatusCode.Should().Be(HttpStatusCode.OK, teks);
    }

    /// <summary>
    /// Bahan dengan stok dapur tertentu + produk berreresep satu takaran + SPK yang belum selesai.
    /// </summary>
    private async Task<Spk> BuatSpkAsync(HttpClient http, string namaBahan, decimal stokDapur, decimal takaranPerUnit, int target)
    {
        var bahan = await KirimAsync(http, "/api/ingredients", new
        {
            name = namaBahan,
            unit = "kg",
            pricePerUnit = 2_000_00,
        });
        var bahanId = bahan.GetProperty("id").GetInt32();

        await IsiStokAsync(http, await LokasiAsync(http, "Kitchen"), bahanId, stokDapur);

        var produk = await KirimAsync(http, "/api/products", new
        {
            name = Nama("Roti Uji Dapur"),
            category = "Uji SPK",
            sellingPriceMinorUnits = 1_000_000,
            lines = new[] { new { ingredientId = bahanId, quantity = takaranPerUnit } },
        });

        var spk = await KirimAsync(http, "/api/production/orders", new
        {
            productTemplateId = produk.GetProperty("id").GetInt32(),
            targetQuantity = target,
            laborCost = 0,
            overheadCost = 0,
        });

        return new Spk(bahanId, namaBahan, spk.GetProperty("orderNumber").GetString()!);
    }

    private async Task<ILocator> BukaModalSelesaikanAsync(IPage page, Spk spk)
    {
        await AppLogin.SignInAsync(page, _fixture.BaseUrl);
        await page.Locator("aside button:has-text('Perintah Kerja (SPK)')").First.ClickAsync();
        await page.Locator("main").GetByText(spk.NomorSpk).First
            .WaitForAsync(new() { Timeout = 20_000 });

        await page.Locator($"tr:has-text('{spk.NomorSpk}') button:has-text('Selesaikan')").ClickAsync();
        await page.Locator("h3:has-text('Penyelesaian SPK')").WaitForAsync(new() { Timeout = 15_000 });

        var tabel = page.Locator("table:has(th:has-text('Sisa Setelah Dipakai'))");
        await tabel.WaitForAsync(new() { Timeout = 15_000 });
        return tabel.Locator($"tr:has-text('{spk.NamaBahan}')");
    }

    [Fact]
    public async Task ModalSelesaikanSpk_StokDapurDanSisaMemakaiStokRiilBukanTakaranResep()
    {
        var page = _fixture.Page;
        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

        // Dapur memegang 5,244 kg, resep menuntut 1 kg per unit dan SPK ini merencanakan 3 unit.
        // Angka pecahan sekalian menguji pemisah desimal: "5.244" terbaca lima ribu dua ratus
        // empat puluh empat oleh operator Indonesia, padahal maksudnya lima koma dua empat empat.
        var spk = await BuatSpkAsync(http, Nama("Tepung Uji Dapur"), stokDapur: 5.244m, takaranPerUnit: 1m, target: 3);
        var baris = await BukaModalSelesaikanAsync(page, spk);
        var sel = baris.Locator("td");

        var standar = await sel.Nth(1).InnerTextAsync();
        standar.Should().Contain("3", "SPK merencanakan 3 unit x 1 kg")
            .And.NotContain("5", "kolom Standar Resep hanya memuat takaran resep, bukan stok");

        (await sel.Nth(3).InnerTextAsync()).Should().Be("5,244 kg",
            "kolom Stok Dapur menampilkan jumlah riil di Dapur Produksi dengan pemisah desimal Indonesia");
        (await sel.Nth(4).InnerTextAsync()).Should().Be("2,244 kg",
            "sisa = stok riil dikurangi pemakaian, bukan '0' palsu hasil takaran resep dikurangi pemakaian");

        (await page.GetByText("Sisa dihitung dari stok riil di Dapur Produksi.", new() { Exact = true })
            .First.IsVisibleAsync()).Should().BeTrue();

        // Label lama menyesatkan karena menyebut stok dapur padahal menghitung resep.
        (await page.Locator("th:has-text('Sisa Stok Dapur')").CountAsync()).Should().Be(0,
            "kolom dengan nama lama harus hilang");
    }

    [Fact]
    public async Task ModalSelesaikanSpk_PemakaianMelebihiStokDapur_SisaNegatifDanMerah()
    {
        var page = _fixture.Page;
        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

        // Dapur hanya 2 kg sementara rencana menuntut 3 kg: keadaan yang dulu tersamar di balik "0".
        var spk = await BuatSpkAsync(http, Nama("Mentega Uji Dapur"), stokDapur: 2m, takaranPerUnit: 1m, target: 3);
        var baris = await BukaModalSelesaikanAsync(page, spk);
        var sisa = baris.Locator("td").Nth(4);

        (await sisa.InnerTextAsync()).Should().Be("-1 kg",
            "kekurangan bahan harus terlihat sebelum formulir dikirim, bukan sesudah ditolak server");
        (await sisa.GetAttributeAsync("class")).Should().Contain("text-red-400");

        // Mengubah "Dipakai Aktual" harus menghitung ulang dari stok dapur yang sama.
        await baris.Locator("input[type='number']").FillAsync("1.5");
        await WaitForAsync(sisa, "0,5 kg");
        (await sisa.GetAttributeAsync("class")).Should().Contain("text-emerald-400");
    }

    [Fact]
    public async Task ModalSelesaikanSpk_BahanBelumPernahMasukDapur_SisaDihitungDariNol()
    {
        var page = _fixture.Page;
        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

        // Bahan ini sengaja tidak pernah disetor ke dapur, jadi Dapur Produksi tidak punya baris
        // stok untuknya sama sekali. Bagi SPK yang menentukan kecukupan adalah dapur: sisanya harus
        // langsung negatif, bukan kosong seolah datanya belum diketahui.
        var namaBahan = Nama("Gula Uji Dapur");
        var spk = await BuatSpkAsync(http, namaBahan, stokDapur: 0m, takaranPerUnit: 1m, target: 4);
        await IsiStokAsync(http, await LokasiAsync(http, "Warehouse"), spk.BahanId, 9m);

        var baris = await BukaModalSelesaikanAsync(page, spk);
        var sel = baris.Locator("td");
        (await sel.Nth(3).InnerTextAsync()).Should().Be("0 kg",
            "bahan yang belum pernah disetor ke dapur memang nol stoknya di dapur");
        (await sel.Nth(4).InnerTextAsync()).Should().Be("-4 kg",
            "stok gudang tidak menolong SPK; kekurangan harus terlihat di sini");
    }

    /// <summary>Re-render React terjadi beberapa milidetik setelah isian berubah.</summary>
    private static async Task WaitForAsync(ILocator locator, string teks)
    {
        var akhir = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < akhir)
        {
            if (await locator.InnerTextAsync() == teks) return;
            await Task.Delay(100);
        }
        (await locator.InnerTextAsync()).Should().Be(teks, "kolom sisa belum ikut berubah");
    }
}
