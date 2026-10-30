namespace Seruni22Bakery.Infrastructure.Sqlite.Database;

/// <summary>
/// Ambang kapasitas. Nilai bawaan sengaja longgar untuk toko roti (basis data tumbuh hitungan
/// puluhan MB per tahun), tetapi bisa diubah pemilik lewat pengaturan tanpa membangun ulang.
/// </summary>
public sealed record StorageThresholds(
    long DbWarnBytes,
    long DbCriticalBytes,
    long FreeWarnBytes,
    long FreeCriticalBytes,
    int BackupMaxAgeHours)
{
    public const long Mb = 1024L * 1024L;

    public static StorageThresholds Bawaan { get; } = new(
        DbWarnBytes: 1024 * Mb,
        DbCriticalBytes: 4096 * Mb,
        FreeWarnBytes: 2048 * Mb,
        FreeCriticalBytes: 512 * Mb,
        BackupMaxAgeHours: 48);

    public static StorageThresholds DariPengaturan(IReadOnlyDictionary<string, string> pengaturan) => new(
        Baca(pengaturan, "storage.db_warn_mb", 1024) * Mb,
        Baca(pengaturan, "storage.db_critical_mb", 4096) * Mb,
        Baca(pengaturan, "storage.free_warn_mb", 2048) * Mb,
        Baca(pengaturan, "storage.free_critical_mb", 512) * Mb,
        (int)Baca(pengaturan, "backup.max_age_hours", 48));

    private static long Baca(IReadOnlyDictionary<string, string> isi, string kunci, long bawaan) =>
        isi.TryGetValue(kunci, out var teks) && long.TryParse(teks, out var nilai) && nilai >= 0 ? nilai : bawaan;
}

/// <summary>Potret pemakaian ruang yang dilaporkan ke layar dan ke notifikasi internal.</summary>
public sealed record StorageSnapshot(
    long DbBytes,
    long WalBytes,
    long ShmBytes,
    long BackupBytes,
    int BackupCount,
    long LogBytes,
    long FreeBytes,
    long TotalBytes,
    DateTime? LastVerifiedBackupUtc,
    string Level,
    IReadOnlyList<string> Reasons,
    DateTime CheckedAtUtc);

/// <summary>
/// Menilai kesehatan ruang penyimpanan dari ukuran berkas dan sisa disk yang sebenarnya.
/// Tiga tingkat: normal, perhatian, kritis. Penilaian tidak menulis apa pun; pengiriman
/// notifikasi dilakukan pemanggil supaya satu kondisi tidak menghasilkan pesan berulang.
/// </summary>
public static class StorageMonitor
{
    public const string Normal = "normal";
    public const string Attention = "perhatian";
    public const string Critical = "kritis";

    public static StorageSnapshot Evaluate(
        string databasePath,
        string backupDirectory,
        string logDirectory,
        StorageThresholds thresholds,
        DateTime? sekarang = null)
    {
        var now = sekarang ?? DateTime.UtcNow;
        var alasan = new List<string>();

        var dbBytes = Ukuran(databasePath);
        var walBytes = Ukuran(databasePath + "-wal");
        var shmBytes = Ukuran(databasePath + "-shm");
        var logBytes = UkuranFolder(logDirectory);

        var cadangan = BackupCatalog.Baca(backupDirectory);
        var backupBytes = cadangan.Sum(c => c.SizeBytes);
        var terakhir = cadangan.FirstOrDefault(c => c.Terverifikasi)?.CreatedAtUtc;

        long free = 0, total = 0;
        try
        {
            var akar = Path.GetPathRoot(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(akar))
            {
                var drive = new DriveInfo(akar);
                if (drive.IsReady) { free = drive.AvailableFreeSpace; total = drive.TotalSize; }
            }
        }
        catch (IOException) { }
        catch (ArgumentException) { }

        var kritis = false;
        var perhatian = false;
        var dataBytes = dbBytes + walBytes;

        if (free > 0 && free < thresholds.FreeCriticalBytes)
        {
            kritis = true;
            alasan.Add($"sisa disk {Mb(free)} MB, di bawah batas kritis {Mb(thresholds.FreeCriticalBytes)} MB");
        }
        else if (free > 0 && free < thresholds.FreeWarnBytes)
        {
            perhatian = true;
            alasan.Add($"sisa disk {Mb(free)} MB, di bawah batas perhatian {Mb(thresholds.FreeWarnBytes)} MB");
        }

        if (dataBytes > thresholds.DbCriticalBytes)
        {
            kritis = true;
            alasan.Add($"basis data + WAL {Mb(dataBytes)} MB melewati batas kritis {Mb(thresholds.DbCriticalBytes)} MB");
        }
        else if (dataBytes > thresholds.DbWarnBytes)
        {
            perhatian = true;
            alasan.Add($"basis data + WAL {Mb(dataBytes)} MB melewati batas perhatian {Mb(thresholds.DbWarnBytes)} MB");
        }

        if (terakhir is null)
        {
            // Cadangan lama tanpa manifes belum tentu rusak, jadi belum layak disebut kritis;
            // yang kritis adalah keadaan tanpa satu pun berkas cadangan.
            if (cadangan.Count == 0)
            {
                kritis = true;
                alasan.Add("belum ada cadangan terverifikasi di folder cadangan");
            }
            else
            {
                perhatian = true;
                alasan.Add($"{cadangan.Count} cadangan ditemukan tetapi belum ada yang terverifikasi; buat cadangan baru");
            }
        }
        else
        {
            var umurJam = (now - terakhir.Value).TotalHours;
            if (umurJam > thresholds.BackupMaxAgeHours * 3)
            {
                kritis = true;
                alasan.Add($"cadangan terverifikasi terakhir {umurJam:0} jam lalu " +
                           $"(batas kritis {thresholds.BackupMaxAgeHours * 3} jam)");
            }
            else if (umurJam > thresholds.BackupMaxAgeHours)
            {
                perhatian = true;
                alasan.Add($"cadangan terverifikasi terakhir {umurJam:0} jam lalu " +
                           $"(diharapkan tiap {thresholds.BackupMaxAgeHours} jam)");
            }
        }

        return new StorageSnapshot(
            dbBytes, walBytes, shmBytes, backupBytes, cadangan.Count, logBytes, free, total,
            terakhir,
            kritis ? Critical : perhatian ? Attention : Normal,
            alasan,
            now);
    }

    private static long Ukuran(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException) { return 0; }
    }

    private static long UkuranFolder(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return 0;
        try
        {
            return Directory.EnumerateFiles(directory, "*.log", SearchOption.AllDirectories)
                .Sum(f => Ukuran(f));
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.#");
}
