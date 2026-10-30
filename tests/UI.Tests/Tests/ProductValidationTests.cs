using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Regresi P3: produk tanpa daftar resep dulu berakhir 500 karena referensi null. Data induk
/// yang cacat harus ditolak dengan 400 dan pesan yang bisa dibaca, bukan galat server.
/// </summary>
[Collection("UI Tests")]
public sealed class ProductValidationTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public ProductValidationTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static string NamaBaru() => $"Produk Uji {Guid.NewGuid():N}"[..28];

    private static async Task<(HttpStatusCode Status, string ErrorCode, string ErrorMessage)> KirimAsync(
        HttpClient owner, object isi)
    {
        using var respons = await owner.PostAsJsonAsync("/api/products", isi);
        var teks = await respons.Content.ReadAsStringAsync();
        if (respons.StatusCode == HttpStatusCode.OK)
            return (respons.StatusCode, "", teks);
        using var dok = JsonDocument.Parse(teks);
        return (respons.StatusCode,
            dok.RootElement.GetProperty("errorCode").GetString() ?? "",
            dok.RootElement.GetProperty("errorMessage").GetString() ?? "");
    }

    [Fact]
    public async Task TanpaDaftarResep_Ditolak400Bukan500()
    {
        using var owner = await OwnerAsync();

        var (status, kode, pesan) = await KirimAsync(owner, new
        {
            name = NamaBaru(),
            sellingPriceMinorUnits = 500_000,
        });

        status.Should().Be(HttpStatusCode.BadRequest, "masukan yang tidak lengkap adalah galat pemanggil, bukan galat server");
        kode.Should().Be("VALIDATION_FAILED");
        pesan.Should().Contain("resep");
    }

    [Fact]
    public async Task NamaKosong_HargaNegatif_DanJumlahBahanTidakSah_Ditolak400()
    {
        using var owner = await OwnerAsync();

        var tanpaNama = await KirimAsync(owner, new
        {
            name = "   ",
            sellingPriceMinorUnits = 500_000,
            lines = Array.Empty<object>(),
        });
        tanpaNama.Status.Should().Be(HttpStatusCode.BadRequest);
        tanpaNama.ErrorMessage.Should().Contain("Nama produk");

        var hargaNegatif = await KirimAsync(owner, new
        {
            name = NamaBaru(),
            sellingPriceMinorUnits = -1,
            lines = Array.Empty<object>(),
        });
        hargaNegatif.Status.Should().Be(HttpStatusCode.BadRequest);
        hargaNegatif.ErrorMessage.Should().Contain("Harga jual");

        var jumlahNol = await KirimAsync(owner, new
        {
            name = NamaBaru(),
            sellingPriceMinorUnits = 500_000,
            lines = new[] { new { ingredientId = 1, quantity = 0 } },
        });
        jumlahNol.Status.Should().Be(HttpStatusCode.BadRequest);
        jumlahNol.ErrorMessage.Should().Contain("Jumlah bahan");
    }

    [Fact]
    public async Task ProdukSah_TetapDiterima_DanUbahTanpaResepJugaDitolak400()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();

        using var buat = await owner.PostAsJsonAsync("/api/products", new
        {
            name = nama,
            category = "Uji",
            sellingPriceMinorUnits = 750_000,
            lines = Array.Empty<object>(),
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        using var dok = JsonDocument.Parse(teks);
        var id = dok.RootElement.GetProperty("id").GetInt32();
        id.Should().BeGreaterThan(0);

        // Jalur ubah memakai DTO yang sama; tanpa daftar resep harus 400 juga, bukan 500.
        using var ubah = await owner.PutAsJsonAsync($"/api/products/{id}", new
        {
            name = nama,
            category = "Uji",
            sellingPriceMinorUnits = 750_000,
        });
        var teksUbah = await ubah.Content.ReadAsStringAsync();
        ubah.StatusCode.Should().Be(HttpStatusCode.BadRequest, teksUbah);
        using var dokUbah = JsonDocument.Parse(teksUbah);
        dokUbah.RootElement.GetProperty("errorCode").GetString().Should().Be("VALIDATION_FAILED");
    }
}
