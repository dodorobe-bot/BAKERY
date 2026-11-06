using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Kasus uji angka baris 55-56: beban harus bisa diberi tanggal sendiri, dan bulan buku yang
/// sudah ditutup pemilik harus benar-benar menolak tulisan baru.
///
/// Yang diuji lewat HTTP sungguhan adalah janji ke layar Laporan keuangan: daftar periode berasal
/// dari isi jurnal, tombol tutup/buka hanya mengubah status, dan pagar menahan jalur tulis mana pun
/// karena berada di satu penulis jurnal. Baris berstatus Locked disisipkan langsung ke basis data
/// sementara — tidak ada endpoint yang boleh membuatnya — untuk membuktikan cabang penolakan yang
/// tidak bisa dicapai dari layar.
/// </summary>
[Collection("UI Tests")]
public sealed class PeriodCloseApiTests : IClassFixture<PlaywrightFixture>
{
    private const long BiayaOperasional = 12_000_000; // Rp 120.000 dalam sen
    private const string SandiAwal = "SandiSementara1";
    private const string SandiBaru = "SandiHarianPeriode1";

    private readonly PlaywrightFixture _fixture;

    public PeriodCloseApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static DateTime HariToko => BusinessTime.Today();

    private static string Bulan(DateTime tanggal) => tanggal.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Tanggal(DateTime tanggal) => tanggal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime AwalBulan(DateTime tanggal) => new(tanggal.Year, tanggal.Month, 1);

    private static string TanggalDiBulan(DateTime acuan, int hari) =>
        Tanggal(new DateTime(acuan.Year, acuan.Month, hari));

    private static string Penanda() => "uji periode " + Guid.NewGuid().ToString("N")[..10];

