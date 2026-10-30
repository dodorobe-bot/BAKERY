using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Domain.Interfaces;

/// <summary>Kotak notifikasi internal: hanya baris milik satu pengguna yang boleh dibaca pengguna itu.</summary>
public interface INotificationRepository
{
    /// <returns>Id baris baru, atau 0 bila DedupeKey sudah ada (peristiwa yang sama).</returns>
    Task<int> CreateAsync(Notification notification);

    Task<IReadOnlyList<Notification>> ListAsync(int recipientUserId, bool unreadOnly, int limit);

    Task<NotificationSummary> SummaryAsync(int recipientUserId);

    Task<bool> MarkReadAsync(int id, int recipientUserId, DateTime readAtUtc);

    Task<int> MarkAllReadAsync(int recipientUserId, DateTime readAtUtc);
}
