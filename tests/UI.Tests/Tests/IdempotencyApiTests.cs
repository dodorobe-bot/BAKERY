using System.Net;
using System.Text;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Kunci idempotensi diuji lewat HTTP sungguhan, bukan lewat kelasnya sendiri: yang harus
/// dibuktikan adalah bahwa dua tekanan pada tombol "Simpan" hanya menghasilkan satu pesanan di
/// basis data, dan bahwa permintaan yang ditolak tidak memakan kunci milik pengguna.
/// </summary>
[Collection("UI Tests")]
public sealed class IdempotencyApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public IdempotencyApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> HttpAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
    private static async Task<(HttpStatusCode Kode, JsonDocument Body)> KirimAsync(
        HttpClient http, string rute, object isi, string? kunci)
    {
        var permintaan = new HttpRequestMessage(HttpMethod.Post, rute)
        {
            Content = new StringContent(JsonSerializer.Serialize(isi), Encoding.UTF8, "application/json")
        };
        if (kunci is not null) permintaan.Headers.TryAddWithoutValidation("Idempotency-Key", kunci);

        var respons = await http.SendAsync(permintaan);
        var teks = await respons.Content.ReadAsStringAsync();
        return (respons.StatusCode, JsonDocument.Parse(teks == "" ? "null" : teks));
    }

    private static object PesananBaru(string nama) => new
    {
        customerName = nama,
        description = "Uji kunci idempotensi",
        pickupDate = "2030-03-03T10:00:00",
        totalAmount = 2_000_000,
        downPayment = 500_000,
        quantity = 1,
    };

    [Fact]
    public async Task KunciSama_DuaKaliHanyaSatuPesananDanKlikKeduaMendapatJawaban()
    {
        var http = await HttpAsync();
        var nama = $"Kunci Uji {Guid.NewGuid():N}"[..28];
        var kunci = $"orders-{Guid.NewGuid():N}";

        var pertama = await KirimAsync(http, "/api/orders", PesananBaru(nama), kunci);
        pertama.Kode.Should().Be(HttpStatusCode.Created, $"pesanan pertama harus tersimpan: {pertama.Body.RootElement}");
        var idPertama = pertama.Body.RootElement.GetProperty("id").GetInt32();

        var kedua = await KirimAsync(http, "/api/orders", PesananBaru(nama), kunci);
        kedua.Kode.Should().Be(HttpStatusCode.OK, "permintaan ulang dengan kunci yang sama bukan kesalahan, hanya pengulangan");
        kedua.Body.RootElement.GetProperty("duplikat").GetBoolean().Should().BeTrue();
        kedua.Body.RootElement.GetProperty("id").GetInt32().Should().Be(idPertama);

        var daftar = JsonDocument.Parse(await http.GetStringAsync("/api/orders"));
        var jumlah = daftar.RootElement.EnumerateArray()
            .Count(o => o.GetProperty("customerNameSnapshot").GetString() == nama);
        jumlah.Should().Be(1, "kunci yang sama tidak boleh menambah pesanan kedua");
    }

    [Fact]
    public async Task PermintaanDitolak_KunciDilepasPemilikBolehMenyimpanLagi()
    {
        var http = await HttpAsync();
        var nama = $"Kunci Gagal {Guid.NewGuid():N}"[..28];
        var kunci = $"gagal-{Guid.NewGuid():N}";

        // Nominal nol ditolak validasi sisi server.
        var ditolak = await KirimAsync(http, "/api/orders",
            new { customerName = nama, description = "x", pickupDate = "2030-03-03T10:00:00", totalAmount = 0, downPayment = 0, quantity = 1 }, kunci);
        ditolak.Kode.Should().Be(HttpStatusCode.BadRequest);

        var berhasil = await KirimAsync(http, "/api/orders", PesananBaru(nama), kunci);
        berhasil.Kode.Should().Be(HttpStatusCode.Created,
            $"satu galat validasi tidak boleh membuat kunci mati: {berhasil.Body.RootElement}");
    }

    [Fact]
    public async Task KunciSama_RuteBerbedaDitolakBukanDijalankan()
    {
        var http = await HttpAsync();
        var nama = $"Kunci Lintas {Guid.NewGuid():N}"[..28];
        var kunci = $"lintas-{Guid.NewGuid():N}";

        (await KirimAsync(http, "/api/orders", PesananBaru(nama), kunci)).Kode
            .Should().Be(HttpStatusCode.Created);

        var tabrakan = await KirimAsync(http, "/api/expenses",
            new { category = "Operasional", description = "Bensar", amount = 50_000 }, kunci);
        tabrakan.Kode.Should().Be(HttpStatusCode.Conflict,
            "kunci yang sudah dipakai pesanan tidak boleh mengerjakan aksi lain");
        tabrakan.Body.RootElement.GetProperty("errorCode").GetString().Should().Be("IDEMPOTENCY_CONFLICT");

        var jurnal = JsonDocument.Parse(await http.GetStringAsync("/api/journal?limit=50"));
        jurnal.RootElement.EnumerateArray()
            .Any(j => (j.GetProperty("description").GetString() ?? "").Contains("Bensar"))
            .Should().BeFalse("permintaan yang ditolak tidak boleh meninggalkan jurnal");
    }

    [Fact]
    public async Task KunciTidakSah_DitolakSebelumMenyentuhBasisData()
    {
        var http = await HttpAsync();
        var nama = $"Kunci Buruk {Guid.NewGuid():N}"[..28];

        var buruk = await KirimAsync(http, "/api/orders", PesananBaru(nama), "kunci dengan spasi");
        buruk.Kode.Should().Be(HttpStatusCode.BadRequest);

        var daftar = JsonDocument.Parse(await http.GetStringAsync("/api/orders"));
        daftar.RootElement.EnumerateArray()
            .Any(o => o.GetProperty("customerNameSnapshot").GetString() == nama)
            .Should().BeFalse();
    }

    [Fact]
    public async Task TanpaKunci_PerilakuLamaTetapJalan()
    {
        var http = await HttpAsync();
        var nama = $"Tanpa Kunci {Guid.NewGuid():N}"[..28];

        (await KirimAsync(http, "/api/orders", PesananBaru(nama), null)).Kode
            .Should().Be(HttpStatusCode.Created);
        (await KirimAsync(http, "/api/orders", PesananBaru(nama), null)).Kode
            .Should().Be(HttpStatusCode.Created, "klien yang tidak mengirim kunci tidak boleh tiba-tiba dapat 409");
    }

    [Fact]
    public async Task EndpointYangTidakDikenal_MenjawabJsonBukanHtmlAplikasi()
    {
        var http = await HttpAsync();

        var respons = await http.GetAsync("/api/tyo-masukan-salah");
        respons.StatusCode.Should().Be(HttpStatusCode.NotFound);
        respons.Content.Headers.ContentType!.MediaType.Should().Be("application/json",
            "fallback SPA pernah menjawab 200 + HTML, sehingga layar hanya terlihat kosong dan bugnya hilang tanpa jejak");

        var teks = await respons.Content.ReadAsStringAsync();
        JsonDocument.Parse(teks).RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task PeriksaDiriBasisData_MelaporkanSemuaPemeriksaanUntukOwner()
    {
        var http = await HttpAsync();

        var respons = await http.GetAsync("/api/system/self-check");
        respons.StatusCode.Should().Be(HttpStatusCode.OK);

        using var laporan = JsonDocument.Parse(await respons.Content.ReadAsStringAsync());
        var akar = laporan.RootElement;
        akar.GetProperty("status").GetString().Should().Be("ok",
            $"basis data hasil migrasi tidak boleh dilaporkan bermasalah: {akar}");
        akar.GetProperty("checks").GetArrayLength().Should().BeGreaterThanOrEqualTo(9);
        akar.GetProperty("jumlahGagal").GetInt32().Should().Be(0);

        foreach (var cek in akar.GetProperty("checks").EnumerateArray())
        {
            cek.GetProperty("passed").GetBoolean().Should().BeTrue(cek.GetProperty("name").GetString());
            cek.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task PeriksaDiriButuhLogin()
    {
        var baseUrl = _fixture.BaseUrl;
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var anonim = await http.GetAsync("/api/system/self-check");
        anonim.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "struktur basis data dan lokasinya bukan informasi yang bebas dibaca");
    }
}
