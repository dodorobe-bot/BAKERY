using System.Text.Json.Serialization;

namespace Seruni22Bakery.Domain.Entities;

public enum UserRole { Owner, Admin, Dapur, Pegawai }

public class User
{
    public int Id { get; set; }
    public int? LegacyId { get; set; }
    public string Username { get; set; } = string.Empty;

    // Hash pernah ikut terkirim di GET /api/users: semua akun kebocor ke klien yang login.
    [JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
    /// <summary>Dinaikkan saat logout atau ganti sandi; token dengan versi lama langsung ditolak.</summary>
    public int TokenVersion { get; set; } = 0;

    public bool IsLocked() => LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow;
    public static string HashPassword(string p) => BCrypt.Net.BCrypt.HashPassword(p, 12);
    public bool VerifyPassword(string p) => BCrypt.Net.BCrypt.Verify(p, PasswordHash);
}
