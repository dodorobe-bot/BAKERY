using System.Globalization;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <summary>Isi satu notifikasi internal sebelum dikirim ke penerima.</summary>
public sealed record KirimNotifikasi(
    string EventType,
    NotificationSeverity Severity,
    string Title,
    string Body,
    string? EntityType = null,
    int? EntityId = null,
    string? DedupeKey = null,
    string? MetadataJson = null);

public sealed record DaftarNotifikasi(
    IReadOnlyList<Notification> Item, NotificationSummary Ringkasan);

/// <summary>
/// Notifikasi internal: disimpan di SQLite untuk satu pengguna, dibaca ulang oleh layar
/// Pemberitahuan, dan tidak pernah dikirim ke layanan luar. Penulisan notifikasi tidak boleh
/// menggagalkan transaksi yang sudah tersimpan — pemanggil endpoint menangkap galatnya dan
/// mencatatnya ke log.
/// </summary>
public class NotificationService
{
    private readonly INotificationRepository _notifikasi;
    private readonly IUserRepository _users;
    private readonly IIngredientRepository _ingredients;
    private readonly IBatchRepository _batches;
    private readonly ISalesOrderRepository _orders;
    private readonly AnalyticsService _analytics;
    private readonly TaskInboxService _inbox;

    public NotificationService(
        INotificationRepository notifikasi, IUserRepository users,
        IIngredientRepository ingredients, IBatchRepository batches,
        ISalesOrderRepository orders, AnalyticsService analytics,
        TaskInboxService inbox)
    {
        _notifikasi = notifikasi;
        _users = users;
        _ingredients = ingredients;
        _batches = batches;
        _orders = orders;
        _analytics = analytics;
        _inbox = inbox;
    }

    public async Task<DaftarNotifikasi> DaftarAsync(int userId, bool unreadOnly, int limit)
    {
        var item = await _notifikasi.ListAsync(userId, unreadOnly, Math.Clamp(limit, 1, 200));
        var ringkasan = await _notifikasi.SummaryAsync(userId);
        return new DaftarNotifikasi(item, ringkasan);
    }

    public Task<bool> TandaiDibacaAsync(int id, int userId) =>
        _notifikasi.MarkReadAsync(id, userId, DateTime.UtcNow);

    public Task<int> TandaiSemuaDibacaAsync(int userId) =>
        _notifikasi.MarkAllReadAsync(userId, DateTime.UtcNow);

    public Task<int> KirimKePenggunaAsync(int userId, KirimNotifikasi isi, DateTime nowUtc) =>
        _notifikasi.CreateAsync(Bangun(userId, isi, nowUtc));

    /// <summary>Mengirim ke semua akun aktif yang perannya termasuk daftar ini.</summary>
    public async Task<int> KirimKePeranAsync(
        IReadOnlyCollection<UserRole> roles, KirimNotifikasi isi, DateTime nowUtc)
    {
        var penerima = (await _users.GetAllAsync())
            .Where(u => u.IsActive && roles.Contains(u.Role))
            .ToList();
        var terkirim = 0;
        foreach (var user in penerima)
            if (await _notifikasi.CreateAsync(Bangun(user.Id, isi, nowUtc)) > 0)
                terkirim++;
        return terkirim;
    }

    /// <summary>
    /// Menyusun pengingat dari kondisi basis data saat ini. Semua kunci memuat tanggal bisnis,
    /// sehingga satu kondisi hanya menghasilkan satu pengingat per hari walau penyegaran
    /// berjalan berkali-kali. Kembaliannya jumlah baris baru yang benar-benar ditulis.
    /// </summary>
    public async Task<int> SinkronkanOperasionalAsync(DateTime nowUtc)
    {
        var hari = BusinessTime.Today();
        var kunciHari = hari.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ditulis = 0;

        foreach (var bahan in await _ingredients.GetBelowMinStockAsync())
            ditulis += await KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
                new KirimNotifikasi(
                    "stock.low", NotificationSeverity.Warning,
                    $"Stok menipis: {bahan.Name}",
                    $"Sisa {bahan.CurrentStock:0.###} {bahan.Unit}, batas minimum {bahan.MinStockAlert:0.###} {bahan.Unit}.",
                    "Ingredient", bahan.Id, $"stock.low:{bahan.Id}:{kunciHari}"), nowUtc);

