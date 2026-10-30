namespace Seruni22Bakery.Domain.Entities;

public enum NotificationSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Satu notifikasi internal untuk satu pengguna. Isinya disalin saat peristiwa terjadi supaya
/// riwayat tetap terbaca walau data sumbernya berubah kemudian. Notifikasi ini tidak pernah
/// dikirim ke layanan luar.
/// </summary>
public class Notification
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public int RecipientUserId { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? MetadataJson { get; set; }
}

/// <summary>Hitungan untuk lencana dan ringkasan kotak notifikasi satu pengguna.</summary>
public record NotificationSummary(int Total, int Unread, int CriticalUnread);
