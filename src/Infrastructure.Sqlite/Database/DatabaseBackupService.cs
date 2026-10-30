using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Seruni22Bakery.Infrastructure.Sqlite.Database;

/// <summary>
/// Cadangan dan pemulihan basis data SQLite. Cadangan dibuat lewat API backup SQLite
/// (bukan salin berkas), sehingga isi WAL yang belum checkpoint ikut terbawa dan hasilnya
/// satu berkas utuh. Berkas cadangan baru diumumkan dengan namanya setelah lolos pemeriksaan
/// integritas, kunci asing, dan pencatatan sidik SHA-256 di manifesnya — cadangan yang tampak
/// berhasil tetapi tidak bisa dipulihkan tidak boleh menyandang nama cadangan.
/// </summary>
public sealed class DatabaseBackupService
{
    public const string IntegrityOk = "ok";
    public const string ForeignKeysOk = "ok";

    /// <summary>Membuat cadangan, memverifikasinya, lalu menerbitkan berkas + manifes secara atomik.</summary>
    public string CreateBackup(string databasePath, string backupDirectory, string label = "cadangan",
        Action<string>? log = null)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("Basis data yang akan dicadangkan tidak ditemukan.", databasePath);

        Directory.CreateDirectory(backupDirectory);

        var safeLabel = new string(label
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeLabel)) safeLabel = "cadangan";

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(backupDirectory, $"{safeLabel}-{stamp}.db");
        for (var i = 1; File.Exists(target) || File.Exists(BackupManifestFile.PathUntuk(target)); i++)
            target = Path.Combine(backupDirectory, $"{safeLabel}-{stamp}-{i}.db");

        // Berkas sementara di folder yang sama: penggantian nama tetap satu volume sehingga
        // tidak ada saat di mana nama cadangan menunjuk berkas setengah jadi.
        var temp = target + ".tmp";
        TryDelete(temp);
        TryDelete(temp + "-wal");
        TryDelete(temp + "-shm");

        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = databasePath,
                       // Baca-tulis hanya untuk membuka WAL yang belum checkpoint; API backup
                       // sendiri tidak menulis ke basis data sumber.
                       Mode = SqliteOpenMode.ReadWrite
                   }.ToString()))
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = temp,
                       // Koneksi pool yang tertutup masih memegang berkas di Windows, sehingga
                       // penggantian nama ke nama cadangan resmi bisa gagal.
                       Pooling = false
                   }.ToString()))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            BersihkanSidecarKosong(temp);
            SatukanWal(temp);

            var integrity = CheckIntegrity(temp);
            if (integrity != IntegrityOk)
                throw new InvalidOperationException($"Cadangan gagal pemeriksaan integritas: {integrity}");

            var pelanggaran = CheckForeignKeys(temp);
            if (pelanggaran != 0)
                throw new InvalidOperationException(
                    $"Cadangan ditolak: {pelanggaran} baris melanggar kunci asing, sehingga laporan dan stok " +
                    "hasil pemulihan tidak akan cocok dengan dokumennya.");

            var ukuran = new FileInfo(temp).Length;
            var sha = BackupManifestFile.Sha256(temp);
            var versiSkema = ReadSchemaVersion(temp);

            // Sidecar hasil pemeriksaan baca-saja dibersihkan sebelum penerbitan; kalau
            // tertinggal, folder cadangan berisi berkas pendamping yang tidak ikut tersalin.
            BersihkanSidecarCadangan(temp);
            File.Move(temp, target);
            TryDelete(temp + "-wal");
            TryDelete(temp + "-shm");

            var manifes = new BackupManifest(
                BackupManifest.FormatVersion,
                Path.GetFileName(target),
                ukuran,
                sha,
                DateTime.UtcNow,
                BackupKind.DariLabel(label),
                label,
                versiSkema,
                AppVersion(),
                integrity,
                ForeignKeysOk,
                HitungBaris(target, "SalesOrders"),
                HitungBaris(target, "JournalEntries"),
                HitungBaris(target, "StockMovements"));

            BackupManifestFile.Tulis(target, manifes);
            BersihkanSidecarCadangan(target);
            log?.Invoke($"Cadangan terverifikasi: {target} (skema {versiSkema}, {ukuran / 1024.0 / 1024.0:0.0} MB, " +
                        $"sha256 {sha[..Math.Min(12, sha.Length)]}…).");
            return target;
        }
        catch
        {
            TryDelete(temp);
            TryDelete(temp + "-wal");
            TryDelete(temp + "-shm");
            throw;
        }
    }

    /// <summary>
    /// Memeriksa satu berkas cadangan terhadap manifesnya dan terhadap SQLite. Berkas yang
    /// checksum atau ukurannya tidak cocok berarti sudah berubah setelah dibuat, sehingga
    /// isinya tidak bisa dipercaya meskipun bisa dibuka.
    /// </summary>
    public IReadOnlyList<IntegrityCheck> Verify(string backupPath)
    {
        var hasil = new List<IntegrityCheck>();
        var ada = File.Exists(backupPath);
        hasil.Add(new IntegrityCheck("berkas cadangan ada", ada, ada ? backupPath : $"tidak ditemukan: {backupPath}"));
        if (!ada) return hasil;

        var manifes = BackupManifestFile.Baca(backupPath);
        hasil.Add(new IntegrityCheck("manifes tersedia", manifes is not null,
            manifes is null
                ? "cadangan tanpa manifes (dibuat versi lama); checksum tidak bisa dibandingkan"
                : BackupManifestFile.PathUntuk(backupPath)));

        if (manifes is not null)
        {
            var ukuran = new FileInfo(backupPath).Length;
            var ukuranCocok = ukuran == manifes.SizeBytes;
            hasil.Add(new IntegrityCheck("ukuran sesuai manifes", ukuranCocok,
                $"{ukuran} byte di berkas, {manifes.SizeBytes} byte di manifes"));

            var checksumCocok = BackupManifestFile.ChecksumCocok(backupPath, manifes.Sha256);
            hasil.Add(new IntegrityCheck("checksum sha256 sesuai", checksumCocok,
                checksumCocok ? manifes.Sha256 : "isi berkas berubah setelah cadangan dibuat"));
        }

        string integritas;
        try { integritas = CheckIntegrity(backupPath); }
        catch (SqliteException ex)
        {
            hasil.Add(new IntegrityCheck("PRAGMA integrity_check", false, ex.Message));
            return hasil;
        }
        hasil.Add(new IntegrityCheck("PRAGMA integrity_check", integritas == IntegrityOk, integritas));

        int pelanggaran;
        try { pelanggaran = CheckForeignKeys(backupPath); }
        catch (SqliteException ex)
        {
            hasil.Add(new IntegrityCheck("PRAGMA foreign_key_check", false, ex.Message));
            return hasil;
        }
        hasil.Add(new IntegrityCheck("PRAGMA foreign_key_check", pelanggaran == 0,
            pelanggaran == 0 ? "tidak ada baris yang menunjuk induk yang hilang" : $"{pelanggaran} baris melanggar"));

        var versi = ReadSchemaVersion(backupPath);
        var didukung = DatabaseMigrator.LatestEmbeddedVersion();
        hasil.Add(new IntegrityCheck("skema bisa dipulihkan aplikasi ini", versi <= didukung,
            $"cadangan {versi}, aplikasi ini mengenal sampai {didukung}"));
        BersihkanSidecarCadangan(backupPath);
        return hasil;
    }

    /// <summary>
    /// Memulihkan basis data dari cadangan. Basis data yang sedang berlaku dicadangkan lebih
    /// dulu; bila pemulihan atau migrasi setelahnya gagal, salinan pengaman itu dikembalikan.
    /// </summary>
    public string Restore(string backupPath, string databasePath, string backupDirectory, Action<string>? log = null)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Berkas cadangan tidak ditemukan.", backupPath);

        PastikanCadanganUtuh(backupPath, log);

        var backupVersion = ReadSchemaVersion(backupPath);
        var supportedVersion = DatabaseMigrator.LatestEmbeddedVersion();
        if (backupVersion > supportedVersion)
            throw new InvalidOperationException(
                $"Cadangan memakai skema {backupVersion}, lebih baru daripada aplikasi ini ({supportedVersion}). " +
                "Pasang aplikasi versi yang sesuai untuk memulihkannya.");
        BersihkanSidecarCadangan(backupPath);

        var safetyCopy = File.Exists(databasePath)
            ? BuatSalinanPengaman(databasePath, backupDirectory, log)
            : null;
        log?.Invoke(safetyCopy is null
            ? "Pemulihan dimulai tanpa salinan pengaman yang bisa dipulihkan."
            : $"Salinan pengaman sebelum pemulihan: {safetyCopy}");

        SqliteConnection.ClearAllPools();
        // Penutupan koneksi pool di Microsoft.Data.Sqlite berjalan di latar belakang,
        // jadi pegangan berkas bisa masih ada sesaat setelah ClearAllPools. Menimpa DB
        // sebelum pegangan itu lepas akan gagal di Windows; tunggu sampai benar-benar bebas.
        TungguBerkasBebas(databasePath);
        foreach (var suffix in new[] { "-wal", "-shm" })
            HapusSidecar(databasePath + suffix);

        var temp = databasePath + ".restore-tmp";
        try
        {
            File.Copy(backupPath, temp, overwrite: true);
            PindahkanDenganCoba(temp, databasePath);

            new DatabaseMigrator($"Data Source={databasePath};", backupDirectory, log).Migrate();

            var afterRestore = CheckIntegrity(databasePath);
            if (afterRestore != IntegrityOk)
                throw new InvalidOperationException($"Basis data hasil pemulihan gagal pemeriksaan integritas: {afterRestore}");

            // Integritas halaman saja tidak cukup: baris yang menunjuk dokumen yang tidak ikut
            // terbawa akan membuat stok dan laporan berbeda dari sebelum bencana.
            var yatim = CheckForeignKeys(databasePath);
            if (yatim != 0)
                throw new InvalidOperationException(
                    $"Hasil pemulihan memiliki {yatim} baris yang menunjuk induk hilang; " +
                    "pemulihan dibatalkan dan basis data sebelum pemulihan dikembalikan.");

            log?.Invoke($"Pemulihan selesai: {databasePath} (skema {ReadSchemaVersion(databasePath)}).");
            return safetyCopy ?? string.Empty;
        }
        catch
        {
            TryDelete(temp);
            if (safetyCopy is not null)
            {
                // Kembalikan keadaan sebelum pemulihan supaya kegagalan tidak meninggalkan
                // basis data setengah jadi tanpa jalan pulang. Kegagalan pengembalian tidak
                // boleh menutupi galat pemulihan yang asli.
                try
                {
                    SqliteConnection.ClearAllPools();
                    TungguBerkasBebas(databasePath);
                    foreach (var suffix in new[] { "-wal", "-shm" })
                        HapusSidecar(databasePath + suffix);
                    File.Copy(safetyCopy, databasePath, overwrite: true);
                }
                catch { /* galat asli tetap dilempar di bawah */ }
            }
            throw;
        }
    }

    /// <summary>
    /// Menolak cadangan yang berubah setelah dibuat. Manifes yang tidak ada hanya membuat
    /// peringatan: cadangan dari versi lama tetap harus bisa dipulihkan saat dibutuhkan.
    /// </summary>
    private static void PastikanCadanganUtuh(string backupPath, Action<string>? log)
    {
        var manifes = BackupManifestFile.Baca(backupPath);
        if (manifes is null)
        {
            log?.Invoke("Cadangan tanpa manifes (versi lama): checksum tidak dapat dibandingkan. " +
                        "Hanya integritas SQLite yang diperiksa.");
        }
        else
        {
            var ukuran = new FileInfo(backupPath).Length;
            if (ukuran != manifes.SizeBytes)
                throw new InvalidOperationException(
                    $"Berkas cadangan tidak cocok dengan manifesnya: {ukuran} byte di berkas, " +
                    $"{manifes.SizeBytes} byte di manifes. Pemulihan dibatalkan.");
            if (!BackupManifestFile.ChecksumCocok(backupPath, manifes.Sha256))
                throw new InvalidOperationException(
                    "Checksum SHA-256 berkas cadangan tidak cocok dengan manifesnya. Isi berkas berubah " +
                    "setelah cadangan dibuat, jadi pemulihan dibatalkan.");
            log?.Invoke($"Manifes cocok: dibuat {manifes.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}, " +
                        $"skema {manifes.SchemaVersion}, {manifes.Orders} pesanan, {manifes.JournalEntries} entri jurnal.");
        }

        string integritas;
        try { integritas = CheckIntegrity(backupPath); }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException("Berkas tidak dapat dibaca sebagai basis data SQLite yang sah.", ex);
        }
        if (integritas != IntegrityOk)
            throw new InvalidOperationException($"Berkas cadangan rusak dan tidak dipulihkan: {integritas}");

        var yatim = CheckForeignKeys(backupPath);
        if (yatim != 0)
            throw new InvalidOperationException(
                $"Cadangan berisi {yatim} baris yang menunjuk induk hilang; memulihkannya akan menghasilkan stok " +
                "dan laporan yang tidak cocok. Pemulihan dibatalkan.");
    }

    /// <summary>
    /// Salinan pengaman sebelum pemulihan. Basis data yang sudah rusak tidak bisa dicadangkan
    /// lewat API backup, dan justru keadaan itulah yang paling membutuhkan pemulihan — jadi
    /// kegagalan cadangan tidak menghentikan pemulihan. Berkas rusaknya disalin apa adanya
    /// sebagai bahan pemeriksaan, lalu pemulihan diteruskan.
    /// </summary>
    private string? BuatSalinanPengaman(string databasePath, string backupDirectory, Action<string>? log)
    {
        Directory.CreateDirectory(backupDirectory);
        try
        {
            return CreateBackup(databasePath, backupDirectory, "pre-restore", log);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Cadangan pengaman gagal dibuat: {ex.Message} Basis data yang sedang berlaku " +
                        "tidak bisa dibaca utuh; pemulihan tetap dilanjutkan.");
        }

        try
        {
            var rusak = Path.Combine(backupDirectory, $"rusak-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            File.Copy(databasePath, rusak, overwrite: false);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(databasePath + suffix))
                    File.Copy(databasePath + suffix, rusak + suffix, overwrite: false);
            log?.Invoke($"Berkas basis data yang rusak disalin apa adanya ke {rusak} untuk pemeriksaan.");
        }
        catch (Exception ex)
        {
            log?.Invoke($"Berkas basis data yang rusak tidak bisa disalin: {ex.Message}. " +
                        "Pemulihan dilanjutkan tanpa salinan pengaman.");
        }
        return null;
    }

    public static string CheckIntegrity(string databasePath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            // Tanpa pooling: pegangan berkas yang tertinggal di pool menghalangi
            // penggantian nama cadangan sementara menjadi cadangan resmi.
            Pooling = false
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return Convert.ToString(cmd.ExecuteScalar()) ?? "tidak ada jawaban";
    }

    /// <summary>Jumlah baris yang melanggar kunci asing; nol berarti tidak ada.</summary>
    public static int CheckForeignKeys(string databasePath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            // Tanpa pooling: pegangan berkas yang tertinggal di pool menghalangi
            // penggantian nama cadangan sementara menjadi cadangan resmi.
            Pooling = false
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        var jumlah = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read()) jumlah++;
        return jumlah;
    }

    public static int ReadSchemaVersion(string databasePath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            // Tanpa pooling: pegangan berkas yang tertinggal di pool menghalangi
            // penggantian nama cadangan sementara menjadi cadangan resmi.
            Pooling = false
        }.ToString());
        conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private static long HitungBaris(string databasePath, string tabel)
    {
        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            // Nama tabel berasal dari kode sendiri, bukan masukan pengguna.
            cmd.CommandText = $"SELECT COUNT(*) FROM {tabel};";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private static string AppVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>
    /// Menunggu sampai tidak ada proses (termasuk pool koneksi yang sedang ditutup) yang
    /// memegang berkas. Membuka dengan FileShare.None adalah bukti pegangan sudah lepas.
    /// </summary>
    internal static void TungguBerkasBebas(string path, int percobaan = 30, int jedaMs = 100)
    {
        for (var i = 0; i < percobaan; i++)
        {
            if (!File.Exists(path)) return;
            try
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException) { Thread.Sleep(jedaMs); }
            catch (UnauthorizedAccessException) { Thread.Sleep(jedaMs); }
        }
        throw new InvalidOperationException(
            $"Berkas basis data masih dipakai proses lain setelah menunggu {percobaan * jedaMs} ms: {path}");
    }

    /// <summary>
    /// Menghapus -wal/-shm. Sidecar yang tertinggal di samping berkas hasil pemulihan bisa
    /// membawa bingkai lama ke basis data baru, jadi penghapusan harus terbukti berhasil.
    /// </summary>
    internal static void HapusSidecar(string path)
    {
        TungguBerkasBebas(path);
        File.Delete(path);
        if (File.Exists(path))
            throw new InvalidOperationException($"Berkas pendamping tidak bisa dihapus: {path}");
    }

    // ERROR_SHARING_VIOLATION (32) dan ERROR_LOCK_VIOLATION (33): pegangan bisa lepas
    // sedikit setelah pemeriksaan, jadi penggantian berkas diulang sebentar.
    private static void PindahkanDenganCoba(string dari, string ke, int percobaan = 30, int jedaMs = 100)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                File.Move(dari, ke, overwrite: true);
                return;
            }
            catch (IOException ex) when (i < percobaan && (ex.HResult & 0xFFFF) is 32 or 33)
            {
                Thread.Sleep(jedaMs);
            }
        }
    }

    private static void BersihkanSidecarKosong(string path)
    {
        // Koneksi tujuan bisa meninggalkan -wal/-shm kosong di samping berkas sementara.
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = path + suffix;
            var info = new FileInfo(sidecar);
            if (info.Exists && info.Length == 0) TryDelete(sidecar);
        }
    }

    /// <summary>
    /// Menyatukan isi WAL hasil backup ke berkas utama. Pemulihan hanya menyalin satu berkas
    /// .db, jadi bingkai yang tertinggal di -wal akan hilang tanpa jejak dan stok/laporan
    /// hasil pemulihan berbeda dari yang dicadangkan.
    /// </summary>
    private static void SatukanWal(string path)
    {
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = path,
                   Pooling = false
               }.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteScalar();
        }

        var wal = new FileInfo(path + "-wal");
        if (wal.Exists && wal.Length > 0)
            throw new InvalidOperationException(
                $"Isi WAL cadangan tidak bisa disatukan ke berkas utama ({wal.Length} byte tersisa); " +
                "cadangan dibatalkan supaya tidak ada data yang tertinggal di berkas pendamping.");

        BersihkanSidecarCadangan(path);
    }

    /// <summary>
    /// Menghapus -wal/-shm yang ditinggalkan pemeriksaan baca-saja atas berkas cadangan.
    /// -shm hanya wadah indeks yang selalu dibangun ulang; -wal hanya dihapus bila kosong,
    /// dan penghapusan dilewati selama masih ada pegangan lain pada berkasnya.
    /// </summary>
    private static void BersihkanSidecarCadangan(string path)
    {
        var wal = new FileInfo(path + "-wal");
        if (wal.Exists && wal.Length > 0) return;

        if (wal.Exists && !CobaHapus(wal.FullName)) return;
        CobaHapus(path + "-shm");
    }

    /// <summary>Menghapus berkas hanya bila tidak ada proses lain yang memegangnya.</summary>
    private static bool CobaHapus(string path, int percobaan = 10, int jedaMs = 100)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                if (!File.Exists(path)) return true;
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(path);
                return true;
            }
            catch (FileNotFoundException) { return true; }
            catch (IOException) when (i < percobaan) { Thread.Sleep(jedaMs); }
            catch (UnauthorizedAccessException) when (i < percobaan) { Thread.Sleep(jedaMs); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* berkas sementara; kegagalan hapus tidak boleh menutupi galat utama */ }
    }
}
