using System.Security.Cryptography;
using System.Text.Json;

namespace Seruni22Bakery.Infrastructure.Sqlite.Database;

/// <summary>
/// Jenis cadangan dibedakan karena retensi memperlakukannya berbeda: cadangan pra-upgrade
/// dan pra-restore adalah jalan pulang terakhir dan tidak boleh ikut terjadwal hapus.
/// </summary>
public static class BackupKind
{
    public const string Manual = "manual";
    public const string Otomatis = "otomatis";
    public const string PraUpgrade = "pre-upgrade";
    public const string PraRestore = "pre-restore";
    public const string TanpaManifes = "tanpa-manifes";

    public static string DariLabel(string label) =>
        label.StartsWith("pre-upgrade", StringComparison.OrdinalIgnoreCase) ? PraUpgrade
        : label.StartsWith("pre-restore", StringComparison.OrdinalIgnoreCase) ? PraRestore
        : label.StartsWith("auto", StringComparison.OrdinalIgnoreCase) ? Otomatis
        : Manual;
}

/// <summary>
/// Catatan yang menemani setiap berkas cadangan. Tanpa catatan ini sebuah berkas hanya bisa
/// "dibuka dan mudah-mudahan benar"; dengan catatan ini ukuran, sidik SHA-256, versi skema,
/// dan hasil pemeriksaan SQLite bisa dibandingkan sebelum pemulihan menyentuh data berlaku.
/// </summary>
public sealed record BackupManifest(
    string Format,
    string File,
    long SizeBytes,
    string Sha256,
    DateTime CreatedAtUtc,
    string Kind,
    string Label,
    int SchemaVersion,
    string AppVersion,
    string Integrity,
    string ForeignKeys,
    long Orders,
    long JournalEntries,
    long StockMovements)
{
    public const string FormatVersion = "seruni22-backup/1";

    public bool Sehat =>
        string.Equals(Integrity, DatabaseBackupService.IntegrityOk, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ForeignKeys, DatabaseBackupService.ForeignKeysOk, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Membaca/menulis manifes dan menghitung sidik berkas cadangan.</summary>
public static class BackupManifestFile
{
    public const string Ekstensi = ".json";

    private static readonly JsonSerializerOptions Opsi = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string PathUntuk(string backupPath) => backupPath + Ekstensi;

    /// <summary>
    /// Ditulis lewat berkas sementara lalu diganti namanya, supaya pemadaman listrik di tengah
    /// penulisan tidak meninggalkan manifes setengah jadi yang membuat cadangan tampak rusak.
    /// </summary>
    public static void Tulis(string backupPath, BackupManifest manifest)
    {
        var path = PathUntuk(backupPath);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, Opsi));
        File.Move(temp, path, overwrite: true);
    }

    public static BackupManifest? Baca(string backupPath)
    {
        var path = PathUntuk(backupPath);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<BackupManifest>(
                File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public static bool ChecksumCocok(string path, string? sha256)
    {
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(sha256)) return false;
        return string.Equals(Sha256(path), sha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

/// <summary>Satu berkas cadangan yang ditemukan di folder cadangan.</summary>
public sealed record BackupEntry(
    string Path,
    string FileName,
    long SizeBytes,
    DateTime CreatedAtUtc,
    string Kind,
    string Label,
    int SchemaVersion,
    string? Sha256,
    bool PunyaManifes,
    bool Terverifikasi);

/// <summary>
/// Daftar cadangan di sebuah folder. Ukuran dibandingkan dengan manifes tanpa menghitung ulang
/// seluruh sidik: pencocokan sidik penuh dilakukan saat cadangan benar-benar dipulihkan atau
/// diperiksa, bukan setiap kali daftar dibuka.
/// </summary>
public static class BackupCatalog
{
    public static IReadOnlyList<BackupEntry> Baca(string directory)
    {
        if (!Directory.Exists(directory)) return [];

        var daftar = new List<BackupEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.db"))
        {
            if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

            var info = new FileInfo(path);
            var manifes = BackupManifestFile.Baca(path);
            var terverifikasi = manifes is not null
                && manifes.SizeBytes == info.Length
                && manifes.Sehat;

            daftar.Add(new BackupEntry(
                path,
                info.Name,
                info.Length,
                manifes?.CreatedAtUtc ?? info.LastWriteTimeUtc,
                manifes?.Kind ?? BackupKind.TanpaManifes,
                manifes?.Label ?? System.IO.Path.GetFileNameWithoutExtension(path),
                manifes?.SchemaVersion ?? 0,
                manifes?.Sha256,
                manifes is not null,
                terverifikasi));
        }

        return daftar
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.FileName, StringComparer.Ordinal)
            .ToList();
    }
}
