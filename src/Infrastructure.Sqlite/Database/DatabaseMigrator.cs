using Microsoft.Data.Sqlite;
using System.Reflection;

namespace Seruni22Bakery.Infrastructure.Sqlite.Database;

public class DatabaseMigrator
{
    private readonly string _connectionString;
    private readonly string? _backupDirectory;
    private readonly Action<string>? _log;

    public DatabaseMigrator(string connectionString, string? backupDirectory = null, Action<string>? log = null)
    {
        _connectionString = connectionString;
        _backupDirectory = backupDirectory;
        _log = log;
    }

    public void Migrate()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Enable WAL and foreign keys
        ExecuteCommand(conn, "PRAGMA journal_mode=WAL;");
        ExecuteCommand(conn, "PRAGMA foreign_keys=ON;");
        ExecuteCommand(conn, "PRAGMA busy_timeout=5000;");

        // Ensure SchemaVersion table exists
        ExecuteCommand(conn, @"
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version   INTEGER PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );");

        int currentVersion = GetCurrentVersion(conn);

        var migrations = GetMigrations().OrderBy(m => m.Version).ToList();
        int latestVersion = migrations.Count == 0 ? 0 : migrations[^1].Version;

        if (currentVersion > latestVersion)
        {
            throw new InvalidOperationException(
                $"Basis data memakai skema {currentVersion}, lebih baru daripada yang dikenal aplikasi ini ({latestVersion}). " +
                "Pasang aplikasi versi yang sesuai; database tidak diubah.");
        }

        // Upgrade hanya menyentuh struktur dan tidak menghapus data pengguna. Salinan penuh
        // dibuat lebih dulu; bila salinan gagal, migrasi dibatalkan daripada berjalan tanpa
        // jalan pulang. Berkas yang sudah berisi tabel pengguna tetapi belum punya baris
        // SchemaVersion juga harus disalin: migrasi dari nol akan berjalan di atas data nyata.
        if (latestVersion > currentVersion && (currentVersion > 0 || BasisDataSudahBerisiData(conn)))
            BuatCadanganPraUpgrade(currentVersion, latestVersion);

        foreach (var (version, sql) in migrations.Where(m => m.Version > currentVersion))
        {
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var statement in SplitStatements(sql))
                {
                    if (!string.IsNullOrWhiteSpace(statement))
                        ExecuteCommand(conn, statement, tx);
                }
                ExecuteCommand(conn,
                    $"INSERT OR REPLACE INTO SchemaVersion (Version, AppliedAt) VALUES ({version}, datetime('now'));",
                    tx);
                tx.Commit();
                _log?.Invoke($"Migrasi {version} diterapkan.");
                Console.WriteLine($"[Migration] Applied version {version}");
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    public static int LatestEmbeddedVersion() => GetMigrations().Select(m => m.Version).DefaultIfEmpty(0).Max();

