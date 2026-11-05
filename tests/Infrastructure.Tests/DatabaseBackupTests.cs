using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace BakeryDesktop.Infrastructure.Tests;

public class DatabaseBackupTests
{
    [Fact]
    public void CreateBackup_MenyertakanDataDanLolosIntegritas()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO AppSettings (Key, Value) VALUES ('uji-cadangan', 'nilai-1');");
        var dir = DirektoriSementara();

        try
        {
            var path = new DatabaseBackupService().CreateBackup(db.FilePath, dir, "manual");

            File.Exists(path).Should().BeTrue();
            DatabaseBackupService.CheckIntegrity(path).Should().Be("ok");
            DatabaseBackupService.ReadSchemaVersion(path).Should().Be(DatabaseMigrator.LatestEmbeddedVersion());

            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'uji-cadangan';";
            Convert.ToString(cmd.ExecuteScalar()).Should().Be("nilai-1");
        }
        finally
        {
            Bersihkan(db, dir);
        }
    }

    [Fact]
    public void Restore_MengembalikanDataLama_DanMembuatSalinanPengaman()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO AppSettings (Key, Value) VALUES ('uji-restore', 'sebelum');");
        var dir = DirektoriSementara();

        try
        {
            var service = new DatabaseBackupService();
            var cadangan = service.CreateBackup(db.FilePath, dir, "manual");
            db.Exec("UPDATE AppSettings SET Value = 'sesudah' WHERE Key = 'uji-restore';");

            service.Restore(cadangan, db.FilePath, dir);

            db.Scalar("SELECT Value FROM AppSettings WHERE Key = 'uji-restore';")
                .Should().Be("sebelum");
            Directory.GetFiles(dir, "pre-restore-*.db").Should().NotBeEmpty();
            DatabaseBackupService.CheckIntegrity(db.FilePath).Should().Be("ok");
            DatabaseBackupService.ReadSchemaVersion(db.FilePath)
                .Should().Be(DatabaseMigrator.LatestEmbeddedVersion());
        }
        finally
        {
            Bersihkan(db, dir);
        }
    }

    [Fact]
    public void Restore_MenolakBerkasBukanDatabase_TanpaMengubahData()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO AppSettings (Key, Value) VALUES ('uji-rusak', 'utuh');");
        var dir = DirektoriSementara();

        try
        {
            var rusak = Path.Combine(dir, "rusak.db");
            File.WriteAllText(rusak, "ini bukan basis data SQLite");

            var aksi = () => new DatabaseBackupService().Restore(rusak, db.FilePath, dir);

            aksi.Should().Throw<InvalidOperationException>();
            db.Scalar("SELECT Value FROM AppSettings WHERE Key = 'uji-rusak';")
                .Should().Be("utuh");
        }
        finally
        {
            Bersihkan(db, dir);
        }
    }

    [Fact]
    public void Restore_MenolakCadanganDariSkemaLebihBaru()
    {
        using var db = new TempDatabase();
        var dir = DirektoriSementara();

        try
        {
            var service = new DatabaseBackupService();
            var cadangan = service.CreateBackup(db.FilePath, dir, "manual");

            // Pooling dimatikan supaya penutupan koneksi benar-benar menutup berkas dan
            // isi WAL menyatu ke berkas utama sebelum ukuran dan checksum dihitung.
            using (var conn = new SqliteConnection($"Data Source={cadangan};Pooling=False;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (999, datetime('now'));";
                cmd.ExecuteNonQuery();
            }

            // Berkas diubah sesudah manifes dibuat, jadi manifes diperbarui lebih dulu:
            // yang hendak diuji adalah penolakan versi skema, bukan checksum yang memang
            // seharusnya menolak berkas yang berubah tanpa manifes baru.
            BackupManifestFile.Tulis(cadangan, new BackupManifest(
                BackupManifest.FormatVersion, Path.GetFileName(cadangan),
                new FileInfo(cadangan).Length, BackupManifestFile.Sha256(cadangan),
                DateTime.UtcNow, BackupKind.Manual, "manual", 999, "uji",
                DatabaseBackupService.IntegrityOk, DatabaseBackupService.ForeignKeysOk, 0, 0, 0));

            var aksi = () => service.Restore(cadangan, db.FilePath, dir);

            aksi.Should().Throw<InvalidOperationException>()
                .WithMessage("*lebih baru*");
        }
        finally
        {
            Bersihkan(db, dir);
        }
    }

    [Fact]
    public void ReadSchemaVersion_TabelBelumAda_BerartiSkemaNol()
    {
        var dir = DirektoriSementara();
        try
        {
            var path = Path.Combine(dir, "baru.db");
            using (var conn = new SqliteConnection($"Data Source={path};"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Lain (Id INTEGER PRIMARY KEY);";
                cmd.ExecuteNonQuery();
            }

            DatabaseBackupService.ReadSchemaVersion(path).Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { /* berkas sementara */ }
        }
    }

    [Fact]
    public void ReadSchemaVersion_TabelAdaTetapiTidakTerbaca_DilemparBukanNol()
    {
        var dir = DirektoriSementara();
        try
        {
            var path = Path.Combine(dir, "rusak.db");
            using (var conn = new SqliteConnection($"Data Source={path};"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE SchemaVersion (Version TEXT PRIMARY KEY, AppliedAt TEXT NOT NULL);" +
                    "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES ('rusak', 'x');";
                cmd.ExecuteNonQuery();
            }

            var aksi = () => DatabaseBackupService.ReadSchemaVersion(path);

            aksi.Should().Throw<InvalidOperationException>().WithMessage("*tidak bisa dibaca*");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { /* berkas sementara */ }
        }
    }

    [Fact]
    public void Restore_MenolakCadanganYangVersinyaTidakTerbaca_TanpaMengubahData()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO AppSettings (Key, Value) VALUES ('uji-versi', 'utuh');");
        var dir = DirektoriSementara();

        try
        {
            var service = new DatabaseBackupService();
            var cadangan = service.CreateBackup(db.FilePath, dir, "manual");

            using (var conn = new SqliteConnection($"Data Source={cadangan};Pooling=False;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "DROP TABLE SchemaVersion;" +
                    "CREATE TABLE SchemaVersion (Version TEXT PRIMARY KEY, AppliedAt TEXT NOT NULL);" +
                    "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES ('rusak', 'x');";
                cmd.ExecuteNonQuery();
            }

            BackupManifestFile.Tulis(cadangan, new BackupManifest(
                BackupManifest.FormatVersion, Path.GetFileName(cadangan),
                new FileInfo(cadangan).Length, BackupManifestFile.Sha256(cadangan),
                DateTime.UtcNow, BackupKind.Manual, "manual", 0, "uji",
                DatabaseBackupService.IntegrityOk, DatabaseBackupService.ForeignKeysOk, 0, 0, 0));

            var aksi = () => service.Restore(cadangan, db.FilePath, dir);

            aksi.Should().Throw<InvalidOperationException>().WithMessage("*tidak bisa dibaca*");
            db.Scalar("SELECT Value FROM AppSettings WHERE Key = 'uji-versi';").Should().Be("utuh");
        }
        finally
        {
            Bersihkan(db, dir);
        }
    }

    [Fact]
    public void Migrate_MembuatCadanganPraUpgrade_DanTidakMenghapusData()
    {
        var dir = DirektoriSementara();
        var cadanganDir = Path.Combine(dir, "backups");

        try
        {
            var (path, connectionString) = BuatBasisDataVersi(dir, 13);
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES ('data-pengguna', 'jangan-hilang');";
                cmd.ExecuteNonQuery();
            }

            new DatabaseMigrator(connectionString, cadanganDir).Migrate();

            var cadangan = Directory.GetFiles(cadanganDir,
                $"pre-upgrade-v13-to-v{DatabaseMigrator.LatestEmbeddedVersion()}-*.db");
            cadangan.Should().HaveCount(1);
            DatabaseBackupService.CheckIntegrity(cadangan[0]).Should().Be("ok");
            DatabaseBackupService.ReadSchemaVersion(path).Should().Be(DatabaseMigrator.LatestEmbeddedVersion());

            using var cek = new SqliteConnection(connectionString);
            cek.Open();
            using var baca = cek.CreateCommand();
            baca.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'data-pengguna';";
            Convert.ToString(baca.ExecuteScalar()).Should().Be("jangan-hilang");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Migrate_MenolakBasisDataDariVersiLebihBaru_TanpaMengubahApaPun()
    {
        var dir = DirektoriSementara();
        var cadanganDir = Path.Combine(dir, "backups");

        try
        {
            var (path, connectionString) = BuatBasisDataVersi(dir, 15);
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (999, datetime('now'));";
                cmd.ExecuteNonQuery();
            }

            var aksi = () => new DatabaseMigrator(connectionString, cadanganDir).Migrate();

            aksi.Should().Throw<InvalidOperationException>().WithMessage("*lebih baru*");
            DatabaseBackupService.ReadSchemaVersion(path).Should().Be(999);
            Directory.Exists(cadanganDir).Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static string DirektoriSementara()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bakery-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Bersihkan(TempDatabase db, string dir)
    {
        SqliteConnection.ClearAllPools();
        db.Dispose();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static (string Path, string ConnectionString) BuatBasisDataVersi(string directory, int maxVersion)
    {
        var path = Path.Combine(directory, "lama.db");
        var connectionString = $"Data Source={path};";
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        using (var skema = conn.CreateCommand())
        {
            skema.CommandText = "CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);";
            skema.ExecuteNonQuery();
        }

        foreach (var (version, sql) in MigrasiTertanam().Where(m => m.Version <= maxVersion))
        {
            using var tx = conn.BeginTransaction();
            foreach (var statement in DatabaseMigrator.SplitStatements(sql))
            {
                if (string.IsNullOrWhiteSpace(statement)) continue;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = statement;
                cmd.ExecuteNonQuery();
            }
            using (var catat = conn.CreateCommand())
            {
                catat.Transaction = tx;
                catat.CommandText = $"INSERT OR REPLACE INTO SchemaVersion (Version, AppliedAt) VALUES ({version}, datetime('now'));";
                catat.ExecuteNonQuery();
            }
            tx.Commit();
        }

        return (path, connectionString);
    }

    private static IEnumerable<(int Version, string Sql)> MigrasiTertanam()
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(r => r.Contains("Migrations") && r.EndsWith(".sql") && !r.Contains("Rollback"))
            .OrderBy(r => r);

        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var parts = resource.Split('.');
            var name = parts.Length >= 2 ? parts[^2] : resource;
            var number = new string(name.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(number, out var version))
                yield return (version, reader.ReadToEnd());
        }
    }
}
