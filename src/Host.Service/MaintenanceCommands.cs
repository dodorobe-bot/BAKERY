using System.Diagnostics;
using Serilog;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Host;

/// <summary>
/// Perintah pemeliharaan dijalankan sebelum host web menyala, karena pemulihan tidak boleh
/// berjalan saat masih ada permintaan yang memakai basis data. Keluar dengan kode ≠ 0 saat gagal
/// supaya skrip/administrator bisa membedakan berhasil dan tidak.
/// </summary>
internal static class MaintenanceCommands
{
    /// <summary>
    /// Membaca <c>--Database:Path=...</c> langsung dari argumen. Provider konfigurasi baris
    /// perintah menelan token sesudah saklar tanpa nilai, jadi pada
    /// <c>--backup --Database:Path=X</c> path itu hilang dan perintah bekerja pada basis data
    /// bawaan — cadangan/pemulihan bisa mengenai basis data yang salah tanpa peringatan.
    /// </summary>
    internal static string? PathBasisDataDariArgumen(string[] args)
    {
        const string awalan = "--Database:Path=";
        foreach (var arg in args)
        {
            if (arg.StartsWith(awalan, StringComparison.OrdinalIgnoreCase))
            {
                var nilai = arg[awalan.Length..].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(nilai)) return nilai;
            }
        }
        return null;
    }

    public static bool TryRun(string[] args, string dbPath, string dataRoot)
    {
        var backupRequested = args.Contains("--backup", StringComparer.Ordinal);
        var restoreIndex = Array.IndexOf(args, "--restore");
        if (!backupRequested && restoreIndex < 0) return false;

        var dbDirectory = Path.GetDirectoryName(dbPath);
        var backupDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(dbDirectory) ? dataRoot : dbDirectory, "backups");
        var service = new DatabaseBackupService();

        if (backupRequested)
        {
            try
            {
                var path = service.CreateBackup(dbPath, backupDirectory, "manual",
                    pesan => Log.Information("{Pesan}", pesan));
                var keputusan = BackupRetention.Terapkan(backupDirectory, new RetentionPolicy(),
                    log: pesan => Log.Information("[Retensi] {Pesan}", pesan));
                Log.Information("Cadangan dibuat: {Path} (retensi: {Disimpan} disimpan, {Dihapus} dihapus)",
                    path, keputusan.Disimpan.Count, keputusan.Dihapus.Count);
                Console.WriteLine(path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Pembuatan cadangan gagal");
                Environment.ExitCode = 1;
            }
            return true;
        }

        var backupPath = restoreIndex + 1 < args.Length ? args[restoreIndex + 1] : null;
        if (string.IsNullOrWhiteSpace(backupPath) || backupPath.StartsWith("--", StringComparison.Ordinal))
        {
            Log.Error("Perintah --restore membutuhkan path berkas cadangan.");
            Environment.ExitCode = 2;
            return true;
        }
        if (!File.Exists(backupPath))
        {
            Log.Error("Berkas cadangan tidak ditemukan: {Path}", backupPath);
            Environment.ExitCode = 2;
            return true;
        }

        var otherInstances = Process.GetProcessesByName("Seruni22Bakery.Host")
            .Where(p => p.Id != Environment.ProcessId)
            .ToList();
        if (otherInstances.Count > 0)
        {
            Log.Error("Aplikasi masih berjalan (PID {Pid}). Tutup aplikasi sebelum memulihkan.",
                string.Join(", ", otherInstances.Select(p => p.Id)));
            Environment.ExitCode = 3;
            return true;
        }

        try
        {
            service.Restore(backupPath, dbPath, backupDirectory, message => Log.Information("{Pesan}", message));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Pemulihan gagal");
            Environment.ExitCode = 1;
        }
        return true;
    }
}
