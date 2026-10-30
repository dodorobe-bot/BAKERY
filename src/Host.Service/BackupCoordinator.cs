using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Host;

/// <summary>
/// Penyelenggara cadangan di sisi host: pembuatan berkala, retensi, penyalinan ke luar
/// komputer, dan penilaian kapasitas. Semua keputusan diambil dari pengaturan yang tersimpan,
/// sehingga pemilik bisa mengubah jadwal tanpa membangun ulang aplikasi.
/// </summary>
public sealed class BackupCoordinator
{
    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly string _logDir;
    private readonly IServiceScopeFactory _scope;
    private readonly DbConnectionFactory _factory;
    private readonly DatabaseBackupService _service = new();

    /// <summary>Pembuatan cadangan tidak boleh berjalan dua kali bersamaan: dua berkas
    /// cadangan yang saling menimpa atau retensi yang menghapus berkas saudaranya.</summary>
    private readonly SemaphoreSlim _kunci = new(1, 1);

    public BackupCoordinator(
        string dbPath, string backupDir, string logDir, DbConnectionFactory factory, IServiceScopeFactory scope)
    {
        _dbPath = dbPath;
        _backupDir = backupDir;
        _logDir = logDir;
        _factory = factory;
        _scope = scope;
    }

    public string DatabasePath => _dbPath;
    public string BackupDirectory => _backupDir;

    public IReadOnlyList<BackupEntry> Daftar() => BackupCatalog.Baca(_backupDir);

    public async Task<StorageSnapshot> PotretAsync(CancellationToken ct = default)
    {
        var pengaturan = await BacaPengaturanAsync();
        return StorageMonitor.Evaluate(_dbPath, _backupDir, _logDir, StorageThresholds.DariPengaturan(pengaturan));
    }

    public async Task<BackupManifest> BuatSekarangAsync(string label)
    {
        await _kunci.WaitAsync();
        try
        {
            var pengaturan = await BacaPengaturanAsync();
            var path = _service.CreateBackup(_dbPath, _backupDir, label, pesan => Log.Information("{Pesan}", pesan));
            var keputusan = BackupRetention.Terapkan(
                _backupDir, RetentionPolicy.DariPengaturan(pengaturan), log: pesan => Log.Information("[Retensi] {Pesan}", pesan));
            Log.Information("Retensi cadangan: {Disimpan} disimpan, {Dihapus} dihapus.",
                keputusan.Disimpan.Count, keputusan.Dihapus.Count);

            var manifes = BackupManifestFile.Baca(path)
                ?? throw new InvalidOperationException("Cadangan terbit tanpa manifes yang bisa dibaca.");
            return manifes;
        }
        finally { _kunci.Release(); }
    }

