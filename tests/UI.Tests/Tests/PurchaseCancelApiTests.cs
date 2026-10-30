using System.Net;
using System.Text;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Pembatalan PO diuji lewat HTTP sungguhan karena aturannya ada di ujung sambungan antara
/// penerimaan barang dan pembukuan: PO yang sebagian sudah diterima tidak boleh dibatalkan,
/// kalau boleh maka stok dan jurnal yang sudah terbit kehilangan dokumen induknya.
/// </summary>
[Collection("UI Tests")]
public sealed class PurchaseCancelApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public PurchaseCancelApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage respons)
    {
        var teks = await respons.Content.ReadAsStringAsync();
        return JsonDocument.Parse(teks == "" ? "null" : teks);
    }

    /// <summary>Buat pemasok + bahan + PO satu baris, kembalikan (id PO, id baris PO, id bahan).</summary>
    private static async Task<(int PoId, int LineId, int IngredientId, int LocationId)> PoBaruAsync(
        HttpClient http, decimal orderedQty)
    {
        var penanda = Guid.NewGuid().ToString("N")[..8];

        var pemasok = await http.PostAsync("/api/suppliers", Json(new
        {
            name = $"Pemasok Uji {penanda}",
            contactName = "Uji",
            phone = "080000000000",
        }));
        pemasok.StatusCode.Should().Be(HttpStatusCode.OK, "pemasok uji harus terbentuk lewat API asli");
        var pemasokId = (await JsonAsync(pemasok)).RootElement.GetProperty("id").GetInt32();

        var bahan = await http.PostAsync("/api/ingredients", Json(new
        {
            name = $"Bahan Uji {penanda}",
            unit = "kg",
            pricePerUnit = 1_200_000,
            currentStock = 0,
            minStockAlert = 0,
            isActive = true,
        }));
        bahan.StatusCode.Should().Be(HttpStatusCode.Created);
        var bahanId = (await JsonAsync(bahan)).RootElement.GetProperty("id").GetInt32();

        var lokasi = await JsonAsync(await http.GetAsync("/api/inventory/locations"));
        var gudang = lokasi.RootElement.EnumerateArray()
            .First(l => l.GetProperty("type").GetString() == "Warehouse")
            .GetProperty("id").GetInt32();

        var po = await http.PostAsync("/api/purchases", Json(new
        {
            supplierId = pemasokId,
            expectedDate = (string?)null,
            notes = "Uji pagar pembatalan PO",
            lines = new[] { new { ingredientId = bahanId, orderedQty, unitCostMinorUnits = 1_200_000, unit = "kg" } },
        }));
        po.StatusCode.Should().Be(HttpStatusCode.OK,
            $"PO uji harus terbentuk: {await po.Content.ReadAsStringAsync()}");
        var poId = (await JsonAsync(po)).RootElement.GetProperty("id").GetInt32();

        var detail = await JsonAsync(await http.GetAsync($"/api/purchases/{poId}"));
        var baris = detail.RootElement.GetProperty("lines").EnumerateArray().Single();
        baris.GetProperty("receivedQty").GetDecimal().Should().Be(0);

        return (poId, baris.GetProperty("id").GetInt32(), bahanId, gudang);
    }

    private static StringContent Json(object isi) =>
        new(JsonSerializer.Serialize(isi), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Pembatalan_DitolakSaatSebagianBarangSudahDiterima()
    {
        var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var (poId, lineId, bahanId, lokasiId) = await PoBaruAsync(http, 10m);

        var terima = await http.PostAsync($"/api/purchases/{poId}/receive", Json(new
        {
            notes = "Terima sebagian uji",
            lines = new[] { new { purchaseOrderLineId = lineId, ingredientId = bahanId, receivedQty = 4m, unitCostMinorUnits = 1_200_000, locationId = lokasiId } },
        }));
        terima.StatusCode.Should().Be(HttpStatusCode.OK,
            $"penerimaan sebagian harus tersimpan: {await terima.Content.ReadAsStringAsync()}");

        var detail = await JsonAsync(await http.GetAsync($"/api/purchases/{poId}"));
        detail.RootElement.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("receivedQty").GetDecimal().Should().Be(4m);

        var batal = await http.PutAsync($"/api/purchases/{poId}/cancel", null);
        batal.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "pembatalan akan meninggalkan stok dan jurnal tanpa dokumen induk");
        var pesan = (await JsonAsync(batal)).RootElement.GetProperty("error").GetString();
        pesan.Should().Contain("retur", "pengguna diarahkan ke jalur yang benar, bukan sekadar ditolak");

        detail = await JsonAsync(await http.GetAsync($"/api/purchases/{poId}"));
        // Status dikirim sebagai angka enum; PartialReceived = 2, Cancelled = 4.
        detail.RootElement.GetProperty("status").GetInt32().Should().Be(2,
            "PO tetap berstatus sebagian diterima setelah pembatalan ditolak");
    }

    [Fact]
    public async Task Pembatalan_DitolakSetelahDiterimaPenuh()
    {
        var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var (poId, lineId, bahanId, lokasiId) = await PoBaruAsync(http, 5m);

        var terima = await http.PostAsync($"/api/purchases/{poId}/receive", Json(new
        {
            notes = "Terima penuh uji",
            lines = new[] { new { purchaseOrderLineId = lineId, ingredientId = bahanId, receivedQty = 5m, unitCostMinorUnits = 1_200_000, locationId = lokasiId } },
        }));
        terima.StatusCode.Should().Be(HttpStatusCode.OK);

        var batal = await http.PutAsync($"/api/purchases/{poId}/cancel", null);
        batal.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(batal)).RootElement.GetProperty("error").GetString()
            .Should().Contain("diterima penuh");
    }

    [Fact]
    public async Task Pembatalan_JalanUntukPoYangBelumAdaBarangMasuk()
    {
        var http = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        var (poId, _, _, _) = await PoBaruAsync(http, 8m);

        var batal = await http.PutAsync($"/api/purchases/{poId}/cancel", null);
        batal.StatusCode.Should().Be(HttpStatusCode.OK,
            "PO tanpa penerimaan memang berhak dibatalkan; pagarnya hanya untuk yang sudah berjalan");

        var detail = await JsonAsync(await http.GetAsync($"/api/purchases/{poId}"));
        // Status dikirim sebagai angka enum; Cancelled = 4 pada PurchaseOrderStatus.
        detail.RootElement.GetProperty("status").GetInt32().Should().Be(4);

        var batalLagi = await http.PutAsync($"/api/purchases/{poId}/cancel", null);
        batalLagi.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "membatalkan dua kali bukan operasi yang diam-diam berhasil");
    }
}
