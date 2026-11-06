using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Batch E — kasus uji C7 (baris 38): bahan bertarif Rp 12.000/kg dipakai resep "250 g".
/// Sebelum konversi satuan, 250 dibaca sebagai 250 kg dan HPP menjadi Rp 3.000.000. Server
/// harus menerjemahkan satuan baris resep ke satuan stok sebelum menghitung HPP, rencana SPK,
/// dan pemakaian bahan.
/// </summary>
[Collection("UI Tests")]
public sealed class IngredientUnitApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public IngredientUnitApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static string NamaBahan() => $"Bahan Satuan {Guid.NewGuid():N}"[..24];

    private static string NamaProduk() => $"Produk Satuan {Guid.NewGuid():N}"[..27];

    private static object Satuan(string unit, decimal faktor) => new { unit, factorToBase = faktor };

    private static async Task<int> BahanAsync(
        HttpClient owner, string nama, string satuan, long harga, object[]? units = null)
    {
        using var respons = await owner.PostAsJsonAsync("/api/ingredients", new
        {
            name = nama,
            unit = satuan,
            pricePerUnit = harga,
            units = units ?? Array.Empty<object>(),
        });
        var teks = await respons.Content.ReadAsStringAsync();
        respons.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<int> ProdukAsync(
        HttpClient owner, string nama, long hargaJual, params object[] lines)
    {
        using var respons = await owner.PostAsJsonAsync("/api/products", new
        {
            name = nama,
            sellingPriceMinorUnits = hargaJual,
            lines,
        });
        var teks = await respons.Content.ReadAsStringAsync();
        respons.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement> CariBahanAsync(HttpClient owner, int id)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/ingredients"));
        return dok.RootElement.EnumerateArray()
            .First(b => b.GetProperty("id").GetInt32() == id)
            .Clone();
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

    [Fact]
    public async Task Resep250Gram_Setara0Komma25Kilogram_HppRp3RibuBukanRp3Juta()
    {
        using var owner = await OwnerAsync();
        var bahan = await BahanAsync(owner, NamaBahan(), "kg", 1_200_000, new[]
        {
            Satuan("g", 0.001m),
            Satuan("bks", 0.5m),
        });

        var terdaftar = await CariBahanAsync(owner, bahan);
        terdaftar.GetProperty("additionalUnits").EnumerateArray()
            .Select(u => u.GetProperty("unit").GetString())
            .Should().BeEquivalentTo(new[] { "g", "bks" },
                "satuan turunan harus terbaca kembali dari daftar bahan");

        var produk = await ProdukAsync(owner, NamaProduk(), 500_000,
            new { ingredientId = bahan, quantity = 250m, unit = "g" });

        using var detail = JsonDocument.Parse(await owner.GetStringAsync($"/api/products/{produk}"));
        detail.RootElement.GetProperty("hpp").GetInt64().Should().Be(300_000,
            "250 g = 0,25 kg × Rp 12.000 = Rp 3.000; tanpa konversi menjadi 250 kg = Rp 3.000.000");

        var baris = detail.RootElement.GetProperty("recipeLines")[0];
        baris.GetProperty("unit").GetString().Should().Be("g");
        baris.GetProperty("baseQuantity").GetDecimal().Should().Be(0.25m);
    }

    [Fact]
    public async Task SatuanTakTerdaftar_PadaBarisResep_Ditolak400()
    {
        using var owner = await OwnerAsync();
        var bahan = await BahanAsync(owner, NamaBahan(), "kg", 1_200_000);

        using var respons = await owner.PostAsJsonAsync("/api/products", new
        {
            name = NamaProduk(),
            sellingPriceMinorUnits = 500_000,
            lines = new[] { new { ingredientId = bahan, quantity = 250m, unit = "grm" } },
        });
        var (status, kode, pesan) = await JawabanAsync(respons);

        // Salah ketik satuan tidak boleh jatuh ke faktor 1 (dibaca seperti kilogram).
        status.Should().Be(HttpStatusCode.BadRequest);
        kode.Should().Be("VALIDATION_FAILED");
        pesan.Should().Contain("belum terdaftar").And.Contain("grm");
    }

    [Fact]
    public async Task SatuanTambahan_TidakSah_Ditolak400()
    {
        using var owner = await OwnerAsync();

        async Task TolakAsync(object[] units, string kata)
        {
            using var respons = await owner.PostAsJsonAsync("/api/ingredients", new
            {
                name = NamaBahan(),
                unit = "kg",
                pricePerUnit = 1_200_000,
                units,
            });
            var (status, kode, pesan) = await JawabanAsync(respons);
            status.Should().Be(HttpStatusCode.BadRequest, pesan);
            kode.Should().Be("VALIDATION_FAILED");
            pesan.Should().Contain(kata);
        }

        await TolakAsync(new[] { Satuan("g", 0m) }, "Faktor");
        await TolakAsync(new[] { Satuan("g", 0.001m), Satuan("G", 0.001m) }, "lebih dari sekali");
        await TolakAsync(new[] { Satuan("KG", 1000m) }, "sama dengan satuan stok");
    }

    [Fact]
    public async Task SatuanYangMasihDipakaiResep_TidakBisaDihapus_AtauSatuanStokDiganti()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBahan();
        var bahan = await BahanAsync(owner, nama, "kg", 1_200_000, new[] { Satuan("g", 0.001m) });
        var namaProduk = NamaProduk();
        var produk = await ProdukAsync(owner, namaProduk, 500_000,
            new { ingredientId = bahan, quantity = 250m, unit = "g" });

        // Modal bahan mengirim daftar kosong berarti "hapus semua satuan tambahan". Satuan yang
        // masih disebut resep harus menolak, bukan membuat resep itu tidak bisa dihitung.
        using (var hapus = await owner.PutAsJsonAsync($"/api/ingredients/{bahan}", new
               {
                   name = nama,
                   unit = "kg",
                   pricePerUnit = 1_200_000,
                   units = Array.Empty<object>(),
               }))
        {
            var (status, kode, pesan) = await JawabanAsync(hapus);
            status.Should().Be(HttpStatusCode.BadRequest);
            kode.Should().Be("VALIDATION_FAILED");
            pesan.Should().Contain("masih dipakai");
        }
        (await CariBahanAsync(owner, bahan)).GetProperty("additionalUnits").GetArrayLength()
            .Should().Be(1, "penolakan terjadi sebelum baris satuan dihapus");

        // Pindahkan resep ke satuan stok lebih dulu, lalu coba ganti nama satuan stoknya.
        using (var ubahResep = await owner.PutAsJsonAsync($"/api/products/{produk}", new
               {
                   name = namaProduk,
                   sellingPriceMinorUnits = 500_000,
                   lines = new[] { new { ingredientId = bahan, quantity = 0.25m, unit = "kg" } },
               }))
        {
            var (status, kode, pesan) = await JawabanAsync(ubahResep);
            status.Should().Be(HttpStatusCode.OK, pesan);
            kode.Should().BeEmpty();
        }

        using (var gantiSatuan = await owner.PutAsJsonAsync($"/api/ingredients/{bahan}", new
               {
                   name = nama,
                   unit = "gram",
                   pricePerUnit = 1_200_000,
                   units = new[] { Satuan("g", 0.001m) },
               }))
        {
            var (status, kode, pesan) = await JawabanAsync(gantiSatuan);
            status.Should().Be(HttpStatusCode.BadRequest);
            kode.Should().Be("VALIDATION_FAILED");
            pesan.Should().Contain("masih disebut baris resep");
        }

        // Setelah resep tidak lagi menyebut satuan lama, satuan turunan boleh dihapus.
        using (var hapusBersih = await owner.PutAsJsonAsync($"/api/ingredients/{bahan}", new
               {
                   name = nama,
                   unit = "kg",
                   pricePerUnit = 1_200_000,
                   units = Array.Empty<object>(),
               }))
        {
            var (status, kode, pesan) = await JawabanAsync(hapusBersih);
            status.Should().Be(HttpStatusCode.OK, pesan);
            kode.Should().BeEmpty();
        }
        (await CariBahanAsync(owner, bahan)).GetProperty("additionalUnits").GetArrayLength().Should().Be(0);

        using var detail = JsonDocument.Parse(await owner.GetStringAsync($"/api/products/{produk}"));
        detail.RootElement.GetProperty("hpp").GetInt64().Should().Be(300_000,
            "0,25 kg pada Rp 12.000 tetap Rp 3.000 setelah satuan turunan dihapus");
    }

    [Fact]
    public async Task Spk_Target10_StandarQty2Komma5Kilogram()
    {
        using var owner = await OwnerAsync();
        var bahan = await BahanAsync(owner, NamaBahan(), "kg", 1_200_000, new[] { Satuan("g", 0.001m) });
        var produk = await ProdukAsync(owner, NamaProduk(), 500_000,
            new { ingredientId = bahan, quantity = 250m, unit = "g" });

        using var buat = await owner.PostAsJsonAsync("/api/production/orders", new
        {
            productTemplateId = produk,
            targetQuantity = 10,
            laborCost = 0L,
            overheadCost = 0L,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        var id = dok.RootElement.GetProperty("id").GetInt32();

        using var detail = JsonDocument.Parse(await owner.GetStringAsync($"/api/production/orders/{id}"));
        var akar = detail.RootElement;
        var material = akar.GetProperty("materials")[0];
        material.GetProperty("standardQty").GetDecimal().Should().Be(2.5m,
            "10 unit × 0,25 kg per unit");
        material.GetProperty("unitCost").GetInt64().Should().Be(1_200_000);
        akar.GetProperty("totalMaterialCost").GetInt64().Should().Be(3_000_000,
            "2,5 kg × Rp 12.000 = Rp 30.000");
    }
}