    private static HttpClient Klien(HttpClient induk, string token)
    {
        var http = new HttpClient { BaseAddress = induk.BaseAddress!, Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<(int Id, string Token)> BuatPenggunaAsync(HttpClient owner, string role)
    {
        var username = $"uji{role.ToLowerInvariant()}{Guid.NewGuid():N}"[..24];
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

    private static async Task<HttpResponseMessage> CatatBebanAsync(
        HttpClient http, string keterangan, long nominal, string? tanggal)
    {
        return await http.PostAsJsonAsync("/api/expenses", new
        {
            category = "Operasional",
            description = keterangan,
            amount = nominal,
            transactionDate = tanggal,
        });
    }

    private static async Task<JsonElement?> CariPeriodeAsync(HttpClient http, string bulan)
    {
        using var dok = JsonDocument.Parse(await http.GetStringAsync("/api/periods"));
        foreach (var p in dok.RootElement.EnumerateArray())
        {
            if (p.GetProperty("startDate").GetString()?.StartsWith(bulan, StringComparison.Ordinal) == true)
                return p.Clone();
        }
        return null;
    }

    /// <summary>
    /// Semua kaki jurnal bertipe Expense. Waktu jurnal dikirim sebagai ISO lengkap (00:00 pada
    /// tanggal toko), jadi yang dibandingkan dengan kolom tanggal layar adalah bagian tanggalnya.
    /// </summary>
    private static async Task<List<(string Keterangan, string Tanggal)>> BebanAsync(HttpClient http)
    {
        using var dok = JsonDocument.Parse(await http.GetStringAsync("/api/journal?limit=1000"));
        return dok.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("sourceDocumentType").GetString() == "Expense")
            .Select(e => (
                e.GetProperty("description").GetString() ?? string.Empty,
                (e.GetProperty("transactionDate").GetString() ?? string.Empty).Split('T')[0]))
            .ToList();
    }

    [Fact]
    public async Task DaftarPeriode_MemuatBulanBerjalanDanBulanYangBerisiJurnal()
    {
        var http = await OwnerAsync();
        var bulanIni = Bulan(HariToko);

        var berjalan = await CariPeriodeAsync(http, bulanIni);
        berjalan.Should().NotBeNull("bulan berjalan harus selalu terlihat walau belum ada transaksi");
        var barisBerjalan = berjalan!.Value;
        barisBerjalan.GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        barisBerjalan.GetProperty("status").GetString().Should().Be("Open",
            "periode tanpa baris FiscalPeriods dianggap terbuka");
        barisBerjalan.GetProperty("startDate").GetString().Should().Be(Tanggal(AwalBulan(HariToko)));
        barisBerjalan.GetProperty("endDate").GetString().Should()
            .Be(Tanggal(AwalBulan(HariToko).AddMonths(1).AddDays(-1)));

        // Beban bertanggal mundur harus menciptakan baris periode yang bisa ditutup nanti.
        var tanggalLalu = HariToko.AddDays(-20);
        var keterangan = Penanda();
        using (var beban = await CatatBebanAsync(http, keterangan, BiayaOperasional, Tanggal(tanggalLalu)))
            beban.StatusCode.Should().Be(HttpStatusCode.OK, await beban.Content.ReadAsStringAsync());

        var barisLalu = await CariPeriodeAsync(http, Bulan(tanggalLalu));
        barisLalu.Should().NotBeNull("bulan yang sudah berisi jurnal harus muncul supaya bisa ditutup");
        barisLalu!.Value.GetProperty("entryCount").GetInt32().Should().BeGreaterThan(0);
        barisLalu.Value.GetProperty("debitTotalMinorUnits").GetInt64().Should().BeGreaterThanOrEqualTo(
            BiayaOperasional);
        barisLalu.Value.GetProperty("status").GetString().Should().Be("Open");

        var jurnal = await BebanAsync(http);
        jurnal.Should().Contain(e => e.Keterangan == "Biaya: " + keterangan && e.Tanggal == Tanggal(tanggalLalu),
            "beban harus dibukukan pada tanggal yang dipilih, bukan tanggal hari ini");
    }

    [Fact]
    public async Task TanpaTanggal_BerartiHariBisnisHariIni()
    {
        var http = await OwnerAsync();
        var keterangan = Penanda();

        using (var beban = await CatatBebanAsync(http, keterangan, 5_000_000, null))
            beban.StatusCode.Should().Be(HttpStatusCode.OK, await beban.Content.ReadAsStringAsync());

        var jurnal = await BebanAsync(http);
        jurnal.Should().Contain(e => e.Keterangan == "Biaya: " + keterangan && e.Tanggal == Tanggal(HariToko),
            "layar lama yang belum mengirim tanggal tidak boleh pecah dan tetap jatuh di hari bisnis ini");
    }

    [Fact]
    public async Task TanggalMasaDepanFormatSalahDanLebihDari366Hari_Dijawab400TanpaMenulisJurnal()
    {
        var http = await OwnerAsync();
        var sebelum = (await BebanAsync(http)).Count;

        using (var masaDepan = await CatatBebanAsync(http, Penanda(), 5_000_000,
                   Tanggal(HariToko.AddDays(3))))
        {
            masaDepan.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await masaDepan.Content.ReadAsStringAsync()).Should().Contain("masa depan");
        }

        using (var formatSalah = await CatatBebanAsync(http, Penanda(), 5_000_000, "05/11/2026"))
        {
            formatSalah.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await formatSalah.Content.ReadAsStringAsync()).Should().Contain("YYYY-MM-DD");
        }

        using (var terlaluTua = await CatatBebanAsync(http, Penanda(), 5_000_000,
                   Tanggal(HariToko.AddDays(-400))))
        {
            terlaluTua.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await terlaluTua.Content.ReadAsStringAsync()).Should().Contain("366",
                "salah ketik tahun harus ditolak dengan menyebut batasnya");
        }

