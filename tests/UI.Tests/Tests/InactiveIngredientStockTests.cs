using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Tombol sampah di layar Bahan Baku bersifat lembut: barisnya dibiarkan dengan IsActive = 0.
/// Stoknya memang tetap tampil di Inventaris — itu benar, barangnya nyata masih ada di rak — tetapi
/// dua hal ikut rusak: harganya hilang (layar hanya mengambil harga dari daftar bahan aktif sehingga
/// nilai stok selalu Rp 0) dan sisa stok itu tidak bisa dihabiskan dari layar mana pun, karena
/// Pemeriksaan Fisik (Stok Opname) maupun Formulir Mutasi Bahan hanya menawarkan bahan aktif. Sisa
/// stok jadi terjebak selamanya di neraca.
///
/// Dua tes pertama memanggil endpoint stok dan penyetelan stok; tes ketiga membuka layar opname
/// yang sebenarnya dan menuntut baris bahan nonaktif ditawarkan lalu bisa disetel ke nol.
/// </summary>
[Collection("UI Tests")]
public sealed class InactiveIngredientStockTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public InactiveIngredientStockTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static string NamaBaru() => $"Bahan Sampah {Guid.NewGuid():N}"[..26];

    private static async Task<int> BuatBahanAsync(HttpClient owner, string nama, long harga, decimal stok)
    {
        using var buat = await owner.PostAsJsonAsync("/api/ingredients",
            new { name = nama, unit = "kg", pricePerUnit = harga, currentStock = stok });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        return JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();
    }

    private static async Task NonaktifkanAsync(HttpClient owner, int bahan)
    {
        var hapus = await owner.DeleteAsync($"/api/ingredients/{bahan}");
        hapus.StatusCode.Should().Be(HttpStatusCode.OK, await hapus.Content.ReadAsStringAsync());
    }

    private static async Task<int> GudangAsync(HttpClient owner)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/inventory/locations"));
        return dok.RootElement.EnumerateArray()
            .First(l => l.GetProperty("type").GetString() == "Warehouse")
            .GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement> BarisStokAsync(HttpClient owner, int gudang, int bahan)
    {
        using var dok = JsonDocument.Parse(
            await owner.GetStringAsync($"/api/inventory/locations/{gudang}/stock"));
        return dok.RootElement.EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("ingredientId").GetInt32() == bahan)
            .Clone();
    }

    private static async Task<decimal> StokGudangAsync(HttpClient owner, int gudang, int bahan)
    {
        var baris = await BarisStokAsync(owner, gudang, bahan);
        return baris.ValueKind == JsonValueKind.Undefined ? -1m : baris.GetProperty("quantity").GetDecimal();
    }

    [Fact]
    public async Task BahanDinonaktifkan_BarisStoknyaTetapMembawaHargaMaster()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 5m);
        var gudang = await GudangAsync(owner);

        var sebelum = await BarisStokAsync(owner, gudang, bahan);
        sebelum.GetProperty("ingredientIsActive").GetBoolean().Should().BeTrue();

        await NonaktifkanAsync(owner, bahan);

        // Kontrak lama tidak berubah: master nonaktif tidak lagi ikut daftar bahan.
        using (var daftar = JsonDocument.Parse(await owner.GetStringAsync("/api/ingredients")))
        {
            daftar.RootElement.EnumerateArray()
                .Should().NotContain(p => p.GetProperty("name").GetString() == nama,
                    "bahan yang sudah dinonaktifkan tidak boleh muncul lagi di pilihan resep");
        }

        var sesudah = await BarisStokAsync(owner, gudang, bahan);
        sesudah.ValueKind.Should().Be(JsonValueKind.Object,
            "barangnya masih ada di rak, jadi baris stoknya tidak boleh hilang");
        sesudah.GetProperty("quantity").GetDecimal().Should().Be(5m);
        sesudah.GetProperty("ingredientIsActive").GetBoolean().Should().BeFalse();
        sesudah.GetProperty("ingredientPricePerUnit").GetInt64().Should().Be(100_000,
            "harga dibaca dari tabel bahan itu sendiri, bukan dari daftar bahan aktif, supaya "
            + "nilai stok tidak menyurut jadi nol hanya karena master-nya nonaktif");
    }

    [Fact]
    public async Task SisaStokBahanNonaktif_DapatDihabiskanLewatPenyetelanStok()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 5m);
        var gudang = await GudangAsync(owner);

        await NonaktifkanAsync(owner, bahan);

        var setel = await owner.PostAsJsonAsync("/api/inventory/adjust", new
        {
            locationId = gudang,
            ingredientId = bahan,
            newQuantity = 0m,
            reason = "Menghabiskan sisa bahan nonaktif",
        });
        var isi = await setel.Content.ReadAsStringAsync();
        setel.StatusCode.Should().Be(HttpStatusCode.OK,
            $"sisa stok bahan nonaktif harus bisa dihabiskan, tidak boleh terjebak: {isi}");

        (await StokGudangAsync(owner, gudang, bahan)).Should().Be(0m);

        using (var mutasi = JsonDocument.Parse(
                   await owner.GetStringAsync($"/api/inventory/movements?locationId={gudang}&limit=200")))
        {
            mutasi.RootElement.EnumerateArray()
                .Should().Contain(m => m.GetProperty("ingredientId").GetInt32() == bahan
                                        && m.GetProperty("quantity").GetDecimal() == -5m,
                    "barang keluar dicatat di buku mutasi, bukan diam-diam hilang");
        }

        // Nilainya ikut lepas dari neraca: 5 kg × Rp 1.000 = 500.000 unit minor.
        // Entri dicari dari keterangannya karena baris jurnal hanya menyimpan akun dan nominal.
        using var jurnal = JsonDocument.Parse(await owner.GetStringAsync("/api/journal?limit=1000"));
        var entri = jurnal.RootElement.EnumerateArray()
            .FirstOrDefault(e => (e.GetProperty("description").GetString() ?? "").Contains(nama));
        entri.ValueKind.Should().Be(JsonValueKind.Object,
            "meniadakan stok bahan nonaktif harus meninggalkan jejak di buku besar");
        var kaki = entri.GetProperty("lines").EnumerateArray()
            .GroupBy(l => l.GetProperty("account").GetProperty("code").GetString())
            .ToDictionary(g => g.Key!, g => (
                Debit: g.Sum(l => l.GetProperty("debitAmount").GetInt64()),
                Kredit: g.Sum(l => l.GetProperty("creditAmount").GetInt64())));
        kaki["1-003"].Kredit.Should().Be(500_000L,
            "persediaan gudang dikurangi seharga bahan nonaktif itu, bukan nol");
        kaki.Values.Sum(v => v.Debit).Should().Be(kaki.Values.Sum(v => v.Kredit),
            "entri yang sama tetap seimbang sehingga laporan tidak bocor");
    }

    [Fact]
    public async Task LayarOpname_MenawarkanBahanNonaktifDanBolehDisetelNol()
    {
        var page = _fixture.Page;
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 5m);
        var gudang = await GudangAsync(owner);
        await NonaktifkanAsync(owner, bahan);

        await AppLogin.SignInAsync(page, _fixture.BaseUrl);
        await page.Locator("aside button:has-text('Pemeriksaan Fisik (Stok Opname)')").First.ClickAsync();
        await page.Locator("h1:has-text('Pemeriksaan Fisik (Stok Opname)')")
            .WaitForAsync(new() { Timeout = 20_000 });

        await page.Locator("main select").First.SelectOptionAsync(gudang.ToString());
        await page.Locator("main input[placeholder='Cari bahan...']").FillAsync(nama);

        var baris = page.Locator($"main tr:has-text('{nama}')");
        await baris.First.WaitForAsync(new() { Timeout = 15_000 });
        (await baris.First.Locator("td").First.InnerTextAsync())
            .Should().Contain("nonaktif di master",
                "barisnya harus jujur bahwa master bahan ini sudah dinonaktifkan");

        await baris.First.Locator("input[type='number']").FillAsync("0");
        await baris.First.Locator("button:has-text('Setel')").ClickAsync();

        await page.GetByText("stok disetel ke 0 kg")
            .First.WaitForAsync(new() { Timeout = 15_000 });
        (await StokGudangAsync(owner, gudang, bahan)).Should().Be(0m,
            "sisa stok bahan nonaktif harus benar-benar habis, bukan hanya hilang dari layar");
    }
}
