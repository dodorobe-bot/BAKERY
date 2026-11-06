using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Batch F — kasus uji B5 (baris 20), temuan tambahan baris 40, dan label sumber angka baris 8/59.
/// Jumlah pesanan wajib bilangan bulat dan tidak boleh kosong; PO tanpa baris adalah kesalahan
/// masukan (400), bukan kegagalan server (500); dasbor membawa saldo 1-002 supaya selisih dengan
/// sisa tagihan pesanan bisa dijelaskan di layar yang sama.
/// </summary>
[Collection("UI Tests")]
public sealed class BatchFValidationApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public BatchFValidationApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static string NamaProduk() => $"Produk Qty {Guid.NewGuid():N}"[..26];

    private static async Task<int> ProdukAsync(HttpClient owner)
    {
        var nama = NamaProduk();
        using var respons = await owner.PostAsJsonAsync("/api/products", new
        {
            name = nama,
            category = "Uji Batch F",
            sellingPriceMinorUnits = 2_000_000,
            lines = Array.Empty<object>(),
        });
        var teks = await respons.Content.ReadAsStringAsync();
        respons.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<(HttpStatusCode Status, string ErrorCode, string ErrorMessage)> JawabanAsync(
        HttpResponseMessage respons)
    {
        var teks = await respons.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(teks) || !teks.TrimStart().StartsWith('{'))
            return (respons.StatusCode, "", teks);
        using var dok = JsonDocument.Parse(teks);
        return (respons.StatusCode,
            dok.RootElement.TryGetProperty("errorCode", out var kode) ? kode.GetString() ?? "" : "",
            dok.RootElement.TryGetProperty("errorMessage", out var pesan) ? pesan.GetString() ?? "" : "");
    }

    private static Dictionary<string, object?> Pesanan(int produk, object? jumlah, bool sertakanJumlah = true)
    {
        var muatan = new Dictionary<string, object?>
        {
            ["customerName"] = "Pelanggan Qty",
            ["description"] = "Uji jumlah pesanan",
            ["pickupDate"] = "2030-06-01T10:00:00",
            ["totalAmount"] = 2_000_000,
            ["downPayment"] = 0,
            ["productTemplateId"] = produk,
        };
        if (sertakanJumlah) muatan["quantity"] = jumlah;
        return muatan;
    }

    [Fact]
    public async Task QtyKosong_AtauTidakDikirim_Ditolak400_BukanMenjadiSatuUnit()
    {
        var owner = await OwnerAsync();
        var produk = await ProdukAsync(owner);

        foreach (var (label, muatan) in new[]
                 {
                     ("tanpa field quantity", Pesanan(produk, null, sertakanJumlah: false)),
                     ("quantity null", Pesanan(produk, null)),
                 })
        {
            using var respons = await owner.PostAsJsonAsync("/api/orders", muatan);
            var (status, kode, pesan) = await JawabanAsync(respons);
            status.Should().Be(HttpStatusCode.BadRequest, label);
            kode.Should().Be("VALIDATION_FAILED", label);
            pesan.Should().Contain("wajib diisi", label);
        }
    }

    [Fact]
    public async Task QtyPecahan_Ditolak400_SedangkanBilanganBulatDiterima()
    {
        var owner = await OwnerAsync();
        var produk = await ProdukAsync(owner);

        foreach (var pecahan in new[] { 0.5m, 1.5m, 1.0000001m })
        {
            using var respons = await owner.PostAsJsonAsync("/api/orders", Pesanan(produk, pecahan));
            var (status, kode, pesan) = await JawabanAsync(respons);
            status.Should().Be(HttpStatusCode.BadRequest, $"{pecahan} bukan satuan barang jadi");
            kode.Should().Be("VALIDATION_FAILED");
            pesan.Should().Contain("bilangan bulat");
        }

        using var diterima = await owner.PostAsJsonAsync("/api/orders", Pesanan(produk, 2));
        diterima.StatusCode.Should().Be(HttpStatusCode.Created, await diterima.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task QtyNol_Negatif_DanDiAtasBatas_Tetap400()
    {
        var owner = await OwnerAsync();
        var produk = await ProdukAsync(owner);

        foreach (var (jumlah, potonganPesan) in new[]
                 {
                     (0m, "lebih dari nol"),
                     (-1m, "lebih dari nol"),
                     (100_001m, "melebihi batas"),
                 })
        {
            using var respons = await owner.PostAsJsonAsync("/api/orders", Pesanan(produk, jumlah));
            var (status, kode, pesan) = await JawabanAsync(respons);
            status.Should().Be(HttpStatusCode.BadRequest, $"{jumlah} harus ditolak");
            kode.Should().Be("VALIDATION_FAILED");
            pesan.Should().Contain(potonganPesan);
        }
    }

    [Fact]
    public async Task PoTanpaLines_Menjawab400_Bukan500()
    {
        var owner = await OwnerAsync();

        foreach (var muatan in new object[]
                 {
                     new { supplierId = 1, notes = "tanpa lines" },
                     new { supplierId = 1, notes = "lines null", lines = (object?)null },
                 })
        {
            using var respons = await owner.PostAsJsonAsync("/api/purchases", muatan);
            var (status, kode, pesan) = await JawabanAsync(respons);
            status.Should().Be(HttpStatusCode.BadRequest);
            kode.Should().Be("VALIDATION_FAILED");
            pesan.Should().Contain("minimal satu baris");
        }
    }

    [Fact]
    public async Task Dasbor_MembawaSaldoAkunPiutangUsaha()
    {
        var owner = await OwnerAsync();

        using var respons = await owner.GetAsync("/api/dashboard");
        var teks = await respons.Content.ReadAsStringAsync();
        respons.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        using var dok = JsonDocument.Parse(teks);
        dok.RootElement.TryGetProperty("piutangUsaha", out var saldo).Should().BeTrue(
            "kartu dasbor menampilkan saldo 1-002 di samping sisa tagihan pesanan");
        saldo.ValueKind.Should().Be(JsonValueKind.Number);
        saldo.GetInt64().Should().BeGreaterThanOrEqualTo(0);
    }
}
