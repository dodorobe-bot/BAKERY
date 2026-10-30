namespace Seruni22Bakery.Infrastructure.Sqlite.Database;

/// <summary>
/// Pemeriksaan folder tujuan salinan cadangan. Dipisahkan dari host supaya larangan
/// ".." dan path relatif bisa diuji langsung tanpa menjalankan web server.
/// </summary>
public static class BackupDestination
{
    /// <summary>
    /// Mengembalikan path penuh folder tujuan atau melempar ArgumentException bila masukan
    /// tidak layak. ".." diperiksa pada teks masukan, bukan setelah GetFullPath: normalisasi
    /// sudah menghapus ".." sehingga larangannya tidak lagi terlihat.
    /// </summary>
    public static string Validasi(string? tujuan, string backupDirectory)
    {
        var mentah = (tujuan ?? string.Empty).Trim();
        if (mentah.Length == 0)
            throw new ArgumentException("Folder tujuan belum diisi.");
        if (!Path.IsPathRooted(mentah))
            throw new ArgumentException("Folder tujuan harus berupa path penuh, misalnya D:\\Cadangan Seruni.");
        if (mentah.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Folder tujuan tidak boleh memakai '..'.");
        if (mentah.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("Folder tujuan memuat karakter yang tidak sah.");

        var folder = Path.GetFullPath(mentah);
        if (folder.TrimEnd(Path.DirectorySeparatorChar)
            .Equals(backupDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Folder tujuan sama dengan folder cadangan internal; salinan ke luar tidak akan terbuat.");

        return folder;
    }
}
