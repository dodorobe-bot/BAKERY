using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Kasus uji angka baris 45: "retur sebagian, lalu bayar hutang" harus berakhir dengan saldo
/// hutang yang turun tepat. Diuji lewat HTTP sungguhan karena yang diuji adalah janji ke layar
/// Utang Pemasok: angka yang tampil di daftar adalah angka yang boleh dibayar dan diretur,
/// dan keduanya harus mengikat struk penerimaan yang sama.
/// </summary>
[Collection("UI Tests")]
public sealed class SupplierPayableApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public SupplierPayableApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private const long HargaKg = 2_500_000;   // Rp 25.000/kg dalam sen
    private const string SandiAwal = "SandiSementara1";
    private const string SandiBaru = "SandiHarianPemasok1";

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static HttpClient Klien(HttpClient induk, string token)
    {
        var http = new HttpClient { BaseAddress = induk.BaseAddress!, Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>Akun peran lain lewat API produksi, termasuk gerbang ganti sandi bawaan.</summary>
    private static async Task<(int Id, string Token)> BuatPenggunaAsync(HttpClient owner, string role)
    {
        var username = $"ujipms{Guid.NewGuid():N}"[..24];
        using var buat = await owner.PostAsJsonAsync("/api/users", new { username, password = SandiAwal, role });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        var id = JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();

        var tokenBawaan = await LoginAsync(owner, username, SandiAwal);
        using (var ganti = Klien(owner, tokenBawaan))
        {
            var resp = await ganti.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = SandiAwal, newPassword = SandiBaru });
            resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        }
        return (id, await LoginAsync(owner, username, SandiBaru));
    }

    private static async Task<string> LoginAsync(HttpClient induk, string username, string password)
    {
        using var http = new HttpClient { BaseAddress = induk.BaseAddress! };
        var resp = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
        var teks = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        return JsonDocument.Parse(teks).RootElement.GetProperty("token").GetString()!;
    }

    private static StringContent Json(object isi) =>
        new(JsonSerializer.Serialize(isi), Encoding.UTF8, "application/json");

    /// <summary>Satu kartu hutang seperti yang dikirim API — cukup untuk memeriksa angkanya.</summary>
    private record KartuUtang(int GoodsReceiptId, long Terima, long Retur, long Bayar, long Sisa,
        IReadOnlyList<(int BarisId, int BahanId, decimal Diterima, decimal Diretur)> Lines)
    {
        public int BarisPertama => Lines[0].BarisId;
        public int BahanPertama => Lines[0].BahanId;
    }

    private static KartuUtang BacaKartu(JsonElement el) =>
        new(el.GetProperty("goodsReceiptId").GetInt32(),
            el.GetProperty("receivedValueMinorUnits").GetInt64(),
            el.GetProperty("returnedValueMinorUnits").GetInt64(),
            el.GetProperty("paidValueMinorUnits").GetInt64(),
            el.GetProperty("balanceMinorUnits").GetInt64(),
            el.GetProperty("lines").EnumerateArray().Select(l => (
                l.GetProperty("goodsReceiptLineId").GetInt32(),
                l.GetProperty("ingredientId").GetInt32(),
                l.GetProperty("receivedQuantity").GetDecimal(),
                l.GetProperty("returnedQuantity").GetDecimal())).ToList());

    /// <summary>struk penerimaan tidak punya endpoint sendiri; /api/payables/{id} jalur resminya.</summary>
    private static async Task<KartuUtang?> AmbilKartuAsync(HttpClient http, int strukId)
    {
        using var resp = await http.GetAsync($"/api/payables/{strukId}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return BacaKartu(JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement);
    }

    /// <summary>
    /// Pemasok + bahan + PO + penerimaan sungguhan, lalu struknya dicari lewat daftar utang.
    /// Tidak ada sisipan basis data: kalau jalur API tidak melahirkan kartu hutang, tes gagal.
    /// </summary>
    private async Task<KartuUtang> StrukBaruAsync(HttpClient http, decimal jumlahDiterima)
    {
        var penanda = Guid.NewGuid().ToString("N")[..8];

        var pemasok = JsonDocument.Parse(await (await http.PostAsync("/api/suppliers",
            Json(new { name = $"Pemasok Hutang {penanda}", contactName = "Uji", phone = "080000000000" })))
            .Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        var bahan = JsonDocument.Parse(await (await http.PostAsync("/api/ingredients",
            Json(new
            {
                name = $"Bahan Hutang {penanda}", unit = "kg", pricePerUnit = HargaKg,
                currentStock = 0, minStockAlert = 0, isActive = true,
            }))).Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        var gudang = JsonDocument.Parse(await http.GetStringAsync("/api/inventory/locations"))
            .RootElement.EnumerateArray()
            .First(l => l.GetProperty("type").GetString() == "Warehouse")
            .GetProperty("id").GetInt32();

        using var buatPo = await http.PostAsync("/api/purchases", Json(new
        {
            supplierId = pemasok,
            expectedDate = (string?)null,
            notes = "Uji retur pemasok dan pembayaran hutang",
            lines = new[] { new { ingredientId = bahan, orderedQty = jumlahDiterima, unitCostMinorUnits = HargaKg, unit = "kg" } },
        }));
        var teksPo = await buatPo.Content.ReadAsStringAsync();
        buatPo.StatusCode.Should().Be(HttpStatusCode.OK, teksPo);
        var poId = JsonDocument.Parse(teksPo).RootElement.GetProperty("id").GetInt32();

        using var detail = JsonDocument.Parse(await http.GetStringAsync($"/api/purchases/{poId}"));
        var barisPo = detail.RootElement.GetProperty("lines").EnumerateArray().Single();

        using var terima = await http.PostAsync($"/api/purchases/{poId}/receive", Json(new
        {
            notes = "Terima penuh uji hutang",
            lines = new[]
            {
                new
                {
                    purchaseOrderLineId = barisPo.GetProperty("id").GetInt32(),
                    ingredientId = bahan,
                    receivedQty = jumlahDiterima,
                    unitCostMinorUnits = HargaKg,
                    locationId = gudang,
                }
            },
        }));
        terima.StatusCode.Should().Be(HttpStatusCode.OK, await terima.Content.ReadAsStringAsync());

        // Struk ditemukan lewat daftar utang, jadi identitas struk ikut diuji.
        using var daftar = JsonDocument.Parse(await http.GetStringAsync("/api/payables?limit=500"));
        var kartu = daftar.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("purchaseOrderId").GetInt32() == poId)
            .Select(BacaKartu).SingleOrDefault();

        kartu.Should().NotBeNull($"penerimaan PO {poId} wajib muncul di /api/payables");
        kartu!.Terima.Should().Be((long)Math.Round(jumlahDiterima * HargaKg, MidpointRounding.AwayFromZero),
            "hutang awal struk sebesar barang yang benar-benar diterima");
        kartu.Sisa.Should().Be(kartu.Terima);
        kartu.Lines.Should().HaveCount(1);
        kartu.Lines[0].Diterima.Should().Be(jumlahDiterima);
        kartu.Lines[0].Diretur.Should().Be(0);
        return kartu;
    }

    private static async Task<(HttpStatusCode Status, int Id, string Isi)> ReturAsync(
        HttpClient http, int strukId, int barisId, decimal jumlah, string alasan)
    {
        using var resp = await http.PostAsync("/api/supplier-returns", Json(new
        {
            goodsReceiptId = strukId,
            reason = alasan,
            notes = "Uji angka baris 45",
            lines = new[] { new { goodsReceiptLineId = barisId, quantity = jumlah, locationId = (int?)null } },
        }));
        var isi = await resp.Content.ReadAsStringAsync();
        var id = resp.StatusCode == HttpStatusCode.Created
            ? JsonDocument.Parse(isi).RootElement.GetProperty("id").GetInt32()
            : 0;
        return (resp.StatusCode, id, isi);
    }

    private static async Task<HttpResponseMessage> BayarAsync(
        HttpClient http, int strukId, long jumlah, string metode = "Tunai") =>
        await http.PostAsync("/api/supplier-payments", Json(new
        {
            goodsReceiptId = strukId,
            amount = jumlah,
            method = metode,
            notes = "Uang muka pelunasan struk",
        }));

    [Fact]
    public async Task ReturSebagianLaluBayarHutang_SaldoHutangTurunTepat()
    {
        var http = await OwnerAsync();
        var struk = await StrukBaruAsync(http, 10m);

        var (status, idRetur, isiRetur) = await ReturAsync(http, struk.GoodsReceiptId, struk.BarisPertama, 3m, "Kemasan sobek");
        status.Should().Be(HttpStatusCode.Created, $"retur 3 kg dari 10 kg harus tersimpan: {isiRetur}");
        idRetur.Should().NotBe(0, "id dokumen dikirim kembali lewat header Location");

        var sesudahRetur = (await AmbilKartuAsync(http, struk.GoodsReceiptId))!;
        sesudahRetur.Terima.Should().Be(25_000_000);
        sesudahRetur.Retur.Should().Be(7_500_000, "tiga kilogram Rp 25.000");
        sesudahRetur.Bayar.Should().Be(0);
        sesudahRetur.Sisa.Should().Be(17_500_000, "retur menurunkan hutang sebelum uang keluar");
        sesudahRetur.Lines[0].Diretur.Should().Be(3m);

        // Bayar Rp 100.000 dari sisa Rp 175.000.
        using (var bayar = await BayarAsync(http, struk.GoodsReceiptId, 10_000_000))
            bayar.StatusCode.Should().Be(HttpStatusCode.Created, await bayar.Content.ReadAsStringAsync());

        var akhir = (await AmbilKartuAsync(http, struk.GoodsReceiptId))!;
        akhir.Retur.Should().Be(7_500_000, "pembayaran tidak mengubah nilai retur");
        akhir.Bayar.Should().Be(10_000_000);
        akhir.Sisa.Should().Be(7_500_000, "sisa = terima - retur - bayar");

        // Dokumen harus bisa dibaca kembali per struk.
        using (var daftarRetur = JsonDocument.Parse(
                   await http.GetStringAsync($"/api/supplier-returns?goodsReceiptId={struk.GoodsReceiptId}")))
        {
            var r = daftarRetur.RootElement.EnumerateArray().Single();
            r.GetProperty("id").GetInt32().Should().Be(idRetur);
            r.GetProperty("returnNumber").GetString().Should().StartWith("RTB-");
            r.GetProperty("valueMinorUnits").GetInt64().Should().Be(7_500_000);
            r.GetProperty("reason").GetString().Should().Be("Kemasan sobek");
        }
        using (var daftarBayar = JsonDocument.Parse(
                   await http.GetStringAsync($"/api/supplier-payments?goodsReceiptId={struk.GoodsReceiptId}")))
        {
            var p = daftarBayar.RootElement.EnumerateArray().Single();
            p.GetProperty("paymentNumber").GetString().Should().StartWith("PPB-");
            p.GetProperty("amountMinorUnits").GetInt64().Should().Be(10_000_000);
            p.GetProperty("method").GetString().Should().Be("Tunai");
        }

        // Barangnya benar-benar keluar: 10 kg diterima, 3 kg kembali ke pemasok.
        var gudang = JsonDocument.Parse(await http.GetStringAsync("/api/inventory/locations"))
            .RootElement.EnumerateArray()
            .First(l => l.GetProperty("type").GetString() == "Warehouse")
            .GetProperty("id").GetInt32();
        using (var stok = JsonDocument.Parse(
                   await http.GetStringAsync($"/api/inventory/locations/{gudang}/stock")))
        {
            stok.RootElement.EnumerateArray()
                .Where(i => i.GetProperty("ingredientId").GetInt32() == akhir.BahanPertama)
                .Sum(i => i.GetProperty("quantity").GetDecimal())
                .Should().Be(7m, "retur mengurangi stok, bukan hanya angka hutang");
        }

        using (var mutasi = JsonDocument.Parse(
                   await http.GetStringAsync($"/api/inventory/movements?locationId={gudang}&limit=500")))
        {
            // Disempitkan ke bahan uji: basis data tes dipakai bersama, jadi mutasi dari kasus
            // lain tidak boleh dihitung sebagai milik struk ini.
            var m = mutasi.RootElement.EnumerateArray()
                .Single(x => x.GetProperty("sourceType").GetString() == "SupplierReturn" &&
                             x.GetProperty("ingredientId").GetInt32() == akhir.BahanPertama);
            m.GetProperty("movementType").GetString().Should().Be("Retur Pemasok",
                "buku mutasi tidak menyebut retur pemasok sebagai koreksi manual");
            Math.Abs(m.GetProperty("quantity").GetDecimal()).Should().Be(3m);
        }

        using var cek = JsonDocument.Parse(await http.GetStringAsync("/api/system/self-check"));
        var hasil = cek.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("passed").GetBoolean());
        hasil.Should().ContainKey("hutang pemasok per struk tidak pernah negatif");
        hasil["hutang pemasok per struk tidak pernah negatif"].Should().BeTrue();
        hasil.Should().ContainKey("retur dan pembayaran pemasok punya jurnal");
        hasil["retur dan pembayaran pemasok punya jurnal"].Should().BeTrue();
    }

    [Fact]
    public async Task ReturMelebihiBarangDiterima_Jawab400DanHutangTidakBergerak()
    {
        var http = await OwnerAsync();
        var struk = await StrukBaruAsync(http, 4m);

        var (status, id, isi) = await ReturAsync(http, struk.GoodsReceiptId, struk.BarisPertama, 5m, "Melebihi yang diterima");

        status.Should().Be(HttpStatusCode.BadRequest,
            "permintaan yang salah harus 400, bukan 500 atau setengah tersimpan");
        id.Should().Be(0, "permintaan yang salah tidak boleh menyimpan dokumen");
        isi.Should().Contain("melebihi");

        var kartu = (await AmbilKartuAsync(http, struk.GoodsReceiptId))!;
        kartu.Retur.Should().Be(0, "permintaan yang ditolak tidak boleh meninggalkan nilai retur");
        kartu.Sisa.Should().Be(10_000_000);
    }

    [Fact]
    public async Task PembayaranMelebihiSisa_HanyaSisaYangDiterimaDanStrukLunasKeluarDariDaftar()
    {
        var http = await OwnerAsync();
        var struk = await StrukBaruAsync(http, 2m);

        using (var lebih = await BayarAsync(http, struk.GoodsReceiptId, 6_000_000))
        {
            var isi = await lebih.Content.ReadAsStringAsync();
            lebih.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "membayar di atas hutang bukan pelunasan");
            isi.Should().Contain("melebihi sisa hutang struk ini").And.Contain("Rp 50.000");
        }

        using (var lunas = await BayarAsync(http, struk.GoodsReceiptId, 5_000_000, "Transfer"))
            lunas.StatusCode.Should().Be(HttpStatusCode.Created, await lunas.Content.ReadAsStringAsync());

        (await AmbilKartuAsync(http, struk.GoodsReceiptId))!.Sisa.Should().Be(0);
        using var daftar = JsonDocument.Parse(await http.GetStringAsync("/api/payables?limit=500"));
        daftar.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("goodsReceiptId").GetInt32() == struk.GoodsReceiptId).Should().BeFalse(
                "daftar utang hanya menawarkan struk yang masih punya sisa");

        using var denganLunas = JsonDocument.Parse(
            await http.GetStringAsync("/api/payables?includeLunas=true&limit=500"));
        denganLunas.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("goodsReceiptId").GetInt32() == struk.GoodsReceiptId).Should().BeTrue(
                "struk lunas tetap bisa dilihat saat pengguna minta riwayat");

        // Retur setelah lunas ditolak dengan pesan yang mengarahkan, bukan sekadar gagal.
        var (statusRetur, idRetur, isiRetur) = await ReturAsync(http, struk.GoodsReceiptId, struk.BarisPertama, 1m, "Salah kirim");
        statusRetur.Should().Be(HttpStatusCode.BadRequest);
        idRetur.Should().Be(0, "hutang nol tidak boleh dikecilkan lagi");
        isiRetur.Should().Contain("sudah lunas");
    }

    [Fact]
    public async Task StrukAtauBarisAsing_Jawab404Atau400Bukan500()
    {
        var http = await OwnerAsync();
        var struk = await StrukBaruAsync(http, 1m);

        using (var asing = await http.PostAsync("/api/supplier-returns", Json(new
        {
            goodsReceiptId = 9_999_999,
            reason = "Struk tak dikenal",
            lines = new[] { new { goodsReceiptLineId = 1, quantity = 1m, locationId = (int?)null } },
        })))
            asing.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "struk yang tidak ada bukan kesalahan isian pengguna biasa");

        using (var barisAsing = await http.PostAsync("/api/supplier-returns", Json(new
        {
            goodsReceiptId = struk.GoodsReceiptId,
            reason = "Baris dari struk lain",
            lines = new[] { new { goodsReceiptLineId = 9_999_999, quantity = 1m, locationId = (int?)null } },
        })))
        {
            barisAsing.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "baris yang bukan milik struk ini ditolak, bukan dituliskan");
            barisAsing.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        }

        (await http.GetAsync("/api/payables/9999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var tanpaBaris = await http.PostAsync("/api/supplier-returns", Json(new
        {
            goodsReceiptId = struk.GoodsReceiptId,
            reason = "Tidak ada barang",
            lines = (int[])Array.Empty<int>(),
        })))
            tanpaBaris.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "retur kosong bukan dokumen yang bisa dibukukan");
    }

    [Fact]
    public async Task Kasir_TidakBolehMelihatUtangAtaupunMembuatDokumenPemasok()
    {
        var owner = await OwnerAsync();
        var struk = await StrukBaruAsync(owner, 3m);
        var (idKasir, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        (await kasir.GetAsync("/api/payables")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "daftar hutang pemasok berisi harga beli, bukan konsumsi kasir");
        (await kasir.GetAsync("/api/supplier-returns")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var retur = await kasir.PostAsync("/api/supplier-returns", Json(new
        {
            goodsReceiptId = struk.GoodsReceiptId,
            reason = "Kasir mencoba retur",
            lines = new[] { new { goodsReceiptLineId = struk.BarisPertama, quantity = 1m, locationId = (int?)null } },
        }));
        retur.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "menu yang tidak terlihat tidak boleh bisa ditembak langsung");

        var bayar = await kasir.PostAsync("/api/supplier-payments", Json(new
        {
            goodsReceiptId = struk.GoodsReceiptId,
            amount = 100_000,
            method = "Tunai",
        }));
        bayar.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var akhir = (await AmbilKartuAsync(owner, struk.GoodsReceiptId))!;
        akhir.Retur.Should().Be(0, "percobaan tanpa hak tidak mengubah apa pun");
        akhir.Bayar.Should().Be(0);
        akhir.Sisa.Should().Be(7_500_000);

        using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/audit?userId={idKasir}&limit=50"));
        var catatan = dok.RootElement.EnumerateArray()
            .Where(a => a.GetProperty("entityType").GetString() == "Akses")
            .Select(a => a.GetProperty("action").GetString() ?? "")
            .ToList();
        catatan.Should().Contain(a => a.Contains("DITOLAK") && a.Contains("/api/supplier-returns"),
            "percobaan retur pemasok tanpa hak harus meninggalkan jejak");
    }

    [Fact]
    public async Task DokumenPemasok_TercatatDiJejakAudit()
    {
        var http = await OwnerAsync();
        var struk = await StrukBaruAsync(http, 5m);

        var (statusRetur, idRetur, _) = await ReturAsync(http, struk.GoodsReceiptId, struk.BarisPertama, 1m, "Kedaluwarsa");
        statusRetur.Should().Be(HttpStatusCode.Created);
        idRetur.Should().NotBe(0);
        using (var bayar = await BayarAsync(http, struk.GoodsReceiptId, 5_000_000))
            bayar.StatusCode.Should().Be(HttpStatusCode.Created, await bayar.Content.ReadAsStringAsync());

        using var dok = JsonDocument.Parse(await http.GetStringAsync("/api/audit?entity=SupplierReturn&limit=100"));
        var entri = dok.RootElement.EnumerateArray()
            .Where(a => a.GetProperty("entityType").GetString() == "SupplierReturn")
            .ToList();
        entri.Should().NotBeEmpty("dokumen uang tanpa jejak audit tidak bisa ditelusuri");
        entri.Any(a => a.GetProperty("entityId").GetInt32() == idRetur).Should().BeTrue(
            "jejak menunjuk id dokumen yang dikembalikan lewat header Location");
    }
}
