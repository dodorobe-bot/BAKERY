using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// MCP lokal diuji lewat transport yang sebenarnya: proses host kedua berbicara JSON-RPC di
/// stdin/stdout pada basis data yang sama dengan host UI. Yang dibuktikan adalah daftar tool,
/// validasi argumen, penolakan peran, jejak audit, dan kemampuan membaca data yang ditulis
/// proses lain — bukan sekadar bahwa kelasnya bisa dikompilasi.
/// </summary>
[Collection("UI Tests")]
public sealed class McpStdioTests : IClassFixture<PlaywrightFixture>
{
    private static readonly string[] ToolYangDiharapkan =
    [
        "get_daily_report", "get_task_list", "get_notification_summary", "get_operational_status"
    ];

    private readonly PlaywrightFixture _fixture;

    public McpStdioTests(PlaywrightFixture fixture) => _fixture = fixture;

    private static string DatabasePath =>
        TestHost.DatabasePath
        ?? throw new InvalidOperationException(
            "Tes MCP membutuhkan basis data sementara milik TestHost; jalankan tanpa BAKERY_TEST_URL.");

    private async Task<(HttpClient Owner, int OwnerId)> OwnerAsync()
    {
        var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);
        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/users"));
        var id = dok.RootElement.EnumerateArray()
            .Single(u => u.GetProperty("username").GetString() == AppLogin.Username)
            .GetProperty("id").GetInt32();
        return (owner, id);
    }

    private async Task<int> BuatPegawaiAsync(HttpClient owner)
    {
        var username = $"mcpug{Guid.NewGuid():N}"[..16];
        using var buat = await owner.PostAsJsonAsync("/api/users",
            new { username, password = "SandiSementara1", role = "Pegawai" });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Inisialisasi_ToolsList_DanPemanggilanSah()
    {
        var (_, ownerId) = await OwnerAsync();
        using var mcp = new McpClient(DatabasePath, ownerId);

        using var init = await mcp.InisialisasiAsync();
        var server = init.RootElement.GetProperty("result");
        server.GetProperty("protocolVersion").GetString().Should().Be("2024-11-05");
        server.GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("seruni22-bakery");

        using var daftar = await mcp.KirimAsync("tools/list", new { });
        var tools = daftar.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo(ToolYangDiharapkan);

        var laporan = tools.Single(t => t.GetProperty("name").GetString() == "get_daily_report");
        laporan.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean()
            .Should().BeFalse("tool hanya menerima argumen yang dideklarasikan");
        laporan.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("date", out _)
            .Should().BeTrue();

        using var status = await mcp.PanggilToolAsync("get_operational_status");
        McpClient.Gagal(status).Should().BeFalse(McpClient.Teks(status));
        using var isiStatus = JsonDocument.Parse(McpClient.Teks(status));
        isiStatus.RootElement.GetProperty("aplikasi").GetString().Should().Be("Seruni 22 Bakery");
        isiStatus.RootElement.GetProperty("versi").GetString().Should().NotBeNullOrWhiteSpace();
        isiStatus.RootElement.GetProperty("basisData").GetProperty("lolos").GetBoolean().Should().BeTrue();

        using var tugas = await mcp.PanggilToolAsync("get_task_list");
        McpClient.Gagal(tugas).Should().BeFalse(McpClient.Teks(tugas));
        using var isiTugas = JsonDocument.Parse(McpClient.Teks(tugas));
        isiTugas.RootElement.TryGetProperty("item", out _).Should().BeTrue();
        isiTugas.RootElement.TryGetProperty("jumlah", out _).Should().BeTrue();

        using var harian = await mcp.PanggilToolAsync("get_daily_report");
        McpClient.Gagal(harian).Should().BeFalse(McpClient.Teks(harian));
        using var isiHarian = JsonDocument.Parse(McpClient.Teks(harian));
        isiHarian.RootElement.GetProperty("tanggal").GetString()
            .Should().Be(Seruni22Bakery.Domain.Common.BusinessTime.Today().ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Penolakan_ToolAsingArgumenSalahDanMetodeTakDikenal()
    {
        var (_, ownerId) = await OwnerAsync();
        using var mcp = new McpClient(DatabasePath, ownerId);
        await mcp.InisialisasiAsync();

        using var asing = await mcp.PanggilToolAsync("hapus_semua_data");
        McpClient.Gagal(asing).Should().BeTrue();
        McpClient.Teks(asing).Should().Contain("tidak dikenal");

        using var masaDepan = await mcp.PanggilToolAsync("get_daily_report", new { date = "2099-12-31" });
        McpClient.Gagal(masaDepan).Should().BeTrue();
        McpClient.Teks(masaDepan).Should().Contain("tidak boleh melewati");

        using var formatSalah = await mcp.PanggilToolAsync("get_daily_report", new { date = "31-12-2026" });
        McpClient.Gagal(formatSalah).Should().BeTrue("tanggal harus YYYY-MM-DD");

        using var argumenAsing = await mcp.PanggilToolAsync(
            "get_daily_report", new { date = "2026-10-01", sql = "SELECT * FROM Users" });
        McpClient.Gagal(argumenAsing).Should().BeTrue();
        McpClient.Teks(argumenAsing).Should().Contain("Argumen tidak dikenal");

        using var metodeAsing = await mcp.KirimAsync("resources/list", new { });
        metodeAsing.RootElement.GetProperty("error").GetProperty("code").GetInt32()
            .Should().Be(-32601);
    }

    [Fact]
    public async Task TanpaAkunAgen_SemuaToolDitolak()
    {
        using var mcp = new McpClient(DatabasePath, userId: null);
        await mcp.InisialisasiAsync();

        using var tugas = await mcp.PanggilToolAsync("get_task_list");

        McpClient.Gagal(tugas).Should().BeTrue();
        McpClient.Teks(tugas).Should().Contain("Mcp:UserId");
    }

    [Fact]
    public async Task Pegawai_DitolakLaporanHarian_TetapiTugasTetapBoleh_DanSemuaDicatatAudit()
    {
        var (owner, _) = await OwnerAsync();
        var pegawaiId = await BuatPegawaiAsync(owner);
        using var mcp = new McpClient(DatabasePath, pegawaiId);
        await mcp.InisialisasiAsync();

        using var harian = await mcp.PanggilToolAsync("get_daily_report");
        McpClient.Gagal(harian).Should().BeTrue("laporan keuangan butuh peran Owner/Admin");
        McpClient.Teks(harian).Should().Contain("tidak berwenang");

        using var tugas = await mcp.PanggilToolAsync("get_task_list");
        McpClient.Gagal(tugas).Should().BeFalse(McpClient.Teks(tugas));

        using var audit = JsonDocument.Parse(
            await owner.GetStringAsync("/api/audit?entity=McpTool&limit=100"));
        var catatan = audit.RootElement.EnumerateArray()
            .Where(a => a.GetProperty("entityType").GetString() == "McpTool")
            .ToList();
        catatan.Should().Contain(a => a.GetProperty("action").GetString() == "MCP get_daily_report" &&
                                      a.GetProperty("newValues").GetString()!.Contains("ditolak"));
        catatan.Should().Contain(a => a.GetProperty("action").GetString() == "MCP get_task_list" &&
                                      a.GetProperty("newValues").GetString()!.Contains("\"ok\""));
    }

    [Fact]
    public async Task NotifikasiDariTransaksiHost_TerbacaDariProsesMcp()
    {
        var (owner, ownerId) = await OwnerAsync();
        using var buat = await owner.PostAsJsonAsync("/api/orders", new
        {
            customerName = $"Pelanggan MCP {Guid.NewGuid():N}"[..32],
            description = "Uji notifikasi lintas proses",
            pickupDate = "2030-02-10T10:00:00",
            totalAmount = 5_000_000,
            downPayment = 0,
            quantity = 1,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        var pesananId = dok.RootElement.GetProperty("id").GetInt32();

        using var mcp = new McpClient(DatabasePath, ownerId);
        await mcp.InisialisasiAsync();
        using var ringkas = await mcp.PanggilToolAsync("get_notification_summary");
        McpClient.Gagal(ringkas).Should().BeFalse(McpClient.Teks(ringkas));

        using var isi = JsonDocument.Parse(McpClient.Teks(ringkas));
        isi.RootElement.GetProperty("ringkasan").GetProperty("total").GetInt32()
            .Should().BeGreaterThan(0, "baris ditulis host HTTP lalu dibaca proses lain");
        isi.RootElement.GetProperty("terbaru").EnumerateArray().Should()
            .Contain(n => n.GetProperty("eventType").GetString() == "transaction.order.created" &&
                          n.GetProperty("judul").GetString()!.Contains("Pesanan") &&
                          n.GetProperty("severity").GetString() == "Info");
    }
}
