using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Nama toko, slogan, dan logo bisa diganti kapan saja lewat Pengaturan. Tes ini menjaga jalur
/// yang membuat janji itu benar: layar masuk harus bisa membaca merek tanpa sesi, ubah merek
/// dengan isian tidak sah harus ditolak tanpa menyimpan separuh, pegawai tidak boleh mengganti
/// merek, dan jejak audit tidak boleh menyimpan gambar utuh.
/// </summary>
[Collection("UI Tests")]
public sealed class BrandSettingsTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public BrandSettingsTests(PlaywrightFixture fixture) => _fixture = fixture;

    private const string NamaUji = "Toko Roti Bu Ratna";
    private const string SloganUji = "Roti hangat setiap hari";
    private const string PngSatuPiksel =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static string LogoDataUrl => $"data:image/png;base64,{PngSatuPiksel}";

    private static async Task<JsonElement> BacaMerekAsync(HttpClient klien)
    {
        using var dok = JsonDocument.Parse(await klien.GetStringAsync("/api/brand"));
        return dok.RootElement.Clone();
    }

    // Kembalikan merek ke keadaan sebelum uji supaya tes lain di basis data yang sama tidak
    // membaca nama tokonya sendiri.
    private static async Task PulihkanMerekAsync(HttpClient owner, JsonElement sebelum)
    {
        using var pulih = await owner.PutAsJsonAsync("/api/settings", new
        {
            brand_name = sebelum.GetProperty("name").GetString(),
            brand_tagline = sebelum.GetProperty("tagline").GetString(),
            brand_logo = sebelum.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.String
                ? logo.GetString()
                : "",
        });
        pulih.IsSuccessStatusCode.Should().BeTrue(await pulih.Content.ReadAsStringAsync());
    }

    private static async Task<(int Id, string Token)> BuatPegawaiAsync(HttpClient owner)
    {
        var username = $"ujibrand{Guid.NewGuid():N}"[..24];
        using var buat = await owner.PostAsJsonAsync("/api/users",
            new { username, password = "SandiSementara1", role = "Pegawai" });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        var id = dok.RootElement.GetProperty("id").GetInt32();

        using var http = new HttpClient { BaseAddress = owner.BaseAddress! };
        using var masuk = await http.PostAsJsonAsync("/api/auth/login",
            new { username, password = "SandiSementara1" });
        using var sesi = JsonDocument.Parse(await masuk.Content.ReadAsStringAsync());
        var tokenBawaan = sesi.RootElement.GetProperty("token").GetString()!;

        using var klienBawaan = new HttpClient { BaseAddress = owner.BaseAddress! };
        klienBawaan.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenBawaan);
        using var ganti = await klienBawaan.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "SandiSementara1", newPassword = "SandiHarianKasir1" });
        ganti.IsSuccessStatusCode.Should().BeTrue(await ganti.Content.ReadAsStringAsync());

        using var masukLagi = await http.PostAsJsonAsync("/api/auth/login",
            new { username, password = "SandiHarianKasir1" });
        using var sesiLagi = JsonDocument.Parse(await masukLagi.Content.ReadAsStringAsync());
        return (id, sesiLagi.RootElement.GetProperty("token").GetString()!);
    }

    [Fact]
    public async Task Merek_Publik_TerbacaTanpaSesiDanHanyaTigaKunci()
    {
        using var anonim = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
        using var respons = await anonim.GetAsync("/api/brand");

        respons.StatusCode.Should().Be(HttpStatusCode.OK, "layar masuk tidak punya token saat menggambar merek");

        using var dok = JsonDocument.Parse(await respons.Content.ReadAsStringAsync());
        var akar = dok.RootElement;
        akar.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        akar.GetProperty("tagline").GetString().Should().NotBeNullOrWhiteSpace();

        var kunci = akar.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        kunci.Should().Equal(["logo", "name", "outlet", "tagline"],
            "endpoint anonim tidak boleh membocorkan pengaturan lain seperti printer atau biaya");
    }

    [Fact]
    public async Task UbahNamaDanSlogan_LangsungTerbacaDiLayarMasuk()
    {
        using var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var sebelum = await BacaMerekAsync(owner);
        try
        {
            using var ubah = await owner.PutAsJsonAsync("/api/settings",
                new { brand_name = NamaUji, brand_tagline = SloganUji });
            ubah.StatusCode.Should().Be(HttpStatusCode.OK, await ubah.Content.ReadAsStringAsync());

            using var anonim = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
            var sesudah = await BacaMerekAsync(anonim);
            sesudah.GetProperty("name").GetString().Should().Be(NamaUji);
            sesudah.GetProperty("tagline").GetString().Should().Be(SloganUji);
        }
        finally
        {
            await PulihkanMerekAsync(owner, sebelum);
        }
    }

    [Fact]
    public async Task NamaKosong_DitolakDanTidakMenyimpanSloganSekalipun()
    {
        using var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var sebelum = await BacaMerekAsync(owner);

        using var ubah = await owner.PutAsJsonAsync("/api/settings",
            new { brand_name = "   ", brand_tagline = "Slogan Yang Tidak Boleh Tersimpan" });
        var teks = await ubah.Content.ReadAsStringAsync();
        ubah.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        teks.Should().Contain("VALIDATION_FAILED").And.Contain("tidak boleh kosong");

        var sesudah = await BacaMerekAsync(owner);
        sesudah.GetProperty("name").GetString().Should().Be(sebelum.GetProperty("name").GetString());
        sesudah.GetProperty("tagline").GetString().Should().Be(sebelum.GetProperty("tagline").GetString(),
            "penolakan terjadi sebelum satu kunci pun ditulis");
    }

    [Theory]
    [InlineData("data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", "PNG, JPEG, atau WebP")]
    [InlineData("data:image/png;base64,QUJD", "tidak cocok")]
    [InlineData("https://contoh.id/logo.png", "PNG, JPEG, atau WebP")]
    [InlineData("", null)]
    public async Task Logo_TidakSahDitolak_KosongBerartiHapus(string logo, string? potonganPesan)
    {
        using var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var sebelum = await BacaMerekAsync(owner);
        try
        {
            using var ubah = await owner.PutAsJsonAsync("/api/settings", new { brand_logo = logo });
            var teks = await ubah.Content.ReadAsStringAsync();

            if (potonganPesan is null)
            {
                ubah.StatusCode.Should().Be(HttpStatusCode.OK, teks);
                var kosong = await BacaMerekAsync(owner);
                kosong.GetProperty("logo").ValueKind.Should().Be(JsonValueKind.Null,
                    "nilai kosong menghapus logo dan kotak merek kembali memakai inisial");
            }
            else
            {
                ubah.StatusCode.Should().Be(HttpStatusCode.BadRequest, teks);
                teks.Should().Contain(potonganPesan);
            }
        }
        finally
        {
            await PulihkanMerekAsync(owner, sebelum);
        }
    }

    [Fact]
    public async Task LogoSah_TersimpanDanJejakAuditTidakMemuatGambarUtuh()
    {
        using var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var sebelum = await BacaMerekAsync(owner);
        try
        {
            using var ubah = await owner.PutAsJsonAsync("/api/settings", new { brand_logo = LogoDataUrl });
            ubah.StatusCode.Should().Be(HttpStatusCode.OK, await ubah.Content.ReadAsStringAsync());

            var sesudah = await BacaMerekAsync(owner);
            sesudah.GetProperty("logo").GetString().Should().Be(LogoDataUrl);

            using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/audit?entity=AppSetting&limit=20"));
            var terbaru = dok.RootElement.EnumerateArray()
                .First(a => (a.GetProperty("newValues").GetString() ?? "").Contains("brand_logo"));
            var catatan = terbaru.GetProperty("oldValues").GetString() + terbaru.GetProperty("newValues").GetString();
            catatan.Should().Contain("logo terpasang");
            catatan.Should().NotContain(PngSatuPiksel,
                "jejak audit tidak boleh menyimpan data gambar; satu unggahan 512 KB akan menggandakan basis data");
        }
        finally
        {
            await PulihkanMerekAsync(owner, sebelum);
        }
    }

    [Fact]
    public async Task Pegawai_TidakBisaMenggantiMerek()
    {
        using var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var (_, token) = await BuatPegawaiAsync(owner);
        using var kasir = new HttpClient { BaseAddress = owner.BaseAddress! };
        kasir.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var ubah = await kasir.PutAsJsonAsync("/api/settings", new { brand_name = "Toko Kasir" });
        ubah.StatusCode.Should().Be(HttpStatusCode.Forbidden, await ubah.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GantiMerekLewatLayar_LangsungTampilDiMenuJudulJendelaDanLayarMasuk()
    {
        var page = _fixture.Page;
        using var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var sebelum = await BacaMerekAsync(owner);
        var namaBaru = $"Dapur Uji {Guid.NewGuid():N}"[..20];
        try
        {
            await AppLogin.SignInAsync(page, _fixture.BaseUrl);
            await page.Locator("aside button:has-text('Pengaturan Toko & Cabang')").First.ClickAsync();
            await page.WaitForSelectorAsync("button:has-text('Simpan Pengaturan')");

            await page.FillAsync("input[placeholder='Seruni 22 Bakery']", namaBaru);
            await page.FillAsync("input[placeholder='Sistem Manajemen Toko Kue']", SloganUji);

            // Logo dipilih dari berkas sungguhan: peramban memperkecil dan mengubahnya ke data URL,
            // server memeriksa jenis gambarnya, lalu kotak merek memakai gambar itu alih-alih inisial.
            await page.SetInputFilesAsync("input[type='file']", new FilePayload
            {
                Name = "logo-uji.png",
                MimeType = "image/png",
                Buffer = Convert.FromBase64String(PngSatuPiksel),
            });
            await page.WaitForSelectorAsync("img[alt='Pratinjau logo toko']");

            await page.ClickAsync("button:has-text('Simpan Pengaturan')");
            await page.WaitForSelectorAsync("div:has-text('Tersimpan')", new PageWaitForSelectorOptions
            {
                Timeout = 10_000,
                State = WaitForSelectorState.Visible,
            });

            (await page.Locator("aside").InnerTextAsync()).Should().Contain(namaBaru)
                .And.Contain(SloganUji, "bilah samping membaca merek yang baru disimpan");
            (await page.TitleAsync()).Should().Be(namaBaru, "judul jendela mengikuti nama toko");
            (await page.Locator("aside img").CountAsync()).Should().BeGreaterThan(0,
                "kotak merek memakai logo unggahan, bukan inisial");

            await page.ReloadAsync();
            await page.WaitForSelectorAsync("aside");
            (await page.Locator("aside").InnerTextAsync()).Should().Contain(namaBaru,
                "merek tersimpan di server, bukan hanya di layar yang sedang terbuka");
        }
        finally
        {
            await PulihkanMerekAsync(owner, sebelum);
            await page.EvaluateAsync("localStorage.clear()");
            await page.GotoAsync(_fixture.BaseUrl);
        }
    }
}
