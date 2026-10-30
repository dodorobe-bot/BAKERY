using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Migrasi 015 menambah kotak notifikasi internal. Yang diuji bukan hanya "tabelnya ada",
/// tetapi janji yang dipakai layar: notifikasi selalu terikat ke pengguna penerima, kunci
/// dedupe menolak baris kembar, dan rollback benar-benar mencabut tabel beserta indeksnya.
/// </summary>
public class Migration015Tests
{
    private static int TambahPengguna(TempDatabase db, string username)
    {
        db.Exec("INSERT INTO Users (Username, PasswordHash, Role, IsActive, CreatedAt, TokenVersion) " +
                $"VALUES ('{username}', 'x', 'Pegawai', 1, '2026-10-28T00:00:00Z', 0);");
        return Convert.ToInt32(db.Scalar($"SELECT Id FROM Users WHERE Username = '{username}';"));
    }

    [Fact]
    public void Migrasi015_MenyediakanTabelNotifikasiDanIndeksnya()
    {
        using var db = new TempDatabase();

        db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Notifications';")
            .Should().Be(1L);
        db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ux_notifications_dedupe';")
            .Should().Be(1L);
        db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_notifications_inbox';")
            .Should().Be(1L);
        db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_notifications_event';")
            .Should().Be(1L);
    }

    [Fact]
    public void Notifikasi_TerikatKePenggunaPenerima()
    {
        using var db = new TempDatabase();

        var fk = db.Scalar("SELECT \"table\" FROM pragma_foreign_key_list('Notifications') LIMIT 1;");

        fk.Should().Be("Users", "menghapus pengguna harus ikut membersihkan kotaknya, bukan meninggalkan baris yatim");
    }

    [Fact]
    public void Notifikasi_MenolakSeverityDanIsReadDiLuarDaftar()
    {
        using var db = new TempDatabase();
        var userId = TambahPengguna(db, "kasir");

        var severitySalah = () => db.Exec(
            "INSERT INTO Notifications (EventType, Severity, Title, Body, RecipientUserId, DedupeKey, IsRead, CreatedAt) " +
            $"VALUES ('stock.low', 'Darurat', 'Judul', 'Isi', {userId}, 'kunci-1', 0, '2026-10-28T00:00:00Z');");
        var isReadSalah = () => db.Exec(
            "INSERT INTO Notifications (EventType, Severity, Title, Body, RecipientUserId, DedupeKey, IsRead, CreatedAt) " +
            $"VALUES ('stock.low', 'Info', 'Judul', 'Isi', {userId}, 'kunci-2', 2, '2026-10-28T00:00:00Z');");

        severitySalah.Should().Throw<SqliteException>();
        isReadSalah.Should().Throw<SqliteException>();
        db.Count("Notifications").Should().Be(0);
    }

    [Fact]
    public void Notifikasi_KunciDedupeKembar_DitolakBasisData()
    {
        using var db = new TempDatabase();
        var userId = TambahPengguna(db, "kasir");
        const string kolom =
            "(EventType, Severity, Title, Body, RecipientUserId, DedupeKey, IsRead, CreatedAt)";
        db.Exec($"INSERT INTO Notifications {kolom} " +
                $"VALUES ('stock.low', 'Warning', 'Judul', 'Isi', {userId}, 'stock.low:1:2026-10-28:u{userId}', 0, '2026-10-28T00:00:00Z');");

        var kembar = () => db.Exec($"INSERT INTO Notifications {kolom} " +
                $"VALUES ('stock.low', 'Warning', 'Judul lain', 'Isi lain', {userId}, 'stock.low:1:2026-10-28:u{userId}', 0, '2026-10-28T01:00:00Z');");

        kembar.Should().Throw<SqliteException>("pengingat yang sama tidak boleh menumpuk tiap kali disegarkan");
        db.Count("Notifications").Should().Be(1);
    }

    [Fact]
    public void Notifikasi_TanpaPenerimaYangAda_Ditolak()
    {
        using var db = new TempDatabase();

        var tindakan = () => db.Exec(
            "INSERT INTO Notifications (EventType, Severity, Title, Body, RecipientUserId, DedupeKey, IsRead, CreatedAt) " +
            "VALUES ('stock.low', 'Info', 'Judul', 'Isi', 999, 'kunci-yatim', 0, '2026-10-28T00:00:00Z');");

        tindakan.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Rollback015_MencabutTabelIndeksDanVersi()
    {
        using var db = new TempDatabase();
        var userId = TambahPengguna(db, "kasir");
        db.Exec("INSERT INTO Notifications (EventType, Severity, Title, Body, RecipientUserId, DedupeKey, IsRead, CreatedAt) " +
                $"VALUES ('stock.low', 'Warning', 'Judul', 'Isi', {userId}, 'kunci-1', 0, '2026-10-28T00:00:00Z');");

        MigrationRollback.Run(db, "015_InternalNotifications.down.sql");

        db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Notifications';")
            .Should().Be(0L);
        db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE '%notifications%';")
            .Should().Be(0L);
        db.Scalar("SELECT COUNT(*) FROM SchemaVersion WHERE Version = 15;").Should().Be(0L);
        db.Scalar("SELECT COUNT(*) FROM Users WHERE Username = 'kasir';").Should().Be(1L,
            "rollback hanya mencabut kotak notifikasi, bukan akun pengguna");
    }
}