    private void BuatCadanganPraUpgrade(int dari, int ke)
    {
        if (string.IsNullOrWhiteSpace(_backupDirectory)) return;

        var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath)) return;

        var cadangan = new DatabaseBackupService()
            .CreateBackup(dbPath, _backupDirectory, $"pre-upgrade-v{dari}-to-v{ke}");
        _log?.Invoke($"Cadangan sebelum upgrade skema v{dari}->v{ke}: {cadangan}");
    }

    private int GetCurrentVersion(SqliteConnection conn)
    {
        // Tabel yang benar-benar belum ada berarti basis data baru (versi 0). Tabel ada tetapi
        // isinya tidak bisa dibaca adalah kerusakan: menyamakannya dengan 0 akan membuat
        // cadangan pra-upgrade dilewati dan seluruh migrasi berjalan di atas data nyata.
        if (!TabelAda(conn, "SchemaVersion")) return 0;

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (Exception ex) when (ex is SqliteException or FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException(
                "Versi skema tidak bisa dibaca dari tabel SchemaVersion. Aplikasi berhenti agar migrasi " +
                "tidak menimpa basis data yang sudah berisi data.", ex);
        }
    }

    private static bool TabelAda(SqliteConnection conn, string nama)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @nama;";
        cmd.Parameters.AddWithValue("@nama", nama);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>
    /// Basis data lama bisa berisi tabel pengguna tanpa satu pun baris SchemaVersion (mis. berkas
    /// dari versi sebelum pencatatan versi). Berkas seperti itu tetap harus dicadangkan sebelum
    /// migrasi dijalankan, karena migrasi akan mulai dari versi 1.
    /// </summary>
    private static bool BasisDataSudahBerisiData(SqliteConnection conn)
        => TabelAda(conn, "Users") || TabelAda(conn, "SalesOrders") || TabelAda(conn, "AppSettings");

    private void ExecuteCommand(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (tx != null) cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    // Memecah per titik-koma yang berada di luar komentar baris dan literal teks,
    // karena titik-koma di dalam komentar pernah memecah satu pernyataan jadi dua.
    internal static IEnumerable<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inString = false;
        bool inLineComment = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            if (inLineComment)
            {
                sb.Append(c);
                if (c == '\n') inLineComment = false;
                continue;
            }

            if (inString)
            {
                sb.Append(c);
                if (c == '\'')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                    }
                    else inString = false;
                }
                continue;
            }

            if (c == '\'') { inString = true; sb.Append(c); continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                inLineComment = true;
                sb.Append(c);
                continue;
            }
            if (c == ';')
            {
                // Badan trigger memakai titik-koma di antara BEGIN ... END, dan satu
                // CREATE TRIGGER harus dikirim sebagai satu pernyataan.
                if (HasOpenTriggerBlock(sb))
                {
                    sb.Append(c);
                    continue;
                }
                statements.Add(sb.ToString());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }

        if (sb.Length > 0) statements.Add(sb.ToString());
        // Potongan berisi spasi/baru saja bukan pernyataan; kalau dibiarkan, setiap file
        // migrasi menyumbang satu "statement" kosong di akhir.
        return statements.Where(s => !string.IsNullOrWhiteSpace(s));
    }

    private static bool HasOpenTriggerBlock(System.Text.StringBuilder buffer)
    {
        var words = SignificantWords(buffer.ToString());
        if (words.Count < 2 || words[0] != "CREATE" || words[1] != "TRIGGER") return false;

        // Kedalaman blok, bukan sekadar jumlah kata: CASE ... END di dalam ekspresi
        // membuka dan menutup bloknya sendiri. Kalau END milik CASE dihitung sebagai
        // penutup trigger, pernyataan terpotong di tengah dan SQLite menolak dengan
        // "incomplete input" (pernah terjadi pada pemicu QC migrasi 013).
        int depth = 0;
        foreach (var word in words)
        {
            if (word == "BEGIN" || word == "CASE") depth++;
            else if (word == "END") depth--;
        }
        return depth > 0;
    }

    /// <summary>Kata kunci tanpa komentar baris dan isi literal teks, dalam huruf besar.</summary>
    private static List<string> SignificantWords(string sql)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        bool inString = false;
        bool inLineComment = false;

        void Flush()
        {
            if (word.Length > 0) { words.Add(word.ToString().ToUpperInvariant()); word.Clear(); }
        }

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }
            if (inString)
            {
                if (c == '\'')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\'') i++;
                    else inString = false;
                }
                continue;
            }
            if (c == '\'') { inString = true; Flush(); continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { inLineComment = true; Flush(); continue; }
            if (char.IsLetterOrDigit(c) || c == '_') word.Append(c);
            else Flush();
        }
        Flush();
        return words;
    }

    private record MigrationFile(int Version, string Sql);

    private static IEnumerable<MigrationFile> GetMigrations()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resources = asm.GetManifestResourceNames()
            .Where(r => r.Contains("Migrations") && r.EndsWith(".sql"))
            .OrderBy(r => r);

        foreach (var res in resources)
        {
            using var stream = asm.GetManifestResourceStream(res)!;
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();
            // Resource name format: Seruni22Bakery.Infrastructure.Sqlite.Migrations.001_InitialSchema.sql
            var parts = res.Split('.');
            var nameWithoutExt = parts.Length >= 2 ? parts[^2] : res;
            var numberStr = new string(nameWithoutExt.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(numberStr, out int version))
                yield return new MigrationFile(version, sql);
        }
    }
}
