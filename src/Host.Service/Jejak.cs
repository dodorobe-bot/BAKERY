using System.Text.RegularExpressions;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

/// <summary>
/// Jejak audit untuk setiap aksi tulis yang berhasil. Dicatat di satu tempat (middleware) supaya
/// aksi yang ditambahkan nanti tidak bisa lupa dicatat; nilai sebelum/sesudah yang butuh baca
/// data lama ditulis sendiri oleh endpoint lewat <see cref="CatatDetilAsync"/>, dan middleware
/// tidak mengulang yang sudah tercatat.
///
/// Yang masuk: POST/PUT/PATCH/DELETE di /api yang menjawab 2xx. Yang tidak masuk: permintaan
/// gagal (tidak ada perubahan data yang terjadi) dan ulang-ulang kunci idempotensi (niatnya sama,
/// sudah tercatat oleh permintaan pertama).
/// </summary>
public static class Jejak
{
    /// <summary>Penanda bahwa endpoint sudah menulis entri lengkap dengan nilai sebelum/sesudah.</summary>
    public const string SudahDicatat = "jejak.sudahDicatat";

    /// <summary>Penanda dari Idempotensi: permintaan ini hanya mengulang hasil yang sudah ada.</summary>
    public const string Duplikat = "jejak.duplikat";

    private static readonly (string Awalan, string Entitas)[] Peta =
    [
        ("/api/production/quality-checks", "QualityCheck"),
        ("/api/production/orders", "WorkOrder"),
        ("/api/returns", "SalesReturn"),
        ("/api/inventory/batches", "ItemBatch"),
        ("/api/inventory", "LocationStock"),
        ("/api/purchases", "PurchaseOrder"),
        ("/api/orders", "SalesOrder"),
        ("/api/expenses", "JournalEntry"),
        ("/api/journal", "JournalEntry"),
        ("/api/ingredients", "Ingredient"),
        ("/api/products", "ProductTemplate"),
        ("/api/customers", "Customer"),
        ("/api/suppliers", "Supplier"),
        ("/api/settings", "AppSetting"),
        ("/api/devices", "AppSetting"),
        ("/api/users", "User"),
        ("/api/auth/change-password", "User"),
    ];

    private static readonly Regex AngkaDiPath = new(@"/(\d+)(?:/|$)", RegexOptions.Compiled);

    public static async Task TengahkanAsync(HttpContext ctx, RequestDelegate lanjut)
    {
        var metode = ctx.Request.Method;
        var path = ctx.Request.Path;
        // Login tidak diawasi: percobaan sandi salah sudah dihitung AuthService, dan mencatatnya
        // di sini akan menggandakan hitungan plus menyimpan nama pengguna tebakan.
        if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/auth/login"))
        {
            await lanjut(ctx);
            return;
        }

        await lanjut(ctx);

        var status = ctx.Response.StatusCode;

        // Penolakan otorisasi (401/403) dicatat justru karena tidak ada data yang berubah:
        // inilah bukti pagar peran bekerja sekaligus siapa yang mencoba menembusnya. Permintaan
        // yang ditolak karena kata sandi bawaan juga termasuk, karena isinya tidak tersentuh.
        if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            await CatatAsync(ctx, $"DITOLAK {metode} {path}", "Akses", null, null, null);
            return;
        }

        if (metode is not ("POST" or "PUT" or "PATCH" or "DELETE")) return;
        if (ctx.Items.ContainsKey(SudahDicatat) || ctx.Items.ContainsKey(Duplikat)) return;
        if (status is < 200 or >= 300) return;

        var (awalan, entitas) = Peta.FirstOrDefault(p => path.StartsWithSegments(p.Awalan));
        await CatatAsync(ctx, $"{metode} {path}", entitas ?? "Lainnya",
            IdDariPath(path) ?? IdDariLokasi(ctx), null, null);
    }

    private static async Task CatatAsync(
        HttpContext ctx, string action, string entityType, int? entityId,
        string? lama, string? baru)
    {
        var repo = ctx.RequestServices.GetService(typeof(IAuditLogRepository)) as IAuditLogRepository;
        if (repo is null) return;

        await repo.LogAsync(new AuditLog
        {
            UserId = Pelaku(ctx),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = lama,
            NewValues = baru,
            OccurredAt = DateTime.UtcNow,
            SessionId = ctx.TraceIdentifier,
        });
    }

    /// <summary>
    /// Ditulis oleh endpoint yang tahu nilai lama dan baru (harga, status, stok, pengaturan,
    /// sandi). Isinya tidak dipakai untuk kolom uang, jadi cukup ringkas dan terbaca manusia.
    /// </summary>
    public static async Task CatatDetilAsync(
        HttpContext ctx, string action, string entityType, int? entityId,
        object? lama, object? baru)
    {
        var repo = ctx.RequestServices.GetService(typeof(IAuditLogRepository)) as IAuditLogRepository;
        if (repo is null) return;

        await repo.LogAsync(new AuditLog
        {
            UserId = Pelaku(ctx),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = Ringkas(lama),
            NewValues = Ringkas(baru),
            OccurredAt = DateTime.UtcNow,
            SessionId = ctx.TraceIdentifier,
        });
        ctx.Items[SudahDicatat] = true;
    }

    private static int? Pelaku(HttpContext ctx) =>
        int.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private static int? IdDariPath(PathString path)
    {
        var cocok = AngkaDiPath.Match(path.Value ?? string.Empty);
        return cocok.Success && int.TryParse(cocok.Groups[1].Value, out var id) ? id : null;
    }

    private static int? IdDariLokasi(HttpContext ctx)
    {
        var lokasi = ctx.Response.Headers.Location.ToString();
        if (lokasi.Length == 0) return null;
        var akhir = lokasi.LastIndexOf('/');
        return akhir >= 0 && int.TryParse(lokasi[(akhir + 1)..], out var id) ? id : null;
    }

    /// <summary>
    /// Kolom OldValues/NewValues berisi teks, jadi nilai objek diubah jadi JSON pendek.
    /// Nilai yang lebih panjang dari ini dipotong: jejak audit tidak boleh membengkakkan
    /// basis data, dan isi penuh sudah ada di tabel dokumennya masing-masing.
    /// </summary>
    private static string? Ringkas(object? nilai)
    {
        if (nilai is null) return null;
        var teks = nilai is string s ? s : System.Text.Json.JsonSerializer.Serialize(nilai);
        return teks.Length <= 500 ? teks : teks[..500] + "…";
    }
}
