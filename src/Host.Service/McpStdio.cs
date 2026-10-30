using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Host;

public sealed record McpToolResult(bool IsError, string Text);

/// <summary>
/// Kumpulan tool MCP yang boleh dipanggil agen. Semuanya hanya membaca dan semuanya melewati
/// service aplikasi — tidak ada SQL bebas, akses berkas, atau perubahan transaksi. Identitas
/// agen adalah akun internal yang ditunjuk lewat Mcp:UserId; perannya menentukan tool mana yang
/// boleh dipakai, dan setiap pemanggilan (termasuk penolakan) dicatat ke AuditLog.
/// </summary>
public sealed class McpToolbox
{
    public const string LaporanHarian = "get_daily_report";
    public const string DaftarTugas = "get_task_list";
    public const string RingkasanNotifikasi = "get_notification_summary";
    public const string StatusOperasional = "get_operational_status";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] NamaTool = [LaporanHarian, DaftarTugas, RingkasanNotifikasi, StatusOperasional];

    private readonly IUserRepository _users;
    private readonly AnalyticsService _analytics;
    private readonly TaskInboxService _inbox;
    private readonly NotificationService _notifikasi;
    private readonly ISalesOrderRepository _orders;
    private readonly DatabaseIntegrity _integritas;
    private readonly IAuditLogRepository _audit;

    public McpToolbox(
        IUserRepository users, AnalyticsService analytics, TaskInboxService inbox,
        NotificationService notifikasi, ISalesOrderRepository orders,
        DatabaseIntegrity integritas, IAuditLogRepository audit)
    {
        _users = users;
        _analytics = analytics;
        _inbox = inbox;
        _notifikasi = notifikasi;
        _orders = orders;
        _integritas = integritas;
        _audit = audit;
    }

    public static JsonArray DaftarDefinisiTool()
    {
        JsonObject Skema(params (string Nama, JsonObject Skema)[] properti)
        {
            var objek = new JsonObject { ["type"] = "object", ["additionalProperties"] = false };
            if (properti.Length > 0)
            {
                var props = new JsonObject();
                foreach (var (nama, skema) in properti) props[nama] = skema;
                objek["properties"] = props;
            }
            return objek;
        }

        return
        [
            new JsonObject
            {
                ["name"] = LaporanHarian,
                ["description"] = "Ringkasan laporan harian dari jurnal posted dan pesanan aktual. "
                                  + "Tanggal opsional berformat YYYY-MM-DD; bawaan hari ini.",
                ["inputSchema"] = Skema(("date", new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Tanggal bisnis YYYY-MM-DD (opsional)."
                }))
            },
            new JsonObject
            {
                ["name"] = DaftarTugas,
                ["description"] = "Daftar pekerjaan yang menunggu tindakan, disaring sesuai peran akun agen.",
                ["inputSchema"] = Skema()
            },
            new JsonObject
            {
                ["name"] = RingkasanNotifikasi,
                ["description"] = "Ringkasan notifikasi internal milik akun agen (belum dibaca dan 20 terbaru).",
                ["inputSchema"] = Skema()
            },
            new JsonObject
            {
                ["name"] = StatusOperasional,
                ["description"] = "Status operasional tanpa data sensitif: versi, skema, integritas basis data, "
                                  + "dan jumlah pekerjaan aktif.",
                ["inputSchema"] = Skema()
            }
        ];
    }

    public async Task<McpToolResult> PanggilAsync(string nama, JsonObject? argumen, int? userId)
    {
        if (!NamaTool.Contains(nama))
            return await TolakAsync(userId, nama, "Tool tidak dikenal.");

        if (userId is not { } id)
            return await TolakAsync(null, nama, "MCP belum dikonfigurasi: Mcp:UserId belum diisi.");

        var pengguna = await _users.GetByIdAsync(id);
        if (pengguna is null || !pengguna.IsActive)
            return await TolakAsync(id, nama, "Akun agen MCP tidak ditemukan atau sudah dinonaktifkan.");

        var galatArgumen = ValidasiArgumen(nama, argumen);
        if (galatArgumen is not null)
            return await TolakAsync(pengguna.Id, nama, galatArgumen);

        try
        {
            var hasil = nama switch
            {
                LaporanHarian => await LaporanAsync(pengguna, argumen),
                DaftarTugas => await TugasAsync(pengguna),
                RingkasanNotifikasi => await NotifikasiAsync(pengguna),
                StatusOperasional => await StatusAsync(pengguna),
                _ => throw new InvalidOperationException("Tool tidak dikenal.")
            };

            if (hasil.IsError)
                return await TolakAsync(pengguna.Id, nama, hasil.Text);

            await CatatAuditAsync(pengguna.Id, nama, argumen, "ok", null);
            return hasil;
        }
        catch (Exception ex)
        {
            return await GagalAsync(pengguna.Id, nama, argumen, ex);
        }
    }

    private static string? ValidasiArgumen(string nama, JsonObject? argumen)
    {
        if (argumen is null) return null;

        var kunci = argumen.Select(kv => kv.Key).ToList();
        if (kunci.Count == 0) return null;

        if (nama != LaporanHarian || kunci.Any(k => k != "date"))
            return $"Argumen tidak dikenal: {string.Join(", ", kunci)}.";

        var nilai = argumen["date"];
        if (nilai is null) return null;
        if (nilai is not JsonValue || !nilai.GetValueKind().Equals(JsonValueKind.String))
            return "Argumen 'date' harus berupa teks YYYY-MM-DD.";
        var teks = nilai.GetValue<string>();
        if (!DateTime.TryParseExact(teks, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var tanggal))
            return "Tanggal harus berformat YYYY-MM-DD.";
        if (tanggal.Date > BusinessTime.Today())
            return "Tanggal laporan tidak boleh melewati hari ini.";
        if (tanggal.Date < BusinessTime.Today().AddDays(-366))
            return "Tanggal laporan paling lama 366 hari ke belakang.";
        return null;
    }

    private async Task<McpToolResult> LaporanAsync(User pengguna, JsonObject? argumen)
    {
        if (!RolePolicies.Allows(RolePolicies.FinanceRead, pengguna.Role))
            return new McpToolResult(true,
                "Akun agen tidak berwenang membaca laporan keuangan (butuh peran Owner/Admin).");

        var tanggal = BusinessTime.Today();
        if (argumen?["date"] is { } node)
            tanggal = DateTime.ParseExact(node.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var laporan = await _analytics.GetDailyReportAsync(tanggal);
        return Sukses(new
        {
            tanggal = laporan.Tanggal.ToString("yyyy-MM-dd"),
            pendapatanMinor = laporan.Pendapatan,
            pendapatan = Money.Format(laporan.Pendapatan),
            bebanMinor = laporan.Beban,
            beban = Money.Format(laporan.Beban),
            selisihPendapatanBebanMinor = laporan.LabaKotor,
            selisihPendapatanBeban = Money.Format(laporan.LabaKotor),
            nilaiPesananMinor = laporan.NilaiPesanan,
            nilaiPesanan = Money.Format(laporan.NilaiPesanan),
            jumlahPesanan = laporan.JumlahPesanan,
            jumlahPesananBatal = laporan.JumlahPesananBatal,
            unitTerjual = laporan.UnitTerjual,
            rataRataNilaiPesananMinor = laporan.RataRataNilaiPesanan,
            catatan = laporan.Tanggal == BusinessTime.Today()
                ? "Hari berjalan; angka bisa belum final sampai transaksi hari ini selesai."
                : null
        });
    }

    private async Task<McpToolResult> TugasAsync(User pengguna) =>
        Sukses(await _inbox.GetAsync(pengguna.Role));

    private async Task<McpToolResult> NotifikasiAsync(User pengguna)
    {
        var daftar = await _notifikasi.DaftarAsync(pengguna.Id, unreadOnly: false, limit: 20);
        return Sukses(new
        {
            ringkasan = daftar.Ringkasan,
            terbaru = daftar.Item.Select(n => new
            {
                id = n.Id,
                eventType = n.EventType,
                severity = n.Severity.ToString(),
                judul = n.Title,
                isi = n.Body,
                dibaca = n.IsRead,
                dibuat = n.CreatedAt
            })
        });
    }

    private async Task<McpToolResult> StatusAsync(User pengguna)
    {
        var cek = _integritas.Check();
        var penggunaAktif = (await _users.GetAllAsync()).Count(u => u.IsActive);
        var pesananBelumLunas = (await _orders.GetOutstandingSummaryAsync()).Jumlah;
        var tugas = await _inbox.GetAsync(pengguna.Role);

        return Sukses(new
        {
            aplikasi = "Seruni 22 Bakery",
            versi = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            skema = cek.FirstOrDefault(c => c.Name == "versi skema") is { } versi
                ? new { lolos = versi.Passed, detail = versi.Detail }
                : null,
            basisData = new
            {
                lolos = cek.All(c => c.Passed),
                gagal = cek.Where(c => !c.Passed).Select(c => new { nama = c.Name, detail = c.Detail })
            },
            operasional = new
            {
                penggunaAktif,
                pesananBelumLunas,
                tugasMenunggu = tugas.Jumlah
            }
        });
    }

    private static McpToolResult Sukses(object isi) =>
        new(false, JsonSerializer.Serialize(isi, Json));

    private async Task<McpToolResult> TolakAsync(int? userId, string nama, string alasan)
    {
        try
        {
            await CatatAuditAsync(userId, nama, null, "ditolak", alasan);
        }
        catch
        {
            // Penolakan tetap disampaikan walau jejaknya gagal ditulis.
        }
        return new McpToolResult(true, alasan);
    }

    private async Task<McpToolResult> GagalAsync(int userId, string nama, JsonObject? argumen, Exception ex)
    {
        try
        {
            await CatatAuditAsync(userId, nama, argumen, "galat", ex.Message);
        }
        catch
        {
            // Pesan galat asli tetap dikembalikan; kegagalan audit ikut dilaporkan di bawah.
        }
        return new McpToolResult(true, $"Tool gagal dijalankan: {ex.Message}");
    }

    private async Task CatatAuditAsync(int? userId, string nama, JsonObject? argumen, string status, string? alasan)
    {
        var detail = JsonSerializer.Serialize(new
        {
            tool = nama,
            args = argumen,
            hasil = status,
            alasan
        }, Json);
        await _audit.LogAsync(new AuditLog
        {
            UserId = userId,
            Action = $"MCP {nama}",
            EntityType = "McpTool",
            EntityId = null,
            NewValues = detail.Length <= 500 ? detail : detail[..500],
            OccurredAt = DateTime.UtcNow,
            SessionId = "mcp-stdio"
        });
    }
}