        (await BebanAsync(http)).Count.Should().Be(sebelum, "permintaan yang ditolak tidak boleh meninggalkan jurnal");
    }

    [Fact]
    public async Task TutupBulanLalu_MenahanTulisDanBolehDibukaKembali()
    {
        var http = await OwnerAsync();
        var acuan = HariToko.AddDays(-40);
        var bulan = Bulan(acuan);
        var tanggal = TanggalDiBulan(acuan, 5);

        // Bulan itu harus punya isi nyata lebih dulu: yang dikunci adalah jurnal yang benar-benar ada.
        using (var awal = await CatatBebanAsync(http, Penanda(), 15_000_000, tanggal))
            awal.StatusCode.Should().Be(HttpStatusCode.OK, await awal.Content.ReadAsStringAsync());

        try
        {
            using (var tutup = await http.PostAsJsonAsync("/api/periods/close", new { month = bulan }))
            {
                var isi = await tutup.Content.ReadAsStringAsync();
                tutup.StatusCode.Should().Be(HttpStatusCode.OK, isi);
                using var dok = JsonDocument.Parse(isi);
                dok.RootElement.GetProperty("status").GetString().Should().Be("Closed");
                dok.RootElement.GetProperty("closedAt").GetString().Should().NotBeNullOrWhiteSpace();
                dok.RootElement.GetProperty("closedByName").GetString().Should().Be(AppLogin.Username);
            }

            var baris = (await CariPeriodeAsync(http, bulan))!.Value;
            baris.GetProperty("status").GetString().Should().Be("Closed");
            baris.GetProperty("entryCount").GetInt32().Should().BeGreaterThan(0,
                "pemilik melihat berapa kaki jurnal yang akan terkunci sebelum menutup");
            baris.GetProperty("debitTotalMinorUnits").GetInt64().Should().BeGreaterThanOrEqualTo(15_000_000);
            baris.GetProperty("closedByName").GetString().Should().Be(AppLogin.Username);

            var sebelum = (await BebanAsync(http)).Count;
            using (var tembus = await CatatBebanAsync(http, Penanda(), 9_900_000, tanggal))
            {
                tembus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                (await tembus.Content.ReadAsStringAsync()).Should()
                    .Contain("sudah ditutup").And.Contain("ditutup oleh",
                        "pesan harus menyebut bulan dan siapa yang menutupnya, bukan galat mesin");
            }
            (await BebanAsync(http)).Count.Should().Be(sebelum, "pagar periode menahan tulis, bukan hanya memberi pesan");

            using (var tutupLagi = await http.PostAsJsonAsync("/api/periods/close", new { month = bulan }))
            {
                tutupLagi.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                (await tutupLagi.Content.ReadAsStringAsync()).Should().Contain("sudah berstatus tertutup");
            }

            using (var buka = await http.PostAsJsonAsync("/api/periods/reopen", new { month = bulan }))
            {
                buka.StatusCode.Should().Be(HttpStatusCode.OK, await buka.Content.ReadAsStringAsync());
                using var dok = JsonDocument.Parse(await buka.Content.ReadAsStringAsync());
                dok.RootElement.GetProperty("status").GetString().Should().Be("Open");
            }

            using (var sesudahBuka = await CatatBebanAsync(http, Penanda(), 2_500_000, tanggal))
                sesudahBuka.StatusCode.Should().Be(HttpStatusCode.OK,
                    "menutup periode harus bisa dibatalkan tanpa pemulihan basis data");
        }
        finally
        {
            // Tes tidak boleh meninggalkan bulan terkunci untuk kelas lain di basis data yang sama.
            using var pulih = await http.PostAsJsonAsync("/api/periods/reopen", new { month = bulan });
        }

        using var audit = JsonDocument.Parse(
            await http.GetStringAsync("/api/audit?entity=FiscalPeriod&limit=100"));
        var aksi = audit.RootElement.EnumerateArray()
            .Select(a => a.GetProperty("action").GetString() ?? string.Empty)
            .ToList();
        aksi.Should().Contain("tutup periode").And.Contain("buka periode",
            "aksi yang mengubah hak tulis orang lain harus tercatat di Jejak Audit");
    }

    [Fact]
    public async Task TutupBulanBerjalanAtauBulanDepanAtauFormatSalah_DitolakDenganAlasan()
    {
        var http = await OwnerAsync();

        using (var berjalan = await http.PostAsJsonAsync("/api/periods/close", new { month = Bulan(HariToko) }))
        {
            berjalan.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await berjalan.Content.ReadAsStringAsync()).Should().Contain("masih berjalan",
                "hari ini ada di dalam bulan ini, jadi menutupnya membekukan pekerjaan hari ini");
        }

        var depan = AwalBulan(HariToko).AddMonths(1);
        using (var bulanDepan = await http.PostAsJsonAsync("/api/periods/close", new { month = Bulan(depan) }))
        {
            bulanDepan.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await bulanDepan.Content.ReadAsStringAsync()).Should().Contain("baru akan dimulai");
        }

        using (var formatSalah = await http.PostAsJsonAsync("/api/periods/close",
                   new { month = HariToko.ToString("yyyy/MM", CultureInfo.InvariantCulture) }))
        {
            formatSalah.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await formatSalah.Content.ReadAsStringAsync()).Should().Contain("YYYY-MM",
                "salah format tidak boleh menutup bulan lain secara diam-diam");
        }

        (await CariPeriodeAsync(http, Bulan(HariToko)))!.Value
            .GetProperty("status").GetString().Should().Be("Open", "penolakan tidak boleh mengubah status");
    }

    [Fact]
    public async Task HanyaOwnerYangBolehMenutup_AdminDanPegawaiDitolakTanpaMengubahStatus()
    {
        var owner = await OwnerAsync();
        var target = HariToko.AddDays(-70);
        var bulan = Bulan(target);

        var (_, tokenAdmin) = await BuatPenggunaAsync(owner, "Admin");
        using var admin = Klien(owner, tokenAdmin);
        (await admin.GetAsync("/api/periods")).StatusCode.Should().Be(HttpStatusCode.OK,
            "Admin boleh membaca laporan keuangan");
        (await admin.PostAsJsonAsync("/api/periods/close", new { month = bulan })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "menutup laporan hanya milik Owner");

        var (_, tokenPegawai) = await BuatPenggunaAsync(owner, "Pegawai");
        using var pegawai = Klien(owner, tokenPegawai);
        (await pegawai.GetAsync("/api/periods")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await pegawai.PostAsJsonAsync("/api/periods/reopen", new { month = bulan })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var baris = await CariPeriodeAsync(owner, bulan);
        if (baris is not null)
            baris.Value.GetProperty("status").GetString().Should().Be("Open",
                "percobaan tanpa hak tidak boleh meninggalkan status tertutup");
    }

    /// <summary>
    /// Status Locked tidak bisa dibuat dari layar mana pun; hanya sisipan langsung yang menghasilkannya.
    /// Cabang inilah yang dipakai menjaga supaya kunci permanen tidak bisa dibuka lewat API, dan
    /// membuktikan penolakan tulis tetap datang dari pagar jurnal yang sama.
    /// </summary>
    [Fact]
    public async Task BarisTerkunci_TidakBisaDibukaDanJurnalKeBulanItuDitolak()
    {
        var http = await OwnerAsync();
        var acuan = HariToko.AddMonths(-8);
        var mulai = AwalBulan(acuan);
        var bulan = Bulan(acuan);

        SisipkanPeriodeTerkunci(mulai);
        try
        {
            var baris = await CariPeriodeAsync(http, bulan);
            baris.Should().NotBeNull();
            baris!.Value.GetProperty("status").GetString().Should().Be("Locked");

            using (var buka = await http.PostAsJsonAsync("/api/periods/reopen", new { month = bulan }))
            {
                buka.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await buka.Content.ReadAsStringAsync()).Should().Contain("dikunci permanen");
            }

            using (var tembus = await CatatBebanAsync(http, Penanda(), 1_000_000, Tanggal(mulai.AddDays(4))))
            {
                tembus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                (await tembus.Content.ReadAsStringAsync()).Should().Contain("sudah ditutup");
            }

            var keterangan = Penanda();
            using (var tembusLagi = await CatatBebanAsync(http, keterangan, 1_000_000, Tanggal(mulai.AddDays(4))))
                tembusLagi.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using (var jurnal = JsonDocument.Parse(await http.GetStringAsync("/api/journal?limit=1000")))
                jurnal.RootElement.EnumerateArray()
                    .Any(e => e.GetProperty("description").GetString() == "Biaya: " + keterangan)
                    .Should().BeFalse("tidak ada jurnal yang boleh mendarat di periode terkunci");
        }
        finally
        {
            HapusPeriode(mulai);
        }
    }

    private static string BasisDataUji =>
        TestHost.DatabasePath
        ?? throw new InvalidOperationException(
            "Tes periode menyisipkan baris Locked langsung ke basis data sementara; jalankan tanpa BAKERY_TEST_URL.");

    private static void DenganBasisData(Action<SqliteConnection> aksi)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = BasisDataUji,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }
        aksi(conn);
    }

    private static void SisipkanPeriodeTerkunci(DateTime mulai)
    {
        DenganBasisData(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO FiscalPeriods (Name, StartDate, EndDate, Status, ClosedAt, ClosedByUserId)
                VALUES (@nama, @mulai, @akhir, 'Locked', @tutup, NULL);";
            cmd.Parameters.AddWithValue("@nama", "Periode terkunci uji");
            cmd.Parameters.AddWithValue("@mulai", Tanggal(mulai));
            cmd.Parameters.AddWithValue("@akhir", Tanggal(mulai.AddMonths(1).AddDays(-1)));
            cmd.Parameters.AddWithValue("@tutup", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        });
    }

    private static void HapusPeriode(DateTime mulai)
    {
        DenganBasisData(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM FiscalPeriods WHERE StartDate = @mulai;";
            cmd.Parameters.AddWithValue("@mulai", Tanggal(mulai));
            cmd.ExecuteNonQuery();
        });
    }
}