        foreach (var status in new[] { "segera", "kedaluwarsa" })
            foreach (var lot in await _batches.GetAsync(status, 200))
                ditulis += await KirimKePeranAsync(
                    [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
                    new KirimNotifikasi(
                        "batch.expiry",
                        status == "kedaluwarsa" ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                        $"{(status == "kedaluwarsa" ? "Batch kedaluwarsa" : "Batch hampir kedaluwarsa")}: {lot.BatchNumber}",
                        $"{lot.IngredientName} ({lot.Unit}) kedaluwarsa {lot.ExpiryDate:yyyy-MM-dd}, "
                        + $"sisa {lot.DaysToExpiry} hari.",
                        "ItemBatch", lot.Id, $"batch.expiry:{lot.Id}:{kunciHari}"), nowUtc);

        // Dibatasi 200 pesanan paling lama menunggu: pengingat per pesanan tidak boleh tumbuh
        // mengikuti seluruh riwayat, dan daftar lengkapnya sudah tersedia di kotak tugas.
        foreach (var pesanan in await _orders.GetOutstandingOverdueAsync(hari, 200))
            ditulis += await KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin, UserRole.Pegawai],
                new KirimNotifikasi(
                    "task.pickup.overdue", NotificationSeverity.Warning,
                    $"Pesanan lewat jadwal ambil: {pesanan.OrderNumber ?? $"#{pesanan.Id}"}",
                    $"{pesanan.CustomerNameSnapshot} · jadwal {pesanan.PickupDate:yyyy-MM-dd} · sisa {Money.Format(pesanan.RemainingAmount())}.",
                    "SalesOrder", pesanan.Id, $"task.pickup:{pesanan.Id}:{kunciHari}"), nowUtc);

        // Kotak tugas sudah menghitung pekerjaan per peran; pengingatnya cukup satu per peran
        // per hari dan menunjuk ke layar tempat pekerjaannya bisa dibereskan.
        foreach (var peran in new[] { UserRole.Owner, UserRole.Admin, UserRole.Dapur, UserRole.Pegawai })
        {
            var kotak = await _inbox.GetAsync(peran);
            if (kotak.Jumlah == 0) continue;
            var contoh = string.Join("; ", kotak.Item.Take(3).Select(i => i.Judul));
            ditulis += await KirimKePeranAsync(
                [peran],
                new KirimNotifikasi(
                    "task.inbox", NotificationSeverity.Warning,
                    $"Pengingat tugas: {kotak.Jumlah} pekerjaan menunggu",
                    $"Contoh: {contoh}. Buka Kotak Tugas untuk daftar lengkap.",
                    DedupeKey: $"task.inbox:{peran}:{kunciHari}"), nowUtc);
        }

        // Ringkasan harian disusun setelah hari berakhir, supaya angkanya lengkap dan bukan
        // potret pagi yang menyesatkan. Hari tanpa catatan apa pun tidak dibuatkan ringkasan.
        var kemarin = hari.AddDays(-1);
        var laporan = await _analytics.GetDailyReportAsync(kemarin);
        if (laporan.JumlahPesanan > 0 || laporan.Pendapatan != 0 || laporan.Beban != 0)
            ditulis += await KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin],
                new KirimNotifikasi(
                    "report.daily", NotificationSeverity.Info,
                    $"Ringkasan harian {kemarin:yyyy-MM-dd}",
                    $"Pendapatan {Money.Format(laporan.Pendapatan)}; beban {Money.Format(laporan.Beban)}; "
                    + $"selisih pendapatan − beban {Money.Format(laporan.LabaKotor)}. Pesanan {laporan.JumlahPesanan} "
                    + $"(batal {laporan.JumlahPesananBatal}), {laporan.UnitTerjual:0.###} unit, "
                    + $"nilai {Money.Format(laporan.NilaiPesanan)}.",
                    DedupeKey: $"report.daily:{kemarin:yyyy-MM-dd}"), nowUtc);

        return ditulis;
    }

    private static Notification Bangun(int userId, KirimNotifikasi isi, DateTime nowUtc)
    {
        var dasar = isi.DedupeKey is { Length: > 0 }
            ? isi.DedupeKey
            : $"{isi.EventType}:{nowUtc:yyyyMMddHHmmssfff}";
        return new Notification
        {
            EventType = isi.EventType,
            Severity = isi.Severity,
            Title = isi.Title,
            Body = isi.Body,
            EntityType = isi.EntityType,
            EntityId = isi.EntityId,
            RecipientUserId = userId,
            DedupeKey = $"{dasar}:u{userId}",
            CreatedAt = nowUtc,
            MetadataJson = isi.MetadataJson
        };
    }
}
