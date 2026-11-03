using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Batch-7 diuji lewat HTTP sungguhan pada basis data sementara: logout mencabut token di semua
/// salinan, hasil QC benar-benar tersimpan per SPK, HPP memakai angka yang sudah tercatat, dan
/// retur mengembalikan uang serta stok dalam satu dokumen. Dua penulisan yang datang serentak
/// juga diuji di sini, karena idempotensi hanya menutup klik ganda, bukan balapan sungguhan.
/// </summary>
[Collection("UI Tests")]
public sealed class Batch7ApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public Batch7ApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private const string SandiAwal = "SandiSementara1";
    private const string SandiBaru = "SandiHarianKasir1";

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static HttpClient Klien(HttpClient induk, string token)
    {
        var http = new HttpClient { BaseAddress = induk.BaseAddress!, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<(int Id, string Username, string Token)> BuatPenggunaAsync(
        HttpClient owner, string role)
    {
        var username = $"uji{role.ToLowerInvariant()}{Guid.NewGuid():N}"[..24];
        using var buat = await owner.PostAsJsonAsync("/api/users", new { username, password = SandiAwal, role });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        var id = dok.RootElement.GetProperty("id").GetInt32();

        var (tokenBawaan, wajibGanti) = await LoginAsync(owner, username, SandiAwal);
        wajibGanti.Should().BeTrue("akun baru selalu wajib mengganti sandi lebih dulu");

        using (var klien = Klien(owner, tokenBawaan))
        {
            var ganti = await klien.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = SandiAwal, newPassword = SandiBaru });
            var isi = await ganti.Content.ReadAsStringAsync();
            ganti.StatusCode.Should().Be(HttpStatusCode.OK, isi);
        }

        var (token, wajibGantiLagi) = await LoginAsync(owner, username, SandiBaru);
        wajibGantiLagi.Should().BeFalse();
        return (id, username, token);
    }

    private static async Task<(string Token, bool WajibGanti)> LoginAsync(
        HttpClient induk, string username, string password)
    {
        using var http = new HttpClient { BaseAddress = induk.BaseAddress! };
        var resp = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
        var teks = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        using var dok = JsonDocument.Parse(teks);
        return (dok.RootElement.GetProperty("token").GetString()!,
            dok.RootElement.GetProperty("mustChangePassword").GetBoolean());
    }

    private static async Task<int> BuatProdukAsync(HttpClient owner, long hargaMinor)
    {
        var nama = $"Produk Uji {Guid.NewGuid():N}"[..26];
        using var buat = await owner.PostAsJsonAsync("/api/products", new
        {
            name = nama,
            category = "Uji Batch-7",
            sellingPriceMinorUnits = hargaMinor,
            lines = Array.Empty<object>(),
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.IsSuccessStatusCode.Should().BeTrue(teks);

        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/products"));
        return dok.RootElement.EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == nama)
            .GetProperty("id").GetInt32();
    }

    private static async Task<int> BuatSpkAsync(
        HttpClient owner, int produkId, int target, long laborMinor = 0, long overheadMinor = 0)
    {
        using var buat = await owner.PostAsJsonAsync("/api/production/orders", new
        {
            productTemplateId = produkId,
            targetQuantity = target,
            laborCost = laborMinor,
            overheadCost = overheadMinor,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    private static JsonContent SelesaiSpk(int good) => JsonContent.Create(new
    {
        actualGoodQty = good,
        actualFailedQty = 0,
        materials = Array.Empty<object>(),
        scraps = Array.Empty<object>(),
    });

    private static async Task SelesaikanSpkAsync(HttpClient owner, int spkId, int good)
    {
        using var selesai = await owner.PostAsync($"/api/production/orders/{spkId}/complete", SelesaiSpk(good));
        var teks = await selesai.Content.ReadAsStringAsync();
        selesai.StatusCode.Should().Be(HttpStatusCode.OK, teks);
    }

    /// <summary>Pesanan lunas sejak dibuat: uang masuk penuh, stok etalase ikut berkurang.</summary>
    private static async Task<int> BuatPesananLunasAsync(
        HttpClient owner, int produkId, int qty, long totalMinor)
    {
        using var buat = await owner.PostAsJsonAsync("/api/orders", new
        {
            customerName = $"Pelanggan {Guid.NewGuid():N}"[..24],
            description = "Uji retur penjualan",
            pickupDate = "2030-06-06T10:00:00",
            totalAmount = totalMinor,
            downPayment = totalMinor,
            productTemplateId = produkId,
            quantity = qty,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<(HttpStatusCode Kode, string Teks)> KirimAsync(
        HttpClient http, string rute, object isi, string? kunci = null)
    {
        using var permintaan = new HttpRequestMessage(HttpMethod.Post, rute) { Content = JsonContent.Create(isi) };
        if (kunci is not null) permintaan.Headers.TryAddWithoutValidation("Idempotency-Key", kunci);
        using var respons = await http.SendAsync(permintaan);
        return (respons.StatusCode, await respons.Content.ReadAsStringAsync());
    }

    private static async Task<decimal> StokEtalaseAsync(HttpClient owner, int produkId)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/inventory/finished-goods"));
        var baris = dok.RootElement.EnumerateArray()
            .FirstOrDefault(g => g.GetProperty("productTemplateId").GetInt32() == produkId);
        return baris.ValueKind == JsonValueKind.Undefined ? 0m : baris.GetProperty("quantity").GetDecimal();
    }

    [Fact]
    public async Task Qc_TersimpanPerSpk_DanKeputusanDihitungServer()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 2_500_000);
        var spk = await BuatSpkAsync(owner, produk, 6);

        var belumSelesai = await KirimAsync(owner, "/api/production/quality-checks",
            new { workOrderId = spk, sampleQuantity = 2, passedQuantity = 2, failedQuantity = 0 });
        belumSelesai.Kode.Should().Be(HttpStatusCode.BadRequest,
            "SPK yang belum selesai belum punya hasil untuk diperiksa");

        await SelesaikanSpkAsync(owner, spk, 6);

        var lebihDariSampel = await KirimAsync(owner, "/api/production/quality-checks",
            new { workOrderId = spk, sampleQuantity = 3, passedQuantity = 3, failedQuantity = 1 });
        lebihDariSampel.Kode.Should().Be(HttpStatusCode.BadRequest, "lulus + gagal tidak boleh melebihi sampel");

        var gagal = await KirimAsync(owner, "/api/production/quality-checks", new
        {
            workOrderId = spk,
            sampleQuantity = 5,
            passedQuantity = 4,
            failedQuantity = 1,
            appearanceNotes = "Warna tidak rata",
        });
        gagal.Kode.Should().Be(HttpStatusCode.Created, gagal.Teks);
        using (var dok = JsonDocument.Parse(gagal.Teks))
            dok.RootElement.GetProperty("keputusan").GetString().Should().Be("Failed",
                "keputusan dihitung server dari angka gagal, bukan dikirim layar");

        var ulang = await KirimAsync(owner, "/api/production/quality-checks",
            new { workOrderId = spk, sampleQuantity = 5, passedQuantity = 5, failedQuantity = 0 });
        ulang.Kode.Should().Be(HttpStatusCode.BadRequest);
        ulang.Teks.Should().Contain("sudah diperiksa");

        using var daftar = JsonDocument.Parse(await owner.GetStringAsync("/api/production/quality-checks"));
        var baris = daftar.RootElement.EnumerateArray()
            .FirstOrDefault(q => q.GetProperty("workOrderId").GetInt32() == spk);
        baris.ValueKind.Should().NotBe(JsonValueKind.Undefined, "hasil pemeriksaan harus bisa dibaca lagi setelah layar ditutup");
        baris.GetProperty("sampleQuantity").GetInt32().Should().Be(5);
        baris.GetProperty("passedQuantity").GetInt32().Should().Be(4);
        baris.GetProperty("failedQuantity").GetInt32().Should().Be(1);
        baris.GetProperty("decision").GetString().Should().Be("Failed");
        baris.GetProperty("appearanceNotes").GetString().Should().Be("Warna tidak rata");
        baris.GetProperty("checkerUsername").GetString().Should().Be(AppLogin.Username);
        baris.GetProperty("orderNumber").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Qc_HanyaUntukPeranProduksi()
    {
        var owner = await OwnerAsync();
        var (_, _, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        (await kasir.GetAsync("/api/production/quality-checks")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await kasir.PostAsJsonAsync("/api/production/quality-checks",
            new { workOrderId = 1, sampleQuantity = 1, passedQuantity = 1, failedQuantity = 0 })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Hpp_MemakaiBiayaSpkSelesaiBukanAngkaPerkiraan()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 3_000_000);
        var spk = await BuatSpkAsync(owner, produk, 10, laborMinor: 100_000, overheadMinor: 20_000);
        await SelesaikanSpkAsync(owner, spk, 10);

        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/production/cost-summary"));
        var akar = dok.RootElement;
        var baris = akar.GetProperty("products").EnumerateArray()
            .First(p => p.GetProperty("productId").GetInt32() == produk);

        baris.GetProperty("recipeLineCount").GetInt32().Should().Be(0);
        baris.GetProperty("standardHppMinorUnits").GetInt64().Should().Be(0);
        baris.GetProperty("hasActual").GetBoolean().Should().BeTrue("SPK-nya sudah selesai");
        baris.GetProperty("completedWorkOrders").GetInt32().Should().Be(1);
        baris.GetProperty("goodUnits").GetInt32().Should().Be(10);
        baris.GetProperty("actualHppMinorUnits").GetInt64().Should().Be(12_000,
            "(Rp1.000 tenaga kerja + Rp200 overhead) / 10 unit baik");
        baris.GetProperty("varianceMinorUnits").GetInt64().Should().Be(12_000,
            "standar resep kosong, jadi seluruh biaya aktual adalah selisih");

        akar.GetProperty("completedWorkOrders").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        akar.GetProperty("laborCostMinorUnits").GetInt64().Should().BeGreaterThanOrEqualTo(100_000);
        akar.GetProperty("overheadCostMinorUnits").GetInt64().Should().BeGreaterThanOrEqualTo(20_000);
        akar.GetProperty("weightedActualHppMinorUnits").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Retur_MengembalikanUangStokDanJurnal()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 1_500_000);
        var spk = await BuatSpkAsync(owner, produk, 5);
        await SelesaikanSpkAsync(owner, spk, 5);

        var pesanan = await BuatPesananLunasAsync(owner, produk, qty: 2, totalMinor: 3_000_000);
        (await StokEtalaseAsync(owner, produk)).Should().Be(3, "dua unit terjual keluar dari etalase");

        var retur = await KirimAsync(owner, "/api/returns", new
        {
            salesOrderId = pesanan,
            quantity = 1,
            amount = 1_500_000,
            reason = "Produk tidak sesuai pesanan",
            refundMethod = "Tunai",
        });
        retur.Kode.Should().Be(HttpStatusCode.Created, retur.Teks);
        (await StokEtalaseAsync(owner, produk)).Should().Be(4, "barang retur kembali ke etalase");

        using var daftar = JsonDocument.Parse(await owner.GetStringAsync("/api/returns"));
        var dokumen = daftar.RootElement.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("salesOrderId").GetInt32() == pesanan);
        dokumen.ValueKind.Should().NotBe(JsonValueKind.Undefined, "retur yang baru dibuat harus muncul di riwayat");
        dokumen.GetProperty("quantity").GetDecimal().Should().Be(1);
        dokumen.GetProperty("amount").GetInt64().Should().Be(1_500_000);
        dokumen.GetProperty("refundMethod").GetString().Should().Be("Tunai");
        dokumen.GetProperty("reason").GetString().Should().Be("Produk tidak sesuai pesanan");
        dokumen.GetProperty("createdByUsername").GetString().Should().Be(AppLogin.Username);
        var nomorRetur = dokumen.GetProperty("returnNumber").GetString();
        nomorRetur.Should().StartWith("RTR");

        using var jurnal = JsonDocument.Parse(await owner.GetStringAsync("/api/journal?limit=200"));
        var entri = jurnal.RootElement.EnumerateArray()
            .FirstOrDefault(j => (j.GetProperty("description").GetString() ?? "").Contains(nomorRetur!));
        entri.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "uang yang keluar harus punya jurnal pembalik pendapatan");
        entri.GetProperty("sourceDocumentType").GetString().Should().Be("SalesReturn");

        // Sisa yang boleh diretur dihitung dari dokumen yang sudah ada, bukan dari isian layar.
        var melebihiSisa = await KirimAsync(owner, "/api/returns", new
        {
            salesOrderId = pesanan,
            quantity = 2,
            amount = 3_000_000,
            reason = "Retur kedua",
            refundMethod = "Tunai",
        });
        melebihiSisa.Kode.Should().Be(HttpStatusCode.BadRequest,
            "satu unit sudah diretur, jadi hanya satu unit lagi yang tersisa");

        using var cek = JsonDocument.Parse(await owner.GetStringAsync("/api/system/self-check"));
        cek.RootElement.GetProperty("status").GetString().Should().Be("ok",
            "jurnal retur harus seimbang seperti jurnal lain");
    }

    [Fact]
    public async Task Retur_NilaiUangMengikutiJumlahUnit()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 1_000_000);
        var spk = await BuatSpkAsync(owner, produk, 5);
        await SelesaikanSpkAsync(owner, spk, 5);

        // Skenario audit: pesanan 2 unit senilai Rp 304,00 — mengembalikan 2 unit tidak boleh
        // hanya mencatat setengah nilainya.
        var pesanan = await BuatPesananLunasAsync(owner, produk, qty: 2, totalMinor: 30_400);

        var separuh = await KirimAsync(owner, "/api/returns", new
        {
            salesOrderId = pesanan,
            quantity = 2,
            amount = 15_200,
            reason = "Nilai tidak sesuai jumlah",
            refundMethod = "Tunai",
        });
        separuh.Kode.Should().Be(HttpStatusCode.BadRequest, separuh.Teks);
        separuh.Teks.Should().Contain("mengikuti jumlah");

        // Retur sebagian yang nilainya sesuai proporsi tetap diterima.
        var sebagian = await KirimAsync(owner, "/api/returns", new
        {
            salesOrderId = pesanan,
            quantity = 1,
            amount = 15_200,
            reason = "Satu unit rusak",
            refundMethod = "Tunai",
        });
        sebagian.Kode.Should().Be(HttpStatusCode.Created, sebagian.Teks);

        // Retur terakhir menutup sisa nilai pesanan tepat.
        var sisa = await KirimAsync(owner, "/api/returns", new
        {
            salesOrderId = pesanan,
            quantity = 1,
            amount = 15_200,
            reason = "Sisa unit rusak",
            refundMethod = "Tunai",
        });
        sisa.Kode.Should().Be(HttpStatusCode.Created, sisa.Teks);

        using var daftar = JsonDocument.Parse(await owner.GetStringAsync("/api/returns"));
        var totalNilai = daftar.RootElement.EnumerateArray()
            .Where(r => r.GetProperty("salesOrderId").GetInt32() == pesanan)
            .Sum(r => r.GetProperty("amount").GetInt64());
        totalNilai.Should().Be(30_400, "total retur harus menutup nilai pesanan, tidak lebih dan tidak kurang");
    }

    [Fact]
    public async Task CapWaktu_MutasiStokDanJejakAudit_MemakaiUtcBertanda()
    {
        var owner = await OwnerAsync();
        var nama = $"Bahan Waktu {Guid.NewGuid():N}"[..24];
        using var bahan = await owner.PostAsJsonAsync("/api/ingredients", new
        {
            name = nama,
            unit = "kg",
            pricePerUnit = 10_000,
        });
        bahan.StatusCode.Should().Be(HttpStatusCode.Created, await bahan.Content.ReadAsStringAsync());
        var bahanId = JsonDocument.Parse(await bahan.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        var koreksi = await KirimAsync(owner, "/api/inventory/adjust", new
        {
            locationId = 1,
            ingredientId = bahanId,
            newQuantity = 4,
            reason = "Uji cap waktu",
        });
        koreksi.Kode.Should().Be(HttpStatusCode.OK, koreksi.Teks);

        using var gerak = JsonDocument.Parse(await owner.GetStringAsync("/api/inventory/movements?limit=100"));
        var waktuGerak = gerak.RootElement.EnumerateArray()
            .First(m => m.GetProperty("ingredientId").GetInt32() == bahanId)
            .GetProperty("createdAt").GetString()!;
        waktuGerak.Should().EndWith("Z", "cap waktu mutasi harus menyatakan UTC, bukan waktu lokal tanpa penanda");

        using var jejak = JsonDocument.Parse(await owner.GetStringAsync("/api/audit?limit=100"));
        var waktuJejak = jejak.RootElement.EnumerateArray()
            .First(a => a.GetProperty("entityType").GetString() == "LocationStock" &&
                        a.GetProperty("entityId").GetInt32() == bahanId)
            .GetProperty("occurredAt").GetString()!;
        waktuJejak.Should().EndWith("Z", "jejak audit harus memakai acuan waktu yang sama dengan mutasi stok");

        DateTimeOffset.Parse(waktuGerak).Should().BeCloseTo(
            DateTimeOffset.Parse(waktuJejak), TimeSpan.FromMinutes(2),
            "kejadian yang sama tidak boleh terbaca selisih jam di dua layar");
    }

    [Fact]
    public async Task Retur_HanyaPesananLunasDanHanyaOwnerAdmin()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 1_000_000);

        using var belumLunas = await owner.PostAsJsonAsync("/api/orders", new
        {
            customerName = $"Pelanggan {Guid.NewGuid():N}"[..24],
            description = "Uji retur pesanan berjalan",
            pickupDate = "2030-06-06T10:00:00",
            totalAmount = 1_000_000,
            downPayment = 0,
            productTemplateId = produk,
            quantity = 1,
        });
        var teks = await belumLunas.Content.ReadAsStringAsync();
        belumLunas.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        var pesanan = JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();

        var ditolak = await KirimAsync(owner, "/api/returns", new
        {
            salesOrderId = pesanan,
            quantity = 1,
            amount = 1_000_000,
            reason = "Belum lunas",
            refundMethod = "Tunai",
        });
        ditolak.Kode.Should().Be(HttpStatusCode.BadRequest);
        ditolak.Teks.Should().Contain("lunas");

        var (_, _, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);
        (await kasir.GetAsync("/api/returns")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await kasir.PostAsJsonAsync("/api/returns", new
        {
            salesOrderId = pesanan, quantity = 1, amount = 1_000_000, reason = "Coba", refundMethod = "Tunai",
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "retur mengeluarkan uang, jadi bukan wewenang kasir");
    }

    [Fact]
    public async Task Logout_MencabutTokenDiSemuaSalinan()
    {
        var owner = await OwnerAsync();
        var (_, username, tokenPertama) = await BuatPenggunaAsync(owner, "Pegawai");
        var (tokenKedua, _) = await LoginAsync(owner, username, SandiBaru);

        using var perangkatSatu = Klien(owner, tokenPertama);
        using var perangkatDua = Klien(owner, tokenKedua);
        (await perangkatSatu.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await perangkatDua.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.OK);

        var keluar = await perangkatSatu.PostAsync("/api/auth/logout", null);
        keluar.StatusCode.Should().Be(HttpStatusCode.OK, await keluar.Content.ReadAsStringAsync());

        (await perangkatSatu.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "token yang tercetak sebelum logout tidak boleh dipakai lagi");
        (await perangkatDua.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "logout mencabut sesi akun, bukan hanya perangkat yang menekan tombol");

        var (tokenBaru, _) = await LoginAsync(owner, username, SandiBaru);
        using var setelahMasukLagi = Klien(owner, tokenBaru);
        (await setelahMasukLagi.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.OK,
            "masuk kembali memberi token dengan versi terbaru");
    }

    [Fact]
    public async Task Kasir_HanyaMelihatPesananYangIaCatatSendiri()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 1_200_000);
        var (idA, _, tokenA) = await BuatPenggunaAsync(owner, "Pegawai");
        var (idB, _, tokenB) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasirA = Klien(owner, tokenA);
        using var kasirB = Klien(owner, tokenB);

        object Pesanan(string nama) => new
        {
            customerName = nama,
            description = "Uji cakupan kasir",
            pickupDate = "2030-08-08T10:00:00",
            totalAmount = 1_200_000,
            downPayment = 0,
            productTemplateId = produk,
            quantity = 1,
        };

        var namaA = $"Kasir A {Guid.NewGuid():N}"[..24];
        var namaB = $"Kasir B {Guid.NewGuid():N}"[..24];
        var pesananA = await kasirA.PostAsJsonAsync("/api/orders", Pesanan(namaA));
        var pesananB = await kasirB.PostAsJsonAsync("/api/orders", Pesanan(namaB));
        pesananA.StatusCode.Should().Be(HttpStatusCode.Created, await pesananA.Content.ReadAsStringAsync());
        pesananB.StatusCode.Should().Be(HttpStatusCode.Created, await pesananB.Content.ReadAsStringAsync());
        var idPesananB = JsonDocument.Parse(await pesananB.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        using var daftar = JsonDocument.Parse(await kasirA.GetStringAsync("/api/orders"));
        var baris = daftar.RootElement.EnumerateArray().ToList();
        baris.Should().NotBeEmpty();
        baris.Should().OnlyContain(o => o.GetProperty("createdByUserId").GetInt32() == idA,
            "daftar kasir hanya memuat pesanannya sendiri");
        baris.Should().Contain(o => o.GetProperty("customerNameSnapshot").GetString() == namaA);
        baris.Should().NotContain(o => o.GetProperty("customerNameSnapshot").GetString() == namaB);

        (await kasirA.GetAsync($"/api/orders/{idPesananB}")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "keberadaan pesanan kasir lain pun bukan informasi untuknya");

        using var semua = JsonDocument.Parse(await owner.GetStringAsync("/api/orders"));
        var namaSemua = semua.RootElement.EnumerateArray()
            .Select(o => o.GetProperty("customerNameSnapshot").GetString())
            .ToList();
        namaSemua.Should().Contain(namaA).And.Contain(namaB, "pemilik tetap melihat seluruh toko");
    }

    [Fact]
    public async Task KotakTugasKasir_TidakMembocorkanPesananKasirLain()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 1_200_000);
        var (_, _, tokenA) = await BuatPenggunaAsync(owner, "Pegawai");
        var (_, _, tokenB) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasirA = Klien(owner, tokenA);
        using var kasirB = Klien(owner, tokenB);

        object Pesanan(string nama) => new
        {
            customerName = nama,
            description = "Uji cakupan kotak tugas",
            // Tanggal jauh di depan tetapi paling awal, supaya kedua pesanan uji tetap berada
            // di dalam batas tampil kotak tugas walaupun basis data uji sudah berisi pesanan lain.
            pickupDate = "2030-01-01T10:00:00",
            totalAmount = 1_200_000,
            downPayment = 0,
            productTemplateId = produk,
            quantity = 1,
        };

        var namaA = $"Tugas A {Guid.NewGuid():N}"[..24];
        var namaB = $"Tugas B {Guid.NewGuid():N}"[..24];
        using var pesananA = await kasirA.PostAsJsonAsync("/api/orders", Pesanan(namaA));
        using var pesananB = await kasirB.PostAsJsonAsync("/api/orders", Pesanan(namaB));
        pesananA.StatusCode.Should().Be(HttpStatusCode.Created, await pesananA.Content.ReadAsStringAsync());
        pesananB.StatusCode.Should().Be(HttpStatusCode.Created, await pesananB.Content.ReadAsStringAsync());
        var idA = JsonDocument.Parse(await pesananA.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();
        var idB = JsonDocument.Parse(await pesananB.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        using var tugasA = JsonDocument.Parse(await kasirA.GetStringAsync("/api/tasks"));
        var tautanA = tugasA.RootElement.GetProperty("item").EnumerateArray()
            .Where(i => i.GetProperty("jenis").GetString() is "pengambilan" or "piutang")
            .Select(i => i.GetProperty("tautanId").GetInt32())
            .ToList();
        tautanA.Should().Contain(idA, "pekerjaan kasir sendiri tetap muncul");
        tautanA.Should().NotContain(idB,
            "kotak tugas tidak boleh menjadi jalan pintas melihat pesanan kasir lain");
        tugasA.RootElement.GetProperty("perJenis").GetProperty("pengambilan").GetInt32().Should().Be(1,
            "hitungan per jenis mengikuti cakupan yang sama dengan daftar barisnya");

        using var tugasOwner = JsonDocument.Parse(await owner.GetStringAsync("/api/tasks"));
        var tautanOwner = tugasOwner.RootElement.GetProperty("item").EnumerateArray()
            .Where(i => i.GetProperty("jenis").GetString() is "pengambilan" or "piutang")
            .Select(i => i.GetProperty("tautanId").GetInt32())
            .ToList();
        tautanOwner.Should().Contain(idA).And.Contain(idB,
            "Owner melihat pekerjaan seluruh kasir");
    }

    [Fact]
    public async Task DaftarPiutang_HanyaPesananBersisaDanBisaBerhalaman()
    {
        var owner = await OwnerAsync();

        object Pesanan(string nama, long dibayar) => new
        {
            customerName = nama,
            description = "Uji daftar piutang keuangan",
            pickupDate = "2029-12-31T10:00:00",
            totalAmount = 1_200_000,
            downPayment = dibayar,
        };

        var namaLunas = $"Lunas {Guid.NewGuid():N}"[..24];
        var namaSisa = $"Sisa {Guid.NewGuid():N}"[..24];
        var namaSisa2 = $"Sisa2 {Guid.NewGuid():N}"[..24];
        using var lunas = await owner.PostAsJsonAsync("/api/orders", Pesanan(namaLunas, 1_200_000));
        using var sisa = await owner.PostAsJsonAsync("/api/orders", Pesanan(namaSisa, 0));
        using var sisa2 = await owner.PostAsJsonAsync("/api/orders", Pesanan(namaSisa2, 0));
        lunas.StatusCode.Should().Be(HttpStatusCode.Created, await lunas.Content.ReadAsStringAsync());
        sisa.StatusCode.Should().Be(HttpStatusCode.Created, await sisa.Content.ReadAsStringAsync());
        sisa2.StatusCode.Should().Be(HttpStatusCode.Created, await sisa2.Content.ReadAsStringAsync());

        static int Total(HttpResponseMessage r) =>
            int.Parse(r.Headers.GetValues("X-Total-Count").Single());

        using var semua = await owner.GetAsync("/api/orders?limit=1");
        using var piutang = await owner.GetAsync("/api/orders?outstanding=true&limit=500");
        piutang.StatusCode.Should().Be(HttpStatusCode.OK);
        using var dokPiutang = JsonDocument.Parse(await piutang.Content.ReadAsStringAsync());
        var baris = dokPiutang.RootElement.EnumerateArray().ToList();
        baris.Select(o => o.GetProperty("customerNameSnapshot").GetString())
            .Should().Contain(namaSisa)
            .And.NotContain(namaLunas, "pesanan yang sudah dibayar penuh bukan piutang");

        Total(piutang).Should().Be(baris.Count,
            "jumlah total harus sama dengan baris yang dikembalikan saat seluruhnya muat satu halaman");
        Total(piutang).Should().BeLessThan(Total(semua),
            "jumlah piutang tidak boleh menyamakan pesanan lunas dengan pesanan bersisa");

        // Halaman kedua tidak boleh mengulang baris halaman pertama.
        using var hal1 = await owner.GetAsync("/api/orders?outstanding=true&limit=1&offset=0");
        using var hal2 = await owner.GetAsync("/api/orders?outstanding=true&limit=1&offset=1");
        using var dok1 = JsonDocument.Parse(await hal1.Content.ReadAsStringAsync());
        using var dok2 = JsonDocument.Parse(await hal2.Content.ReadAsStringAsync());
        dok1.RootElement.GetArrayLength().Should().Be(1);
        var id1 = dok1.RootElement[0].GetProperty("id").GetInt32();
        var id2 = dok2.RootElement[0].GetProperty("id").GetInt32();
        id1.Should().NotBe(id2, "offset harus memindahkan halaman, bukan mengulang baris");
        Total(hal1).Should().Be(Total(piutang));
    }

    [Fact]
    public async Task KebijakanSandi_DitegakkanServerSaatMembuatDanMeresetAkun()
    {
        var owner = await OwnerAsync();

        var pendek = await owner.PostAsJsonAsync("/api/users",
            new { username = $"pendek{Guid.NewGuid():N}"[..20], password = "pendek1", role = "Pegawai" });
        var teksPendek = await pendek.Content.ReadAsStringAsync();
        pendek.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        teksPendek.Should().Contain("minimal 8");

        var tanpaAngka = await owner.PostAsJsonAsync("/api/users",
            new { username = $"huruf{Guid.NewGuid():N}"[..20], password = "hanyahuruf", role = "Pegawai" });
        var teksTanpaAngka = await tanpaAngka.Content.ReadAsStringAsync();
        tanpaAngka.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        teksTanpaAngka.Should().Contain("huruf dan angka");

        var (id, _, _) = await BuatPenggunaAsync(owner, "Pegawai");
        var reset = await owner.PutAsJsonAsync($"/api/users/{id}",
            new { role = "Pegawai", isActive = true, passwordSementara = "lemah1" });
        var teksReset = await reset.Content.ReadAsStringAsync();
        reset.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        teksReset.Should().Contain("Kata sandi sementara");
    }

    [Fact]
    public async Task PenulisanSerentak_KunciSamaHanyaSatuPesanan()
    {
        var owner = await OwnerAsync();
        var nama = $"Serentak {Guid.NewGuid():N}"[..26];
        var kunci = $"serentak-{Guid.NewGuid():N}";
        object isi = new
        {
            customerName = nama,
            description = "Uji dua permintaan berbarengan",
            pickupDate = "2030-09-09T10:00:00",
            totalAmount = 2_000_000,
            downPayment = 0,
        };

        var jawaban = await Task.WhenAll(
            KirimAsync(owner, "/api/orders", isi, kunci),
            KirimAsync(owner, "/api/orders", isi, kunci));

        jawaban.Count(j => j.Kode == HttpStatusCode.Created).Should().Be(1,
            "hanya satu permintaan yang boleh membuat pesanan, yang lain menunggu atau menyerah");
        jawaban.Should().OnlyContain(j =>
                j.Kode == HttpStatusCode.Created || j.Kode == HttpStatusCode.OK || j.Kode == HttpStatusCode.Conflict,
            "jawaban kedua harus berupa pengulangan yang sama atau 409, bukan galat lain");

        using var daftar = JsonDocument.Parse(await owner.GetStringAsync("/api/orders"));
        daftar.RootElement.EnumerateArray()
            .Count(o => o.GetProperty("customerNameSnapshot").GetString() == nama)
            .Should().Be(1);
    }

    [Fact]
    public async Task PenulisanSerentak_PelunasanGandaTidakMencatatDuaPembayaran()
    {
        var owner = await OwnerAsync();

        // Tanpa produk katalog: yang diuji pagar pelunasan, bukan ketersediaan stok etalase.
        using var buat = await owner.PostAsJsonAsync("/api/orders", new
        {
            customerName = $"Serentak Bayar {Guid.NewGuid():N}"[..26],
            description = "Uji pelunasan berbarengan",
            pickupDate = "2030-09-09T10:00:00",
            totalAmount = 1_500_000,
            downPayment = 0,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        var pesanan = JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();

        var bayar = new { amount = 1_500_000, method = "Tunai", notes = "Uji serentak" };
        var jawaban = await Task.WhenAll(
            KirimAsync(owner, $"/api/orders/{pesanan}/payment", bayar, $"bayar-a-{Guid.NewGuid():N}"),
            KirimAsync(owner, $"/api/orders/{pesanan}/payment", bayar, $"bayar-b-{Guid.NewGuid():N}"));

        jawaban.Count(j => j.Kode == HttpStatusCode.OK).Should().Be(1,
            "dua pelunasan berbarengan tidak boleh sama-sama diterima");
        jawaban.Count(j => j.Kode != HttpStatusCode.OK).Should().Be(1);

        using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/orders/{pesanan}"));
        var akar = dok.RootElement;
        akar.GetProperty("status").GetInt32().Should().Be(3, "OrderStatus.Paid");
        akar.GetProperty("payments").GetArrayLength().Should().Be(1,
            "uang yang tercatat hanya satu kali meski dua permintaan datang bersamaan");
        akar.GetProperty("payments").EnumerateArray().Sum(p => p.GetProperty("amount").GetInt64())
            .Should().Be(1_500_000);
    }

    [Fact]
    public async Task PenulisanSerentak_PenyelesaianSpkTidakMenggandakanHasil()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 800_000);
        var spk = await BuatSpkAsync(owner, produk, 10);

        var jawaban = await Task.WhenAll(
            KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
            {
                actualGoodQty = 10, actualFailedQty = 0,
                materials = Array.Empty<object>(), scraps = Array.Empty<object>(),
            }),
            KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
            {
                actualGoodQty = 10, actualFailedQty = 0,
                materials = Array.Empty<object>(), scraps = Array.Empty<object>(),
            }));

        jawaban.Count(j => j.Kode == HttpStatusCode.OK).Should().Be(1,
            "SPK yang sudah selesai tidak boleh diselesaikan ulang");

        using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/production/orders/{spk}"));
        dok.RootElement.GetProperty("status").GetInt32().Should().Be(2, "WorkOrderStatus.Completed");
        dok.RootElement.GetProperty("actualGoodQty").GetInt32().Should().Be(10);

        (await StokEtalaseAsync(owner, produk)).Should().Be(10,
            "hasil produksi tidak boleh tercatat dua kali di stok etalase");
    }

    // ── BUG-004/005: bahan, harga, dan output penyelesaian SPK milik server ───

    private static async Task<int> BuatBahanAsync(HttpClient owner, long hargaMinor)
    {
        var nama = $"Bahan Uji {Guid.NewGuid():N}"[..24];
        using var buat = await owner.PostAsJsonAsync("/api/ingredients", new
        {
            name = nama,
            unit = "kg",
            pricePerUnit = hargaMinor,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<int> BuatProdukBeresepeAsync(HttpClient owner, params int[] bahanIds)
    {
        var nama = $"Produk Resep {Guid.NewGuid():N}"[..26];
        using var buat = await owner.PostAsJsonAsync("/api/products", new
        {
            name = nama,
            category = "Uji SPK",
            sellingPriceMinorUnits = 1_000_000,
            lines = bahanIds.Select(id => new { ingredientId = id, quantity = 1m }).ToArray(),
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<int> LokasiAsync(HttpClient owner, string tipe)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/inventory/locations"));
        return dok.RootElement.EnumerateArray()
            .First(l => l.GetProperty("type").GetString() == tipe)
            .GetProperty("id").GetInt32();
    }

    private static async Task<decimal> StokBahanAsync(
        HttpClient owner, int locationId, int ingredientId)
    {
        using var dok = JsonDocument.Parse(
            await owner.GetStringAsync($"/api/inventory/locations/{locationId}/stock"));
        var baris = dok.RootElement.EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("ingredientId").GetInt32() == ingredientId);
        return baris.ValueKind == JsonValueKind.Undefined ? 0m : baris.GetProperty("quantity").GetDecimal();
    }

    private static async Task IsiStokDapurAsync(
        HttpClient owner, int kitchenId, int ingredientId, decimal quantity)
    {
        var hasil = await KirimAsync(owner, "/api/inventory/adjust", new
        {
            locationId = kitchenId,
            ingredientId,
            newQuantity = quantity,
            reason = "Uji penyelesaian SPK",
        });
        hasil.Kode.Should().Be(HttpStatusCode.OK, hasil.Teks);
    }

    private static async Task<JsonElement> EntriJurnalSpkAsync(HttpClient owner, int spkId)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/journal?limit=1000"));
        return dok.RootElement.EnumerateArray()
            .First(e => e.TryGetProperty("sourceDocumentId", out var sid)
                        && sid.ValueKind == JsonValueKind.Number
                        && sid.GetInt32() == spkId
                        && e.GetProperty("sourceDocumentType").GetString() == "WorkOrder")
            .Clone();
    }

    private static long DebitAkun(JsonElement entri, string kode) =>
        entri.GetProperty("lines").EnumerateArray()
            .Where(l => l.TryGetProperty("account", out var a)
                        && a.ValueKind == JsonValueKind.Object
                        && a.GetProperty("code").GetString() == kode)
            .Sum(l => l.GetProperty("debitAmount").GetInt64());

    [Fact]
    public async Task PenyelesaianSpk_MenolakBahanKosongAsingDanDuplikat_TanpaMengubahStok()
    {
        var owner = await OwnerAsync();
        var dapur = await LokasiAsync(owner, "Kitchen");
        var tepung = await BuatBahanAsync(owner, 10_000);
        var asing = await BuatBahanAsync(owner, 5_000);
        await IsiStokDapurAsync(owner, dapur, tepung, 10m);
        var produk = await BuatProdukBeresepeAsync(owner, tepung);
        var spk = await BuatSpkAsync(owner, produk, 2);

        var kosong = await KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
        {
            actualGoodQty = 2,
            actualFailedQty = 0,
            materials = Array.Empty<object>(),
            scraps = Array.Empty<object>(),
        });
        kosong.Kode.Should().Be(HttpStatusCode.BadRequest, kosong.Teks);

        var bahanAsing = await KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
        {
            actualGoodQty = 2,
            actualFailedQty = 0,
            materials = new[] { new { ingredientId = asing, actualQty = 1m, unitCost = 1 } },
            scraps = Array.Empty<object>(),
        });
        bahanAsing.Kode.Should().Be(HttpStatusCode.BadRequest, bahanAsing.Teks);

        var duplikat = await KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
        {
            actualGoodQty = 2,
            actualFailedQty = 0,
            materials = new[]
            {
                new { ingredientId = tepung, actualQty = 1m, unitCost = 1 },
                new { ingredientId = tepung, actualQty = 1m, unitCost = 1 },
            },
            scraps = Array.Empty<object>(),
        });
        duplikat.Kode.Should().Be(HttpStatusCode.BadRequest, duplikat.Teks);

        (await StokBahanAsync(owner, dapur, tepung)).Should().Be(10m,
            "penyelesaian yang ditolak tidak boleh menggeser stok bahan");
        (await StokBahanAsync(owner, dapur, asing)).Should().Be(0m,
            "bahan asing tidak boleh ikut terpakai");

        using var wo = JsonDocument.Parse(await owner.GetStringAsync($"/api/production/orders/{spk}"));
        wo.RootElement.GetProperty("status").GetInt32().Should().Be(0, "WorkOrderStatus.Planned");
    }

    [Fact]
    public async Task PenyelesaianSpk_MenolakBahanRencanaYangTidakDilaporkan()
    {
        var owner = await OwnerAsync();
        var dapur = await LokasiAsync(owner, "Kitchen");
        var tepung = await BuatBahanAsync(owner, 10_000);
        var gula = await BuatBahanAsync(owner, 4_000);
        await IsiStokDapurAsync(owner, dapur, tepung, 5m);
        await IsiStokDapurAsync(owner, dapur, gula, 5m);
        var produk = await BuatProdukBeresepeAsync(owner, tepung, gula);
        var spk = await BuatSpkAsync(owner, produk, 2);

        var kurang = await KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
        {
            actualGoodQty = 2,
            actualFailedQty = 0,
            materials = new[] { new { ingredientId = tepung, actualQty = 2m, unitCost = 1 } },
            scraps = Array.Empty<object>(),
        });
        kurang.Kode.Should().Be(HttpStatusCode.BadRequest, kurang.Teks);

        (await StokBahanAsync(owner, dapur, tepung)).Should().Be(5m,
            "bahan yang sudah dilaporkan tidak boleh terpakai saat permintaan ditolak");
        (await StokBahanAsync(owner, dapur, gula)).Should().Be(5m);
    }

    [Fact]
    public async Task PenyelesaianSpk_MengabaikanHargaSatuanDariKlien()
    {
        var owner = await OwnerAsync();
        var dapur = await LokasiAsync(owner, "Kitchen");
        var tepung = await BuatBahanAsync(owner, 10_000);
        await IsiStokDapurAsync(owner, dapur, tepung, 10m);
        var produk = await BuatProdukBeresepeAsync(owner, tepung);
        var spk = await BuatSpkAsync(owner, produk, 2);

        var selesai = await KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
        {
            actualGoodQty = 2,
            actualFailedQty = 0,
            materials = new[] { new { ingredientId = tepung, actualQty = 2m, unitCost = 1 } },
            scraps = Array.Empty<object>(),
        });
        selesai.Kode.Should().Be(HttpStatusCode.OK, selesai.Teks);

        (await StokBahanAsync(owner, dapur, tepung)).Should().Be(8m);
        var entri = await EntriJurnalSpkAsync(owner, spk);
        DebitAkun(entri, "1-006").Should().Be(20_000,
            "nilai jurnal memakai harga satuan rencana SPK, bukan harga dari payload");
    }

    [Fact]
    public async Task PenyelesaianSpk_TanpaOutput_Ditolak_DanTanpaUnitBaikSeluruhBahanJadiKerugian()
    {
        var owner = await OwnerAsync();
        var dapur = await LokasiAsync(owner, "Kitchen");
        var tepung = await BuatBahanAsync(owner, 10_000);
        await IsiStokDapurAsync(owner, dapur, tepung, 10m);
        var produk = await BuatProdukBeresepeAsync(owner, tepung);
        var spk = await BuatSpkAsync(owner, produk, 2);

        var nol = await KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
        {
            actualGoodQty = 0,
            actualFailedQty = 0,
            materials = new[] { new { ingredientId = tepung, actualQty = 2m, unitCost = 1 } },
            scraps = Array.Empty<object>(),
        });
        nol.Kode.Should().Be(HttpStatusCode.BadRequest, nol.Teks);
        (await StokBahanAsync(owner, dapur, tepung)).Should().Be(10m,
            "penyelesaian tanpa output tidak boleh mengurangi stok");

        var gagalSemua = await KirimAsync(owner, $"/api/production/orders/{spk}/complete", new
        {
            actualGoodQty = 0,
            actualFailedQty = 2,
            materials = new[] { new { ingredientId = tepung, actualQty = 2m, unitCost = 1 } },
            scraps = new[]
            {
                new { quantity = 2, reasonCategory = "Gosong", actionTaken = "Scrap_Loss", notes = "Uji" },
            },
        });
        gagalSemua.Kode.Should().Be(HttpStatusCode.OK, gagalSemua.Teks);

        (await StokEtalaseAsync(owner, produk)).Should().Be(0m,
            "tidak ada unit baik yang boleh masuk etalase");
        var entri = await EntriJurnalSpkAsync(owner, spk);
        DebitAkun(entri, "1-006").Should().Be(0,
            "tanpa unit baik tidak boleh ada aset produk jadi tanpa stok pendukung");
        DebitAkun(entri, "5-004").Should().Be(20_000,
            "seluruh biaya bahan harus menjadi beban produk rusak");
    }
}
