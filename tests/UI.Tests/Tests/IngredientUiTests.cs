using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// BUG-09 diuji dari layar, bukan hanya dari API: instalasi baru tidak punya bahan baku, dan dulu
/// kedua layar saling menunjuk tanpa ada tempat mengisinya. Tes ini menambah bahan lewat tab
/// Bahan Baku, membuktikan harganya tersimpan dalam unit minor yang benar, membuktikan nama kembar
/// ditolak dengan pesan di formulir, dan membuktikan ubah harga tidak menghapus stok.
/// </summary>
[Collection("UI Tests")]
public sealed class IngredientUiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public IngredientUiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private static string NamaBaru() => $"Bahan UI {Guid.NewGuid():N}"[..26];

    private static async Task BukaLayarBahanAsync(IPage page, string baseUrl)
    {
        await AppLogin.SignInAsync(page, baseUrl);
        await page.Locator("aside button:has-text('Resep & Daftar Bahan Baku')").First.ClickAsync();
        await page.Locator("button:text-is('Bahan Baku')").ClickAsync();
        await page.WaitForSelectorAsync("button:has-text('Tambah Bahan')");
    }

    private static async Task IsiFormBahanAsync(IPage page, string nama, string satuan, string harga, string stok, string minimum)
    {
        await page.FillAsync("#bahan-form input[placeholder='cth. Tepung Terigu Cakra']", nama);
        await page.FillAsync("#bahan-form input[placeholder='cth. kg']", satuan);
        await page.FillAsync("#bahan-form input[placeholder='cth. 13000']", harga);
        await page.FillAsync("#bahan-form input[type='number']:not([placeholder]) >> nth=0", stok);
        await page.FillAsync("#bahan-form input[type='number']:not([placeholder]) >> nth=1", minimum);
    }

    private static async Task<JsonElement?> CariBahanAsync(HttpClient http, string nama)
    {
        using var dok = JsonDocument.Parse(await http.GetStringAsync("/api/ingredients"));
        foreach (var bahan in dok.RootElement.EnumerateArray())
        {
            if (bahan.GetProperty("name").GetString() == nama) return bahan.Clone();
        }
        return null;
    }

    [Fact]
    public async Task TambahBahan_LewatLayar_TersimpanDenganHargaUnitMinorYangBenar()
    {
        var page = _fixture.Page;
        await BukaLayarBahanAsync(page, _fixture.BaseUrl);

        var nama = NamaBaru();
        await page.ClickAsync("button:has-text('Tambah Bahan')");
        await page.WaitForSelectorAsync("#bahan-form");
        await IsiFormBahanAsync(page, nama, "kg", "13000", "12.5", "5");
        await page.ClickAsync("button[form='bahan-form']");

        await page.WaitForSelectorAsync($"td:has-text('{nama}')", new PageWaitForSelectorOptions { Timeout = 10_000 });

        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var bahan = await CariBahanAsync(http, nama);
        bahan.Should().NotBeNull("bahan yang disimpan lewat layar harus ada di API");

        var isi = bahan!.Value;
        isi.GetProperty("unit").GetString().Should().Be("kg");
        isi.GetProperty("pricePerUnit").GetInt64().Should().Be(1_300_000,
            "layar mengisi Rupiah penuh, jadi Rp13.000 tersimpan sebagai 1.300.000 unit minor");
        isi.GetProperty("currentStock").GetDecimal().Should().Be(12.5m,
            "stok awal harus masuk lewat jalur stok resmi, bukan hanya tercatat di master");
        isi.GetProperty("minStockAlert").GetDecimal().Should().Be(5m);

        (await page.Locator($"tr:has-text('{nama}')").InnerTextAsync()).Should().Contain("Rp")
            .And.Contain("13.000", "harga di tabel dibaca kembali dari server, bukan dari isian formulir");
    }

    [Fact]
    public async Task NamaKembar_DitolakDenganPesanDiFormulir_TanpaMenambahBarisBaru()
    {
        var page = _fixture.Page;
        await BukaLayarBahanAsync(page, _fixture.BaseUrl);

        var nama = NamaBaru();
        await page.ClickAsync("button:has-text('Tambah Bahan')");
        await page.WaitForSelectorAsync("#bahan-form");
        await IsiFormBahanAsync(page, nama, "kg", "9000", "0", "0");
        await page.ClickAsync("button[form='bahan-form']");
        await page.WaitForSelectorAsync($"td:has-text('{nama}')");

        await page.ClickAsync("button:has-text('Tambah Bahan')");
        await page.WaitForSelectorAsync("#bahan-form");
        await IsiFormBahanAsync(page, nama.ToLowerInvariant(), "gram", "100", "0", "0");
        await page.ClickAsync("button[form='bahan-form']");

        // Tanda * wajib juga berwarna rose; yang dicari khusus kotak pesan galatnya.
        var galat = page.Locator("#bahan-form .text-rose-400")
            .Filter(new LocatorFilterOptions { HasTextString = "sudah dipakai" });
        await galat.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        (await galat.First.InnerTextAsync()).Should().Contain("sudah dipakai",
            "pesan server harus terbaca di formulir, bukan hilang sebagai alert");
        (await page.Locator("#bahan-form").IsVisibleAsync()).Should().BeTrue(
            "formulir tetap terbuka supaya pengguna bisa memperbaiki nama yang bentrok");

        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        using var dok = JsonDocument.Parse(await http.GetStringAsync("/api/ingredients"));
        dok.RootElement.EnumerateArray()
            .Count(b => string.Equals(b.GetProperty("name").GetString(), nama, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "penolakan terjadi sebelum baris kedua ditulis");

        await page.ClickAsync("button:has-text('Batal')");
    }

    [Fact]
    public async Task UbahHarga_DariLayar_TidakMenghapusStok()
    {
        var page = _fixture.Page;
        await BukaLayarBahanAsync(page, _fixture.BaseUrl);

        var nama = NamaBaru();
        await page.ClickAsync("button:has-text('Tambah Bahan')");
        await page.WaitForSelectorAsync("#bahan-form");
        await IsiFormBahanAsync(page, nama, "kg", "10000", "8", "2");
        await page.ClickAsync("button[form='bahan-form']");
        await page.WaitForSelectorAsync($"td:has-text('{nama}')");

        await page.Locator($"tr:has-text('{nama}') button[title='Edit bahan']").ClickAsync();
        await page.WaitForSelectorAsync("#bahan-form");
        await page.FillAsync("#bahan-form input[placeholder='cth. 13000']", "11000");
        await page.ClickAsync("button[form='bahan-form']");

        await page.WaitForSelectorAsync("#bahan-form", new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached });
        await page.WaitForSelectorAsync("td:has-text('11.000')", new PageWaitForSelectorOptions { Timeout = 10_000 });

        using var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var bahan = await CariBahanAsync(http, nama);
        bahan.Should().NotBeNull();
        bahan!.Value.GetProperty("pricePerUnit").GetInt64().Should().Be(1_100_000);
        bahan.Value.GetProperty("currentStock").GetDecimal().Should().Be(8m,
            "mengubah harga tidak boleh mengosongkan stok yang sudah tercatat");
    }
}
