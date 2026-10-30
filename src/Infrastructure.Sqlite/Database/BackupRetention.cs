namespace Seruni22Bakery.Infrastructure.Sqlite.Database;

/// <summary>
/// Aturan penyimpanan cadangan: jendela harian, mingguan, dan bulanan seperti pola arsip
/// biasa, ditambah pagar yang tidak boleh dilanggar kebijakan apa pun.
/// </summary>
public sealed record RetentionPolicy(
    int HariSimpanSemua = 7,
    int HariHarian = 14,
    int MingguMingguan = 8,
    int BulanBulanan = 12,
    int MinimalSalinan = 3)
{
    public static RetentionPolicy DariPengaturan(IReadOnlyDictionary<string, string> pengaturan) => new(
        Baca(pengaturan, "backup.retention.keep_all_days", 7),
        Baca(pengaturan, "backup.retention.daily_days", 14),
        Baca(pengaturan, "backup.retention.weekly_weeks", 8),
        Baca(pengaturan, "backup.retention.monthly_months", 12),
        Baca(pengaturan, "backup.retention.minimum_copies", 3));

    private static int Baca(IReadOnlyDictionary<string, string> isi, string kunci, int bawaan) =>
        isi.TryGetValue(kunci, out var teks) && int.TryParse(teks, out var nilai) && nilai >= 0 ? nilai : bawaan;
}

public sealed record RetentionDecision(
    IReadOnlyList<string> Disimpan,
    IReadOnlyList<string> Dihapus);

/// <summary>
/// Pembersih cadangan lama. Tiga janji yang dijaga di sini:
/// salinan valid terbaru tidak pernah dihapus; cadangan pra-upgrade/pra-restore tidak pernah
/// dihapus; jumlah salinan tidak pernah turun di bawah batas minimum. Setiap penghapusan
/// ditulis ke <c>retention.log</c> beserta alasannya, bukan dihapus diam-diam.
/// </summary>
public static class BackupRetention
{
    public const string LogFile = "retention.log";

    public static RetentionDecision Terapkan(
        string directory, RetentionPolicy policy, DateTime? sekarang = null, Action<string>? log = null)
    {
        var entries = BackupCatalog.Baca(directory);
        if (entries.Count == 0) return new([], []);

        var sekarangUtc = sekarang ?? DateTime.UtcNow;
        var simpan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Cadangan pra-upgrade/pra-restore adalah satu-satunya jalan kembali ke skema lama.
        foreach (var e in entries.Where(e => e.Kind is BackupKind.PraUpgrade or BackupKind.PraRestore))
            simpan.Add(e.Path);

        // Salinan yang terbukti sehat dan paling baru tetap disimpan, bahkan bila umurnya
        // melewati semua jendela retensi (misalnya toko tutup sebulan).
        var terbaruSehat = entries.FirstOrDefault(e => e.Terverifikasi);
        if (terbaruSehat is not null) simpan.Add(terbaruSehat.Path);

        foreach (var e in entries)
            if ((sekarangUtc - e.CreatedAtUtc).TotalDays <= policy.HariSimpanSemua)
                simpan.Add(e.Path);

        SimpanPeriode(entries, simpan, policy.HariHarian, e => e.CreatedAtUtc.ToLocalTime().Date);
        SimpanPeriode(entries, simpan, policy.MingguMingguan,
            e => AwalMinggu(e.CreatedAtUtc.ToLocalTime().Date));
        SimpanPeriode(entries, simpan, policy.BulanBulanan,
            e => new DateTime(e.CreatedAtUtc.ToLocalTime().Year, e.CreatedAtUtc.ToLocalTime().Month, 1));

        // Pagar terakhir: jangan pernah menyisakan kurang dari batas minimum, walau semua
        // jendela sudah menipis. Yang ditambahkan adalah salinan terbaru berikutnya.
        if (simpan.Count < policy.MinimalSalinan)
            foreach (var e in entries.Where(e => !simpan.Contains(e.Path)))
            {
                if (simpan.Count >= policy.MinimalSalinan) break;
                simpan.Add(e.Path);
            }

        var dihapus = new List<string>();
        foreach (var e in entries.Where(e => !simpan.Contains(e.Path)))
        {
            try
            {
                File.Delete(e.Path);
                var manifes = BackupManifestFile.PathUntuk(e.Path);
                if (File.Exists(manifes)) File.Delete(manifes);
                dihapus.Add(e.Path);
                Catat(directory, log,
                    $"hapus {e.FileName} ({e.Kind}, dibuat {e.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}, " +
                    $"{e.SizeBytes / 1024.0 / 1024.0:0.0} MB): di luar jendela retensi " +
                    $"(semua {policy.HariSimpanSemua} hari, harian {policy.HariHarian}, mingguan {policy.MingguMingguan}, " +
                    $"bulanan {policy.BulanBulanan}) dan bukan salinan terbaru yang sehat.");
            }
            catch (IOException ex)
            {
                Catat(directory, log, $"gagal menghapus {e.FileName}: {ex.Message}; berkas dibiarkan.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Catat(directory, log, $"gagal menghapus {e.FileName}: {ex.Message}; berkas dibiarkan.");
            }
        }

        foreach (var e in entries.Where(e => simpan.Contains(e.Path)))
            log?.Invoke($"simpan {e.FileName} ({e.Kind}, {e.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}).");

        return new RetentionDecision(
            entries.Where(e => simpan.Contains(e.Path)).Select(e => e.FileName).ToList(),
            dihapus.Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList());
    }

    /// <summary>Menyimpan satu salinan tertua per periode, sebanyak batas periode yang diminta.</summary>
    private static void SimpanPeriode(
        IReadOnlyList<BackupEntry> entries, HashSet<string> simpan, int batas, Func<BackupEntry, DateTime> periode)
    {
        if (batas <= 0) return;
        var terpakai = new HashSet<DateTime>();
        // entries sudah urut terbaru lebih dulu: salinan pertama yang ditemui untuk sebuah
        // periode adalah yang termuda di periode itu, jadi yang paling berguna disimpan.
        foreach (var e in entries)
        {
            if (terpakai.Count >= batas) break;
            var kunci = periode(e);
            if (!terpakai.Add(kunci)) continue;
            simpan.Add(e.Path);
        }
    }

    private static DateTime AwalMinggu(DateTime hari) =>
        hari.AddDays(-(((int)hari.DayOfWeek + 6) % 7));

    private static void Catat(string directory, Action<string>? log, string pesan)
    {
        log?.Invoke(pesan);
        try
        {
            File.AppendAllText(Path.Combine(directory, LogFile),
                $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss} {pesan}{Environment.NewLine}");
        }
        catch (IOException) { /* catatan retensi tidak boleh menggagalkan pembersihan */ }
        catch (UnauthorizedAccessException) { }
    }
}
