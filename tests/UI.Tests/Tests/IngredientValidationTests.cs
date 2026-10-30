using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Regresi BUG-09: data induk bahan baku dulu masuk apa adanya. Nama kosong, satuan kosong, dan
/// nama kembar tersimpan tanpa keluhan; harga atau stok negatif ditolak pemicu basis data lalu
/// keluar sebagai 500. Endpoint ubah juga menimpa stok menjadi nol bila badannya tidak memuat
/// currentStock, dan menjawab 200 untuk Id yang tidak ada.
///
/// Tes ini menuntut jawaban pemanggil yang bisa dibaca (400/404/409), karena layar bahan baku
/// yang baru bergantung pada pesan itu untuk memberi tahu apa yang salah.
/// </summary>
[Collection("UI Tests")]
public sealed class IngredientValidationTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public IngredientValidationTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static string NamaBaru() => $"Bahan Uji {Guid.NewGuid():N}"[..24];

    private sealed record Jawaban(HttpStatusCode Status, int Id, string ErrorCode, string ErrorMessage);

    private static Jawaban Baca(HttpStatusCode status, string teks)
    {
        if (string.IsNullOrWhiteSpace(teks) || !teks.TrimStart().StartsWith('{'))
            return new Jawaban(status, 0, "", teks);
        using var dok = JsonDocument.Parse(teks);
        var akar = dok.RootElement;
        return new Jawaban(status,
            akar.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            akar.TryGetProperty("errorCode", out var kode) ? kode.GetString() ?? "" : "",
            akar.TryGetProperty("errorMessage", out var pesan) ? pesan.GetString() ?? "" : "");
    }

    private static async Task<Jawaban> TambahAsync(HttpClient klien, object isi)
    {
        using var respons = await klien.PostAsJsonAsync("/api/ingredients", isi);
        return Baca(respons.StatusCode, await respons.Content.ReadAsStringAsync());
    }

    private static async Task<Jawaban> UbahAsync(HttpClient klien, int id, object isi)
    {
        using var respons = await klien.PutAsJsonAsync($"/api/ingredients/{id}", isi);
        return Baca(respons.StatusCode, await respons.Content.ReadAsStringAsync());
    }

    private static async Task<decimal?> StokAsync(HttpClient klien, int id)
    {
        using var dok = JsonDocument.Parse(await klien.GetStringAsync("/api/ingredients"));
        foreach (var bahan in dok.RootElement.EnumerateArray())
        {
            if (bahan.GetProperty("id").GetInt32() == id)
                return bahan.GetProperty("currentStock").GetDecimal();
        }
        return null;
    }

    [Fact]
    public async Task NamaAtauSatuanKosong_Ditolak400()
    {
        using var owner = await OwnerAsync();

        var tanpaNama = await TambahAsync(owner, new { name = "   ", unit = "kg", pricePerUnit = 1_200_00 });
        tanpaNama.Status.Should().Be(HttpStatusCode.BadRequest, "bahan tanpa nama tidak bisa dipilih di resep");
        tanpaNama.ErrorCode.Should().Be("VALIDATION_FAILED");
        tanpaNama.ErrorMessage.Should().Contain("Nama bahan");

        var satuanKosong = await TambahAsync(owner, new { name = NamaBaru(), unit = "  ", pricePerUnit = 1_200_00 });
        satuanKosong.Status.Should().Be(HttpStatusCode.BadRequest, "satuan dipakai pada setiap baris resep dan pembelian");
        satuanKosong.ErrorMessage.Should().Contain("Satuan");

        var tanpaSatuan = await TambahAsync(owner, new { name = NamaBaru(), pricePerUnit = 1_200_00 });
        tanpaSatuan.Status.Should().Be(HttpStatusCode.BadRequest);
        tanpaSatuan.ErrorMessage.Should().Contain("Satuan");
    }

    [Fact]
    public async Task HargaStokAtauBatasMinimumNegatif_Ditolak400BukanGalatServer()
    {
        using var owner = await OwnerAsync();

        var hargaNegatif = await TambahAsync(owner, new { name = NamaBaru(), unit = "kg", pricePerUnit = -1 });
        hargaNegatif.Status.Should().Be(HttpStatusCode.BadRequest,
            "nilai negatif adalah kesalahan pemanggil, bukan kegagalan server");
        hargaNegatif.ErrorCode.Should().Be("VALIDATION_FAILED");
        hargaNegatif.ErrorMessage.Should().Contain("Harga");

        var stokNegatif = await TambahAsync(owner, new { name = NamaBaru(), unit = "kg", pricePerUnit = 0, currentStock = -2.5 });
        stokNegatif.Status.Should().Be(HttpStatusCode.BadRequest);
        stokNegatif.ErrorMessage.Should().Contain("Stok");

        var minimumNegatif = await TambahAsync(owner, new { name = NamaBaru(), unit = "kg", pricePerUnit = 0, minStockAlert = -1 });
        minimumNegatif.Status.Should().Be(HttpStatusCode.BadRequest);
        minimumNegatif.ErrorMessage.Should().Contain("minimum");
    }

    [Fact]
    public async Task NamaSama_Ditolak409DanTidakMenggandakanBahan()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();

        var pertama = await TambahAsync(owner, new { name = nama, unit = "kg", pricePerUnit = 1_000_00 });
        pertama.Status.Should().Be(HttpStatusCode.Created, pertama.ErrorMessage);
        pertama.Id.Should().BeGreaterThan(0);

        // Nama kembar membuat dua baris resep menunjuk bahan yang tidak bisa dibedakan di layar
        // maupun di laporan; spasi dan huruf besar-kecil tidak boleh dipakai untuk mengelak.
        var kembar = await TambahAsync(owner, new { name = $"  {nama.ToLowerInvariant()}  ", unit = "gram", pricePerUnit = 900_00 });
        kembar.Status.Should().Be(HttpStatusCode.Conflict);
        kembar.ErrorCode.Should().Be("DUPLICATE_KEY");
        kembar.ErrorMessage.Should().Contain("sudah");

        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/ingredients"));
        var sejenis = dok.RootElement.EnumerateArray()
            .Count(b => string.Equals(b.GetProperty("name").GetString(), nama, StringComparison.OrdinalIgnoreCase));
        sejenis.Should().Be(1, "penolakan harus terjadi sebelum baris kedua ditulis");
    }

    [Fact]
    public async Task DuaBelasKirimanBersamaan_NamaSama_HanyaSatuTersimpan()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();

        // Dua kasir menekan simpan pada nama yang sama hampir bersamaan. Pemeriksaan nama yang
        // berdiri sendiri dari penyimpanan bisa lolos di kedua permintaan; kuncinya adalah cek dan
        // sisip berada di satu transaksi tulis.
        var kiriman = Enumerable.Range(0, 12)
            .Select(_ => TambahAsync(owner, new { name = nama, unit = "kg", pricePerUnit = 1_000_00 }))
            .ToArray();
        var jawaban = await Task.WhenAll(kiriman);

        jawaban.Count(j => j.Status == HttpStatusCode.Created).Should().Be(1,
            $"tepat satu kiriman yang boleh membuat bahan; status: {string.Join(", ", jawaban.Select(j => (int)j.Status))}");
        jawaban.Count(j => j.Status == HttpStatusCode.Conflict).Should().Be(11);

        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/ingredients"));
        dok.RootElement.EnumerateArray()
            .Count(b => string.Equals(b.GetProperty("name").GetString(), nama, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "nama kembar tidak boleh tersimpan walau dikirim bersamaan");
    }

    [Fact]
    public async Task UbahTanpaMenyertakanStok_TidakMenghapusStok()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();

        var buat = await TambahAsync(owner, new { name = nama, unit = "kg", pricePerUnit = 1_500_00, currentStock = 7.5 });
        buat.Status.Should().Be(HttpStatusCode.Created, buat.ErrorMessage);
        (await StokAsync(owner, buat.Id)).Should().Be(7.5m);

        // Formulir ubah harga kerap tidak mengirim stok sama sekali. Diam-diam membacanya sebagai
        // nol akan menghapus persediaan yang sudah tercatat di buku besar.
        var ubah = await UbahAsync(owner, buat.Id, new { name = nama, unit = "kg", pricePerUnit = 1_750_00 });
        ubah.Status.Should().Be(HttpStatusCode.OK, ubah.ErrorMessage);
        (await StokAsync(owner, buat.Id)).Should().Be(7.5m,
            "permintaan tanpa currentStock tidak boleh mengubah stok");

        var ubahNamaKosong = await UbahAsync(owner, buat.Id, new { name = " ", unit = "kg", pricePerUnit = 1_750_00 });
        ubahNamaKosong.Status.Should().Be(HttpStatusCode.BadRequest);
        ubahNamaKosong.ErrorCode.Should().Be("VALIDATION_FAILED");

        var ubahTidakAda = await UbahAsync(owner, 999_999, new { name = NamaBaru(), unit = "kg", pricePerUnit = 1_000_00 });
        ubahTidakAda.Status.Should().Be(HttpStatusCode.NotFound,
            "endpoint ubah yang menjawab 200 untuk Id tak dikenal membuat layar melaporkan perubahan yang tidak terjadi");
    }

    [Fact]
    public async Task UbahHarga_TercatatDiJejakAuditDenganNilaiLama()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();
        var buat = await TambahAsync(owner, new { name = nama, unit = "kg", pricePerUnit = 1_000_00 });
        buat.Status.Should().Be(HttpStatusCode.Created, buat.ErrorMessage);

        var ubah = await UbahAsync(owner, buat.Id, new { name = nama, unit = "kg", pricePerUnit = 1_400_00 });
        ubah.Status.Should().Be(HttpStatusCode.OK, ubah.ErrorMessage);

        // Harga bahan menentukan HPP semua produk; "dari berapa menjadi berapa" harus terbaca.
        using var dok = JsonDocument.Parse(
            await owner.GetStringAsync($"/api/audit?entity=Ingredient&entityId={buat.Id}&limit=20"));
        var catatan = dok.RootElement.EnumerateArray().ToList();
        catatan.Should().Contain(a => a.GetProperty("action").GetString() == "ubah bahan baku");
        var detail = catatan.First(a => a.GetProperty("action").GetString() == "ubah bahan baku");
        detail.GetProperty("oldValues").GetString().Should().Contain("100000");
        detail.GetProperty("newValues").GetString().Should().Contain("140000");
    }

    [Fact]
    public async Task Kasir_TidakBolehMenambahMengubahAtauMenonaktifkanBahan()
    {
        using var owner = await OwnerAsync();
        using var kasir = await KlienPegawaiAsync(owner);

        var tambah = await TambahAsync(kasir, new { name = NamaBaru(), unit = "kg", pricePerUnit = 1_000_00 });
        tambah.Status.Should().Be(HttpStatusCode.Forbidden);

        var buat = await TambahAsync(owner, new { name = NamaBaru(), unit = "kg", pricePerUnit = 1_000_00 });
        buat.Status.Should().Be(HttpStatusCode.Created, buat.ErrorMessage);

        var ubah = await UbahAsync(kasir, buat.Id, new { name = "Diubah Kasir", unit = "kg", pricePerUnit = 1L });
        ubah.Status.Should().Be(HttpStatusCode.Forbidden);

        using var hapus = await kasir.DeleteAsync($"/api/ingredients/{buat.Id}");
        hapus.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await StokAsync(owner, buat.Id)).Should().NotBeNull("bahan tidak boleh hilang karena percobaan kasir");
    }

    /// <summary>Akun kasir sungguhan lewat API produksi, termasuk melewati gerbang sandi bawaan.</summary>
    private static async Task<HttpClient> KlienPegawaiAsync(HttpClient owner)
    {
        const string sandiAwal = "SandiSementara1";
        const string sandiBaru = "SandiHarianKasir1";
        var username = $"ujibahan{Guid.NewGuid():N}"[..24];

        using var buat = await owner.PostAsJsonAsync("/api/users", new { username, password = sandiAwal, role = "Pegawai" });
        buat.StatusCode.Should().Be(HttpStatusCode.Created, await buat.Content.ReadAsStringAsync());

        var tokenBawaan = await TokenAsync(owner, username, sandiAwal);
        using (var klien = new HttpClient { BaseAddress = owner.BaseAddress! })
        {
            klien.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenBawaan);
            var ganti = await klien.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = sandiAwal, newPassword = sandiBaru });
            ganti.StatusCode.Should().Be(HttpStatusCode.OK, await ganti.Content.ReadAsStringAsync());
        }

        var token = await TokenAsync(owner, username, sandiBaru);
        var kasir = new HttpClient { BaseAddress = owner.BaseAddress! };
        kasir.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return kasir;
    }

    private static async Task<string> TokenAsync(HttpClient induk, string username, string password)
    {
        using var http = new HttpClient { BaseAddress = induk.BaseAddress! };
        using var respons = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
        var teks = await respons.Content.ReadAsStringAsync();
        respons.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("token").GetString()!;
    }
}
