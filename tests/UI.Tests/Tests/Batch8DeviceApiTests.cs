using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Endpoint perangkat diuji lewat HTTP sungguhan pada basis data sementara. Yang dibuktikan:
/// hanya pemilik yang boleh memilih printer, nilai pengaturan yang tidak masuk akal ditolak
/// server, kasir tidak bisa memakai jalur ini untuk melihat perangkat toko, dan kegagalan
/// printer tidak pernah menghapus atau mengubah pesanan yang sudah tersimpan.
/// </summary>
[Collection("UI Tests")]
public sealed class Batch8DeviceApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public Batch8DeviceApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private const string SandiAwal = "SandiSementara1";
    private const string SandiBaru = "SandiHarianKasir1";

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static HttpClient Klien(HttpClient induk, string token)
    {
        var http = new HttpClient { BaseAddress = induk.BaseAddress!, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<(int Id, string Token)> BuatPegawaiAsync(HttpClient owner)
    {
        var username = $"ujikasir{Guid.NewGuid():N}"[..24];
        using var buat = await owner.PostAsJsonAsync("/api/users",
            new { username, password = SandiAwal, role = "Pegawai" });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        var id = JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();

        using (var http = new HttpClient { BaseAddress = owner.BaseAddress! })
        {
            using var masuk = await http.PostAsJsonAsync("/api/auth/login", new { username, password = SandiAwal });
            var sesi = JsonDocument.Parse(await masuk.Content.ReadAsStringAsync()).RootElement;
            sesi.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
            var tokenBawaan = sesi.GetProperty("token").GetString()!;

            using var klien = Klien(owner, tokenBawaan);
            var ganti = await klien.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = SandiAwal, newPassword = SandiBaru });
            ganti.StatusCode.Should().Be(HttpStatusCode.OK, await ganti.Content.ReadAsStringAsync());
        }

        using (var http = new HttpClient { BaseAddress = owner.BaseAddress! })
        {
            using var masukLagi = await http.PostAsJsonAsync("/api/auth/login", new { username, password = SandiBaru });
            var akhir = JsonDocument.Parse(await masukLagi.Content.ReadAsStringAsync()).RootElement;
            akhir.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
            return (id, akhir.GetProperty("token").GetString()!);
        }
    }

    private static async Task<int> BuatProdukAsync(HttpClient owner, long hargaMinor)
    {
        var nama = $"Produk Uji {Guid.NewGuid():N}"[..26];
        using var buat = await owner.PostAsJsonAsync("/api/products", new
        {
            name = nama,
            category = "Uji Batch-8",
            sellingPriceMinorUnits = hargaMinor,
            lines = Array.Empty<object>(),
        });
        buat.IsSuccessStatusCode.Should().BeTrue(await buat.Content.ReadAsStringAsync());

        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/products"));
        return dok.RootElement.EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == nama)
            .GetProperty("id").GetInt32();
    }

    private static async Task<(int Id, long Total, int Status)> BuatPesananAsync(
        HttpClient kasir, int produkId, long hargaMinor, string nama)
    {
        using var buat = await kasir.PostAsJsonAsync("/api/orders", new
        {
            customerName = nama,
            description = "Uji cetak Batch-8",
            pickupDate = "2030-09-09T10:00:00",
            totalAmount = hargaMinor,
            downPayment = 0,
            productTemplateId = produkId,
            quantity = 1,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        var id = JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();

        using var dok = JsonDocument.Parse(await kasir.GetStringAsync($"/api/orders/{id}"));
        var akar = dok.RootElement;
        return (id, akar.GetProperty("totalAmount").GetInt64(), akar.GetProperty("status").GetInt32());
    }

    private static async Task SetPengaturanAsync(HttpClient owner, object nilai)
    {
        using var resp = await owner.PutAsJsonAsync("/api/devices/settings", nilai);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> SnapshotAsync(HttpClient owner)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/devices"));
        return dok.RootElement.Clone();
    }

    private static async Task<List<int?>> JobUntukPesananAsync(HttpClient owner, int orderId)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/devices/print-jobs"));
        return dok.RootElement.EnumerateArray()
            .Where(j => j.GetProperty("orderId").ValueKind != JsonValueKind.Null
                        && j.GetProperty("orderId").GetInt32() == orderId)
            .Select(j => (int?)j.GetProperty("id").GetInt32())
            .ToList();
    }

    [Fact]
    public async Task Kasir_TidakBolehMelihatDaftarPrinterAtauMengubahPengaturan()
    {
        var owner = await OwnerAsync();
        var (_, token) = await BuatPegawaiAsync(owner);
        using var kasir = Klien(owner, token);

        (await kasir.GetAsync("/api/devices")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await kasir.GetAsync("/api/devices/print-jobs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await kasir.PutAsJsonAsync("/api/devices/settings",
            new { device = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await kasir.PostAsync("/api/devices/test-print", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Kasir_BolehMelihatStatusRingkasYangDibutuhkanLayarPenjualan()
    {
        var owner = await OwnerAsync();
        var (_, token) = await BuatPegawaiAsync(owner);
        using var kasir = Klien(owner, token);

        var resp = await kasir.GetAsync("/api/devices/status");

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        using var dok = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        dok.RootElement.TryGetProperty("printerConfigured", out _).Should().BeTrue();
        dok.RootElement.GetProperty("paperWidthMm").GetInt32().Should().BeOneOf(58, 80);
        dok.RootElement.TryGetProperty("printers", out _).Should().BeFalse(
            "daftar perangkat toko bukan urusan kasir");
    }

    [Fact]
    public async Task LebarKertasTidakDidukung_DitolakDanNilaiLamaTetap()
    {
        var owner = await OwnerAsync();
        await SetPengaturanAsync(owner, new Dictionary<string, string> { ["device.paper.width_mm"] = "58" });

        using var tolak = await owner.PutAsJsonAsync("/api/devices/settings",
            new Dictionary<string, string> { ["device.paper.width_mm"] = "57" });
        var teks = await tolak.Content.ReadAsStringAsync();
        tolak.StatusCode.Should().Be(HttpStatusCode.BadRequest, teks);
        JsonDocument.Parse(teks).RootElement.GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");

        var snapshot = await SnapshotAsync(owner);
        snapshot.GetProperty("settings").GetProperty("paperWidthMm").GetInt32().Should().Be(58,
            "nilai yang ditolak tidak boleh menimpa pengaturan lama");
    }

    [Fact]
    public async Task KunciPengaturanAsing_Ditolak()
    {
        var owner = await OwnerAsync();

        using var resp = await owner.PutAsJsonAsync("/api/devices/settings",
            new Dictionary<string, string> { ["device.rahasia"] = "1" });
        var teks = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, teks);
        teks.Should().Contain("device.rahasia");
    }

    [Fact]
    public async Task ModeEscPosTanpaAlamat_DitolakKarenaBelumLengkap()
    {
        var owner = await OwnerAsync();

        using var resp = await owner.PutAsJsonAsync("/api/devices/settings",
            new Dictionary<string, string> { ["device.printer.mode"] = "escpos-raw" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PrinterTidakTerpasang_Ditolak409TanpaMengubahPengaturan()
    {
        var owner = await OwnerAsync();
        var snapshot = await SnapshotAsync(owner);
        if (!snapshot.GetProperty("perangkatDidukung").GetBoolean())
            return; // host non-Windows tidak punya registry printer; jalur ini hanya nyata di Windows

        var sebelum = snapshot.GetProperty("settings").GetProperty("printerName").GetString();
        var nama = $"Printer Karangan {Guid.NewGuid():N}"[..30];

        using var resp = await owner.PutAsJsonAsync("/api/devices/settings",
            new Dictionary<string, string> { ["device.printer.name"] = nama });
        var teks = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict, teks);
        JsonDocument.Parse(teks).RootElement.GetProperty("errorCode").GetString()
            .Should().Be("PRINTER_NOT_FOUND");
        (await SnapshotAsync(owner)).GetProperty("settings").GetProperty("printerName").GetString()
            .Should().Be(sebelum);
    }

    [Fact]
    public async Task CetakTanpaPrinter_PesananTetapUtuhDanTidakAdaPekerjaanDibuat()
    {
        var owner = await OwnerAsync();
        await SetPengaturanAsync(owner, new Dictionary<string, string> { ["device.printer.name"] = "" });

        var produk = await BuatProdukAsync(owner, 1_750_000);
        var (_, token) = await BuatPegawaiAsync(owner);
        using var kasir = Klien(owner, token);
        var nama = $"Kasir Cetak {Guid.NewGuid():N}"[..24];
        var (pesanan, total, status) = await BuatPesananAsync(kasir, produk, 1_750_000, nama);

        using var cetak = await kasir.PostAsJsonAsync($"/api/orders/{pesanan}/receipt", new { cetakUlang = false });
        var teks = await cetak.Content.ReadAsStringAsync();

        cetak.StatusCode.Should().Be(HttpStatusCode.Conflict, teks);
        var galat = JsonDocument.Parse(teks).RootElement;
        galat.GetProperty("errorCode").GetString().Should().Be("PRINTER_NOT_CONFIGURED");
        galat.GetProperty("errorMessage").GetString().Should().Contain("Transaksi tetap tersimpan");

        using var ulang = JsonDocument.Parse(await kasir.GetStringAsync($"/api/orders/{pesanan}"));
        ulang.RootElement.GetProperty("totalAmount").GetInt64().Should().Be(total,
            "kegagalan cetak tidak boleh mengubah nilai pesanan");
        ulang.RootElement.GetProperty("status").GetInt32().Should().Be(status,
            "kegagalan cetak tidak boleh membatalkan atau memindahkan status pesanan");
        (await JobUntukPesananAsync(owner, pesanan)).Should().BeEmpty(
            "printer yang belum dipilih tidak boleh menghasilkan pekerjaan cetak");
    }

    [Fact]
    public async Task CetakUjiTanpaPrinter_DitolakDanTidakMencatatPekerjaan()
    {
        var owner = await OwnerAsync();
        await SetPengaturanAsync(owner, new Dictionary<string, string> { ["device.printer.name"] = "" });

        using var sebelum = JsonDocument.Parse(await owner.GetStringAsync("/api/devices/print-jobs"));
        var jumlahAwal = sebelum.RootElement.GetArrayLength();

        using var resp = await owner.PostAsync("/api/devices/test-print", null);
        var teks = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict, teks);
        JsonDocument.Parse(teks).RootElement.GetProperty("errorCode").GetString()
            .Should().Be("PRINTER_NOT_CONFIGURED");

        using var sesudah = JsonDocument.Parse(await owner.GetStringAsync("/api/devices/print-jobs"));
        sesudah.RootElement.GetArrayLength().Should().Be(jumlahAwal);
    }

    [Fact]
    public async Task Kasir_TidakBisaMencetakStrukPesananKasirLain()
    {
        var owner = await OwnerAsync();
        var produk = await BuatProdukAsync(owner, 900_000);
        var (_, tokenA) = await BuatPegawaiAsync(owner);
        var (_, tokenB) = await BuatPegawaiAsync(owner);
        using var kasirA = Klien(owner, tokenA);
        using var kasirB = Klien(owner, tokenB);

        var nama = $"Pesanan A {Guid.NewGuid():N}"[..24];
        var (pesanan, _, _) = await BuatPesananAsync(kasirA, produk, 900_000, nama);

        var resp = await kasirB.PostAsJsonAsync($"/api/orders/{pesanan}/receipt", new { cetakUlang = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "keberadaan pesanan kasir lain bukan informasi untuk kasir ini");
    }

    [Fact]
    public async Task BarcodeGandaLewatApi_Ditolak409DanProdukKeduaTidakDibuat()
    {
        var owner = await OwnerAsync();
        var barcode = $"BC{Guid.NewGuid():N}"[..14];
        var namaA = $"Barcode A {Guid.NewGuid():N}"[..20];
        var namaB = $"Barcode B {Guid.NewGuid():N}"[..20];

        using (var pertama = await owner.PostAsJsonAsync("/api/products", new
        {
            name = namaA, category = "Uji Batch-8", sellingPriceMinorUnits = 500_000,
            barcode, lines = Array.Empty<object>(),
        }))
        {
            pertama.IsSuccessStatusCode.Should().BeTrue(await pertama.Content.ReadAsStringAsync());
        }

        using var kedua = await owner.PostAsJsonAsync("/api/products", new
        {
            name = namaB, category = "Uji Batch-8", sellingPriceMinorUnits = 500_000,
            barcode, lines = Array.Empty<object>(),
        });
        var teks = await kedua.Content.ReadAsStringAsync();

        kedua.StatusCode.Should().Be(HttpStatusCode.Conflict, teks);
        JsonDocument.Parse(teks).RootElement.GetProperty("errorCode").GetString()
            .Should().Be("DUPLICATE_KEY");

        using var semua = JsonDocument.Parse(await owner.GetStringAsync("/api/products"));
        var nama = semua.RootElement.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        nama.Should().Contain(namaA).And.NotContain(namaB, "hasil pindai harus menunjuk satu produk saja");
    }

    [Fact]
    public async Task PengaturanPerangkat_TersimpanDanBertahanSetelahDibacaUlang()
    {
        var owner = await OwnerAsync();

        await SetPengaturanAsync(owner, new Dictionary<string, string>
        {
            ["device.paper.width_mm"] = "58",
            ["device.printer.auto_print"] = "1",
            ["device.scanner.suffix"] = "enter",
        });

        var snapshot = await SnapshotAsync(owner);
        var setelan = snapshot.GetProperty("settings");
        setelan.GetProperty("paperWidthMm").GetInt32().Should().Be(58);
        setelan.GetProperty("autoPrint").GetBoolean().Should().BeTrue();
        setelan.GetProperty("scannerSuffix").GetString().Should().Be("enter");

        await SetPengaturanAsync(owner, new Dictionary<string, string>
        {
            ["device.paper.width_mm"] = "80",
            ["device.printer.auto_print"] = "0",
        });
    }

    // ── Jalur ESC/POS langsung: bukti lewat HTTP sungguhan ke emulator loopback ──────
    // Host aplikasi yang sama, basis data sementara yang sama, hanya tujuannya socket. Jadi yang
    // dibuktikan di sini adalah seluruh rangkaian (endpoint → layanan → encoder → TCP), bukan
    // adaptor yang dipanggil sendiri.

    private static string Teks(byte[] data) => System.Text.Encoding.ASCII.GetString(data);

    private static Task KembalikanModeDriverAsync(HttpClient owner) =>
        SetPengaturanAsync(owner, new Dictionary<string, string> { ["device.printer.mode"] = "windows-driver" });

    [Fact]
    public async Task EscPosLewatHttp_StrukUjiTibaSampaiByteTerakhirDanTercatatPrinted()
    {
        var owner = await OwnerAsync();
        await using var em = LoopbackEscPosEmulator.Mulai();
        try
        {
            await SetPengaturanAsync(owner, new Dictionary<string, string>
            {
                ["device.printer.mode"] = "escpos-raw",
                ["device.escpos.host"] = em.Host,
                ["device.escpos.port"] = em.Port.ToString(),
                ["device.escpos.cut"] = "1",
                ["device.paper.width_mm"] = "58",
            });

            var snapshot = await SnapshotAsync(owner);
            snapshot.GetProperty("mode").GetString().Should().Be("escpos-raw");
            snapshot.GetProperty("settings").GetProperty("escPosHost").GetString().Should().Be(em.Host);
            snapshot.GetProperty("settings").GetProperty("escPosPort").GetInt32().Should().Be(em.Port);
            snapshot.GetProperty("settings").GetProperty("namaTujuan").GetString()
                .Should().Be($"tcp://{em.Host}:{em.Port}");
            snapshot.GetProperty("catatan").GetString().Should().BeEmpty(
                "printer jaringan tidak dinilai dari daftar printer Windows");

            using var resp = await owner.PostAsync("/api/devices/test-print", null);
            var teks = await resp.Content.ReadAsStringAsync();
            resp.StatusCode.Should().Be(HttpStatusCode.OK, teks);

            var tiba = await em.TungguByteAsync(200, TimeSpan.FromSeconds(10));
            tiba.Take(2).Should().Equal(new byte[] { 0x1B, 0x40 }, "ESC @ pembuka harus paling awal");
            tiba[^6..].Should().Equal(new byte[] { 0x1B, 0x64, 0x03, 0x1D, 0x56, 0x00 },
                "umpan lalu GS V 0: struk dianggap selesai sampai byte pemotong");
            var isi = Teks(tiba);
            isi.Should().Contain("UJI CETAK PERANGKAT").And.Contain("Rp 30.000")
                .And.Contain($"tcp://{em.Host}:{em.Port}");
            em.Sambungan.Should().Be(1, "satu pekerjaan cetak memakai satu sambungan");

            using var jobs = JsonDocument.Parse(await owner.GetStringAsync("/api/devices/print-jobs?limit=20"));
            var job = jobs.RootElement.EnumerateArray()
                .Single(j => j.GetProperty("kind").GetString() == "Test"
                             && j.GetProperty("printerName").GetString() == $"tcp://{em.Host}:{em.Port}");
            job.GetProperty("status").GetString().Should().Be("Printed");
        }
        finally
        {
            await KembalikanModeDriverAsync(owner);
        }
    }

    [Fact]
    public async Task EscPosLewatHttp_StrukPesananTibaDanPesananTetapUtuh()
    {
        var owner = await OwnerAsync();
        await using var em = LoopbackEscPosEmulator.Mulai();
        try
        {
            await SetPengaturanAsync(owner, new Dictionary<string, string>
            {
                ["device.printer.mode"] = "escpos-raw",
                ["device.escpos.host"] = em.Host,
                ["device.escpos.port"] = em.Port.ToString(),
                ["device.escpos.cut"] = "0",
            });

            var produk = await BuatProdukAsync(owner, 2_500_000);
            var nama = $"Kasir Escpos {Guid.NewGuid():N}"[..24];
            var (pesanan, total, status) = await BuatPesananAsync(owner, produk, 2_500_000, nama);

            using var cetak = await owner.PostAsJsonAsync($"/api/orders/{pesanan}/receipt", new { cetakUlang = false });
            var teks = await cetak.Content.ReadAsStringAsync();
            cetak.StatusCode.Should().Be(HttpStatusCode.OK, teks);

            var tiba = await em.TungguByteAsync(200, TimeSpan.FromSeconds(10));
            var isi = Teks(tiba);
            isi.Should().Contain(nama, "isi struk datang dari pesanan di basis data, bukan dari permintaan layar");
            isi.Should().Contain("Rp 25.000");
            tiba.Should().NotContain((byte)0x1D, "potong dimatikan lewat pengaturan, jadi GS V tidak boleh dikirim");

            using (var ulang = await owner.PostAsJsonAsync($"/api/orders/{pesanan}/receipt", new { cetakUlang = true }))
            {
                ulang.StatusCode.Should().Be(HttpStatusCode.OK, await ulang.Content.ReadAsStringAsync());
            }
            await em.TungguByteAsync(2 * tiba.Length, TimeSpan.FromSeconds(10));
            Teks(em.Diterima).Should().Contain("*** CETAK ULANG ***");

            (await JobUntukPesananAsync(owner, pesanan)).Should().HaveCount(2,
                "cetak dan cetak ulang masing-masing mencatat satu pekerjaan");

            using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/orders/{pesanan}"));
            dok.RootElement.GetProperty("totalAmount").GetInt64().Should().Be(total);
            dok.RootElement.GetProperty("status").GetInt32().Should().Be(status);
        }
        finally
        {
            await KembalikanModeDriverAsync(owner);
        }
    }

    [Fact]
    public async Task EscPosLewatHttp_PrinterTidakMenjawab_PesananUtuhDanJobTercatatFailed()
    {
        var owner = await OwnerAsync();
        // Port 1 di loopback tidak pernah menerima sambungan: ini jalur kabel lepas / printer mati.
        await SetPengaturanAsync(owner, new Dictionary<string, string>
        {
            ["device.printer.mode"] = "escpos-raw",
            ["device.escpos.host"] = "127.0.0.1",
            ["device.escpos.port"] = "1",
            ["device.escpos.cut"] = "0",
        });
        try
        {
            var produk = await BuatProdukAsync(owner, 1_200_000);
            var nama = $"Escpos mati {Guid.NewGuid():N}"[..24];
            var (pesanan, total, status) = await BuatPesananAsync(owner, produk, 1_200_000, nama);

            using var cetak = await owner.PostAsJsonAsync($"/api/orders/{pesanan}/receipt", new { cetakUlang = false });
            var teks = await cetak.Content.ReadAsStringAsync();
            cetak.StatusCode.Should().Be(HttpStatusCode.Conflict, teks);
            var galat = JsonDocument.Parse(teks).RootElement;
            galat.GetProperty("errorCode").GetString().Should().Be("PRINT_FAILED");
            galat.GetProperty("errorMessage").GetString().Should().Contain("Transaksi tetap tersimpan");
            galat.GetProperty("jobId").ValueKind.Should().NotBe(JsonValueKind.Null,
                "pekerjaan gagal harus tercatat, bukan hilang");

            using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/orders/{pesanan}"));
            dok.RootElement.GetProperty("totalAmount").GetInt64().Should().Be(total);
            dok.RootElement.GetProperty("status").GetInt32().Should().Be(status);

            using var jobs = JsonDocument.Parse(await owner.GetStringAsync("/api/devices/print-jobs?limit=20"));
            var job = jobs.RootElement.EnumerateArray().Single(j =>
                j.GetProperty("orderId").ValueKind != JsonValueKind.Null
                && j.GetProperty("orderId").GetInt32() == pesanan);
            job.GetProperty("status").GetString().Should().Be("Failed");
            job.GetProperty("lastError").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            await KembalikanModeDriverAsync(owner);
        }
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("100.64.0.1")]
    [InlineData("http://192.168.1.20")]
    [InlineData("192.168.1")]
    public async Task EscPosHostKeluarJaringanToko_DitolakServer(string host)
    {
        var owner = await OwnerAsync();

        using var resp = await owner.PutAsJsonAsync("/api/devices/settings", new Dictionary<string, string>
        {
            ["device.printer.mode"] = "escpos-raw",
            ["device.escpos.host"] = host,
            ["device.escpos.port"] = "9100",
        });
        var teks = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, teks);
        JsonDocument.Parse(teks).RootElement.GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
        var snapshot = await SnapshotAsync(owner);
        snapshot.GetProperty("settings").GetProperty("escPosHost").GetString().Should().NotBe(host,
            "alamat yang ditolak tidak boleh tersimpan separuh");
    }
}