/// <summary>
/// Transport MCP stdio: satu pesan JSON-RPC per baris di stdin, jawaban satu baris di stdout.
/// stdout dipakai khusus protokol; log Serilog diarahkan ke berkas saat mode ini aktif.
/// </summary>
public static class McpStdio
{
    private const string VersiProtokol = "2024-11-05";

    public static async Task<int> JalankanAsync(
        IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var toolbox = scope.ServiceProvider.GetRequiredService<McpToolbox>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("McpStdio");

        var userId = int.TryParse(configuration["Mcp:UserId"], out var id) ? id : (int?)null;
        if (userId is null)
            log.LogWarning("MCP berjalan tanpa Mcp:UserId; semua pemanggilan tool akan ditolak.");

        string? baris;
        while ((baris = await Console.In.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(baris)) continue;

            JsonNode? pesan;
            try
            {
                pesan = JsonNode.Parse(baris);
            }
            catch (JsonException)
            {
                await TulisAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = null,
                    ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "JSON tidak valid." }
                });
                continue;
            }

            if (pesan is not JsonObject permintaan) continue;
            var metode = permintaan["method"]?.GetValue<string>();
            var idPermintaan = permintaan["id"]?.DeepClone();
            var parameter = permintaan["params"] as JsonObject;

            // Notifikasi JSON-RPC (tanpa id) tidak dibalas.
            if (idPermintaan is null && metode != "ping") continue;

            try
            {
                var jawaban = metode switch
                {
                    "initialize" => Initialize(parameter),
                    "ping" => new JsonObject(),
                    "tools/list" => new JsonObject { ["tools"] = McpToolbox.DaftarDefinisiTool() },
                    "tools/call" => await PanggilToolAsync(toolbox, parameter, userId),
                    _ => null
                };

                if (jawaban is null)
                {
                    await TulisAsync(Galat(idPermintaan, -32601, $"Metode tidak dikenal: {metode}"));
                    continue;
                }

                await TulisAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = idPermintaan,
                    ["result"] = jawaban
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Permintaan MCP {Metode} gagal.", metode);
                await TulisAsync(Galat(idPermintaan, -32603, "Galat internal server MCP."));
            }
        }

        return 0;
    }

    private static JsonObject Initialize(JsonObject? parameter)
    {
        var diminta = parameter?["protocolVersion"]?.GetValue<string>();
        var versi = diminta is "2024-11-05" or "2025-03-26" or "2025-06-18" ? diminta : VersiProtokol;
        return new JsonObject
        {
            ["protocolVersion"] = versi,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "seruni22-bakery",
                ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"
            }
        };
    }

    private static async Task<JsonObject> PanggilToolAsync(McpToolbox toolbox, JsonObject? parameter, int? userId)
    {
        var nama = parameter?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(nama))
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text", ["text"] = "Parameter 'name' wajib diisi."
                }),
                ["isError"] = true
            };

        var argumen = parameter?["arguments"] as JsonObject;
        var hasil = await toolbox.PanggilAsync(nama, argumen, userId);
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text", ["text"] = hasil.Text
            }),
            ["isError"] = hasil.IsError
        };
    }

    private static JsonObject Galat(JsonNode? id, int kode, string pesan) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = kode, ["message"] = pesan }
    };

    private static async Task TulisAsync(JsonObject pesan)
    {
        await Console.Out.WriteLineAsync(pesan.ToJsonString());
        await Console.Out.FlushAsync();
    }
}
