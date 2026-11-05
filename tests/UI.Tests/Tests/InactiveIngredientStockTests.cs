using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Dua aturan sekaligus, satu tujuan: barang yang dihitung di neraca tidak boleh kehilangan
/// induknya, dan induk tidak boleh dihapus sementara barangnya masih ada.
///
/// Aturan pertama (penolakan hapus): tombol sampah di layar Bahan Baku dulu boleh menekan bahan
/// yang raknya masih terisi. Stoknya tetap tampil di Inventaris tetapi harganya hilang — layar
/// hanya mengambil harga dari daftar bahan aktif — dan sisanya tidak bisa dihabiskan dari layar
/// mana pun, sehingga nilainya terjebak selamanya di akun persediaan. Sekarang server menolak
/// dan menyebut rak mana yang masih terisi, jadi pengguna mengosongkannya lebih dulu lewat jalur
/// yang mencatat jurnal.
///
/// Aturan kedua (baris stok tetap terbaca): stok yang sudah terlanjur yatim dari data lama harus
/// tetap membawa harga master dan tetap bisa dihabiskan, kalau tidak neraca lama tidak bisa
/// dirapikan sama sekali.
///
/// Tes memakai urutan: tambah bahan (stok 0) → nonaktifkan → setel stok. Penyetelan stok tidak
/// menyaring bahan aktif, jadi keadaan "master nonaktif masih berstok" bisa dibangkitkan tanpa
/// menabrak penolakan hapus. Setiap tes yang membuatnya selalu mengosongkan raknya lagi, karena
/// DB kumpulan tes ini dipakai bersama dan pemeriksaan basis data menuntut tidak ada stok
/// menggantung di master nonaktif.
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

    private static async Task SetelStokAsync(HttpClient owner, int gudang, int bahan, decimal jumlah, string alasan)
    {
        var setel = await owner.PostAsJsonAsync("/api/inventory/adjust", new
        {
            locationId = gudang,
            ingredientId = bahan,
            newQuantity = jumlah,
            reason = alasan,
        });
        setel.StatusCode.Should().Be(HttpStatusCode.OK, await setel.Content.ReadAsStringAsync());
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

    private static async Task<bool> AdaDiDaftarBahanAktifAsync(HttpClient owner, string nama)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/ingredients"));
        return dok.RootElement.EnumerateArray().Any(p => p.GetProperty("name").GetString() == nama);
    }

    [Fact]
    public async Task BahanMasihBerstok_DitolakSaatDinonaktifkan()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 5m);
        var gudang = await GudangAsync(owner);

        var hapus = await owner.DeleteAsync($"/api/ingredients/{bahan}");
        var isi = await hapus.Content.ReadAsStringAsync();
        hapus.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"rak yang masih terisi tidak boleh kehilangan induknya: {isi}");
        isi.Should().Contain(nama)
            .And.Contain("Gudang Utama Bahan Baku")
            .And.Contain("masih punya stok")
            .And.Contain("Habiskan lebih dulu",
            "penolakan harus menyebut bahan, lokasi dan jalurnya, bukan hanya 'tidak bisa dihapus'");

        (await AdaDiDaftarBahanAktifAsync(owner, nama)).Should().BeTrue(
            "percobaan hapus yang ditolak tidak boleh mengubah apa pun");
        (await StokGudangAsync(owner, gudang, bahan)).Should().Be(5m);

        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/system/self-check"));
        dok.RootElement.GetProperty("status").GetString().Should().Be("ok", isi);
    }

    [Fact]
    public async Task SetelahRakKosong_BahanBolehDinonaktifkan()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 5m);
        var gudang = await GudangAsync(owner);

        await SetelStokAsync(owner, gudang, bahan, 0m, "Mengosongkan rak sebelum menonaktifkan");

        await NonaktifkanAsync(owner, bahan);

        (await AdaDiDaftarBahanAktifAsync(owner, nama)).Should().BeFalse();
        (await StokGudangAsync(owner, gudang, bahan)).Should().Be(0m,
            "yang dinonaktifkan adalah induknya, riwayat stoknya tetap terbaca");
    }

    [Fact]
    public async Task BahanDinonaktifkan_BarisStoknyaTetapMembawaHargaMaster()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 0m);
        var gudang = await GudangAsync(owner);

        await NonaktifkanAsync(owner, bahan);
        await SetelStokAsync(owner, gudang, bahan, 5m, "Sisa stok lama di rak gudang");

        // Kontrak lama tidak berubah: master nonaktif tidak lagi ikut daftar bahan.
        (await AdaDiDaftarBahanAktifAsync(owner, nama)).Should().BeFalse(
            "bahan yang sudah dinonaktifkan tidak boleh muncul lagi di pilihan resep");

        var baris = await BarisStokAsync(owner, gudang, bahan);
        baris.ValueKind.Should().Be(JsonValueKind.Object,
            "barangnya masih ada di rak, jadi baris stoknya tidak boleh hilang");
        baris.GetProperty("quantity").GetDecimal().Should().Be(5m);
        baris.GetProperty("ingredientIsActive").GetBoolean().Should().BeFalse();
        baris.GetProperty("ingredientPricePerUnit").GetInt64().Should().Be(100_000,
            "harga dibaca dari tabel bahan itu sendiri, bukan dari daftar bahan aktif, supaya "
            + "nilai stok tidak menyurut jadi nol hanya karena master-nya nonaktif");

        await SetelStokAsync(owner, gudang, bahan, 0m, "Merapikan sisa stok di akhir tes");
    }

    [Fact]
    public async Task SisaStokBahanNonaktif_DapatDihabiskanLewatPenyetelanStok()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 0m);
        var gudang = await GudangAsync(owner);

        await NonaktifkanAsync(owner, bahan);
        await SetelStokAsync(owner, gudang, bahan, 5m, "Sisa stok lama di rak gudang");

        await SetelStokAsync(owner, gudang, bahan, 0m, "Menghabiskan sisa bahan nonaktif");

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
        // Barang ini masuk lalu keluar lagi, jadi total akun persediaan untuk kedua entri harus
        // saling menghapus — kalau salah satu jurnal hilang, selisihnya tinggal di neraca.
        using var jurnal = JsonDocument.Parse(await owner.GetStringAsync("/api/journal?limit=1000"));
        var entri = jurnal.RootElement.EnumerateArray()
            .Where(e => (e.GetProperty("description").GetString() ?? "").Contains(nama))
            .ToList();
        entri.Should().NotBeEmpty("menambah dan menghabiskan stok bahan nonaktif harus meninggalkan jejak di buku besar");

        long debitPersediaan = 0, kreditPersediaan = 0;
        foreach (var e in entri)
        {
            var kaki = e.GetProperty("lines").EnumerateArray()
                .GroupBy(l => l.GetProperty("account").GetProperty("code").GetString())
                .ToDictionary(g => g.Key!, g => (
                    Debit: g.Sum(l => l.GetProperty("debitAmount").GetInt64()),
                    Kredit: g.Sum(l => l.GetProperty("creditAmount").GetInt64())));
            kaki.Values.Sum(v => v.Debit).Should().Be(kaki.Values.Sum(v => v.Kredit),
                $"entri \"{e.GetProperty("description").GetString()}\" harus seimbang");
            debitPersediaan += kaki.TryGetValue("1-003", out var p) ? p.Debit : 0;
            kreditPersediaan += kaki.TryGetValue("1-003", out var q) ? q.Kredit : 0;
        }

        kreditPersediaan.Should().Be(500_000L,
            "persediaan gudang dikurangi seharga bahan nonaktif itu, bukan nol");
        debitPersediaan.Should().Be(kreditPersediaan,
            "barang yang masuk dan keluar lagi tidak boleh meninggalkan saldo di akun persediaan");
    }

    [Fact]
    public async Task LayarOpname_MenawarkanBahanNonaktifDanBolehDisetelNol()
    {
        var page = _fixture.Page;
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 0m);
        var gudang = await GudangAsync(owner);
        await NonaktifkanAsync(owner, bahan);
        await SetelStokAsync(owner, gudang, bahan, 5m, "Sisa stok lama di rak gudang");

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

    [Fact]
    public async Task LayarBahanBaku_PenolakanHapusDitampilkanBesertaAlasannya()
    {
        var page = _fixture.Page;
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var bahan = await BuatBahanAsync(owner, nama, 100_000, 5m);
        var gudang = await GudangAsync(owner);

        // Confirm() di browser dibuang Playwright kalau tidak ada penanganannya, jadi harus diterima.
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await AppLogin.SignInAsync(page, _fixture.BaseUrl);
        await page.Locator("aside button:has-text('Resep & Daftar Bahan Baku')").First.ClickAsync();
        await page.Locator("button:text-is('Bahan Baku')").ClickAsync();
        await page.WaitForSelectorAsync("button:has-text('Tambah Bahan')");

        await page.Locator("main input[placeholder='Cari nama, merek, atau satuan...']").FillAsync(nama);

        var baris = page.Locator($"main tr:has-text('{nama}')");
        await baris.First.WaitForAsync(new() { Timeout = 15_000 });
        await baris.First.Locator("button[title='Nonaktifkan bahan']").ClickAsync();

        await page.GetByText("masih punya stok")
            .First.WaitForAsync(new() { Timeout = 15_000 });

        (await AdaDiDaftarBahanAktifAsync(owner, nama)).Should().BeTrue(
            "penolakan yang ditampilkan di layar harus sama dengan keadaan di server");
        (await StokGudangAsync(owner, gudang, bahan)).Should().Be(5m);

        // Rapikan: rak dikosongkan lebih dulu, baru induknya boleh dinonaktifkan.
        await SetelStokAsync(owner, gudang, bahan, 0m, "Merapikan stok di akhir tes");
        await NonaktifkanAsync(owner, bahan);
    }
}