    /// <summary>
    /// Menyalin cadangan terverifikasi terbaru beserta manifesnya ke folder lain (misalnya
    /// flash disk). Salinan diperiksa ulang di tujuan; salinan yang tidak lolos checksum
    /// dihapus dan dilaporkan gagal, karena "sudah disalin" bukan bukti bisa dipulihkan.
    /// </summary>
    public async Task<(string Folder, string Berkas, string Sha256)> SalinKeluarAsync(string tujuan)
    {
        var folder = BackupDestination.Validasi(tujuan, _backupDir);

        await _kunci.WaitAsync();
        try
        {
            var sumber = BackupCatalog.Baca(_backupDir).FirstOrDefault(c => c.Terverifikasi)
                ?? throw new InvalidOperationException("Belum ada cadangan terverifikasi untuk disalin.");
            if (sumber.Sha256 is null)
                throw new InvalidOperationException("Cadangan terbaru tidak memiliki sidik SHA-256; buat cadangan baru lebih dulu.");

            var target = Path.Combine(folder, sumber.FileName);
            var manifesTarget = BackupManifestFile.PathUntuk(target);
            try
            {
                Directory.CreateDirectory(folder);
                var temp = target + ".tmp";
                File.Copy(sumber.Path, temp, overwrite: true);
                File.Move(temp, target, overwrite: true);
                File.Copy(BackupManifestFile.PathUntuk(sumber.Path), manifesTarget, overwrite: true);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(
                    $"Folder tujuan tidak bisa ditulis: {folder}. Pilih folder lain atau jalankan ulang sebagai pengguna yang berhak. ({ex.Message})");
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"Gagal menyalin ke {folder}: perangkat penuh, terlepas, atau berkasnya sedang dipakai. ({ex.Message})");
            }

            if (!BackupManifestFile.ChecksumCocok(target, sumber.Sha256))
            {
                TryDelete(target);
                TryDelete(manifesTarget);
                throw new InvalidOperationException(
                    "Salinan di folder tujuan tidak lolos pemeriksaan checksum; salinan dihapus. " +
                    "Coba lagi dengan flash disk/perangkat lain.");
            }

            Log.Information("Cadangan disalin ke {Tujuan} ({Berkas}, sha256 cocok).", folder, sumber.FileName);
            return (folder, sumber.FileName, sumber.Sha256);
        }
        finally { _kunci.Release(); }
    }

    /// <summary>
    /// Membuat cadangan otomatis bila jadwalnya sudah lewat. Basis data kosong tidak
    /// dicadangkan: tidak ada data usaha yang perlu dilindungi dan folder cadangan penuh
    /// berkas kosong justru menyulitkan pemulihan.
    /// </summary>
    public async Task AutoAsync(CancellationToken ct)
    {
        var pengaturan = await BacaPengaturanAsync();
        if (!Aktif(pengaturan, "backup.auto.enabled", true)) return;

        var interval = Jam(pengaturan, "backup.auto.interval_hours", 24);
        var terakhir = BackupCatalog.Baca(_backupDir).FirstOrDefault(e => e.Kind == BackupKind.Otomatis);
        if (terakhir is not null && DateTime.UtcNow - terakhir.CreatedAtUtc < TimeSpan.FromHours(interval)) return;
        if (!await AdaDataAsync(ct)) return;

        try
        {
            var manifes = await BuatSekarangAsync("auto");
            Log.Information("Cadangan otomatis selesai: {File} ({Orders} pesanan, {Jurnal} entri jurnal).",
                manifes.File, manifes.Orders, manifes.JournalEntries);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cadangan otomatis gagal");
            await KirimAsync(
                "backup.auto.gagal", NotificationSeverity.Critical,
                "Cadangan otomatis gagal",
                $"Penyebab: {ex.Message}. Buat cadangan manual dari Pengaturan dan periksa folder cadangan.",
                $"backup.auto.gagal:{BusinessTime.Today():yyyy-MM-dd}");
        }
    }

    /// <summary>Menilai kapasitas dan mengirim satu peringatan internal per tingkat per hari.</summary>
    public async Task<StorageSnapshot> CekKapasitasAsync(CancellationToken ct = default)
    {
        var potret = await PotretAsync(ct);
        if (potret.Level != StorageMonitor.Normal)
            await KirimAsync(
                $"storage.{potret.Level}",
                potret.Level == StorageMonitor.Critical ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                potret.Level == StorageMonitor.Critical
                    ? "Ruang penyimpanan kritis"
                    : "Ruang penyimpanan perlu perhatian",
                string.Join("; ", potret.Reasons) + ". Lihat Pengaturan → Cadangan & Penyimpanan.",
                $"storage.level:{potret.Level}:{BusinessTime.Today():yyyy-MM-dd}");
        return potret;
    }

    private async Task<bool> AdaDataAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM SalesOrders) + (SELECT COUNT(*) FROM JournalEntries);";
            var jumlah = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            return jumlah > 0;
        }
        catch (SqliteException ex)
        {
            Log.Warning(ex, "Tidak bisa memeriksa isi basis data sebelum cadangan otomatis");
            return false;
        }
    }

    private async Task<Dictionary<string, string>> BacaPengaturanAsync()
    {
        using var scope = _scope.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        return new Dictionary<string, string>(await repo.GetAllAsync(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task KirimAsync(string jenis, NotificationSeverity tingkat, string judul, string isi, string kunci)
    {
        try
        {
            using var scope = _scope.CreateScope();
            var notifikasi = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await notifikasi.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin],
                new KirimNotifikasi(jenis, tingkat, judul, isi, DedupeKey: kunci),
                DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            // Notifikasi yang gagal tidak boleh menggagalkan pekerjaan yang sudah berhasil.
            Log.Warning(ex, "Notifikasi {Jenis} tidak terkirim", jenis);
        }
    }

    private static bool Aktif(IReadOnlyDictionary<string, string> set, string kunci, bool bawaan) =>
        !set.TryGetValue(kunci, out var teks)
            ? bawaan
            : teks.Trim() is not ("0" or "false" or "off" or "tidak");

    private static int Jam(IReadOnlyDictionary<string, string> set, string kunci, int bawaan) =>
        set.TryGetValue(kunci, out var teks) && int.TryParse(teks, out var nilai) && nilai > 0 ? nilai : bawaan;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

/// <summary>
/// Pemeriksa latar: cadangan otomatis dan kapasitas penyimpanan. Berjalan setelah aplikasi
/// siap supaya tidak memperlambat pembukaan, lalu mengulang setiap setengah jam.
/// </summary>
public sealed class PemeliharaanService : BackgroundService
{
    private readonly BackupCoordinator _koordinator;

    public PemeliharaanService(BackupCoordinator koordinator) => _koordinator = koordinator;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _koordinator.AutoAsync(stoppingToken); }
            catch (Exception ex) { Log.Error(ex, "Pemeriksaan cadangan otomatis gagal"); }

            try { await _koordinator.CekKapasitasAsync(stoppingToken); }
            catch (Exception ex) { Log.Error(ex, "Pemeriksaan kapasitas gagal"); }

            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
