using System.Text.RegularExpressions;

namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Identitas toko yang tampil di layar masuk, bilah samping, struk, dan ekspor. Nilainya hidup
/// di AppSettings supaya pemilik bisa mengganti nama, slogan, dan logo kapan saja tanpa paket baru;
/// nama bawaan hanya dipakai bila pemilik belum pernah mengisi.
/// </summary>
public static class StoreBrand
{
    public const string NameKey = "brand_name";
    public const string TaglineKey = "brand_tagline";
    public const string LogoKey = "brand_logo";

    public const string DefaultName = "Seruni 22 Bakery";
    public const string DefaultTagline = "Sistem Manajemen Toko Kue";

    public const int MaxNameLength = 40;
    public const int MaxTaglineLength = 80;

    // Logo disimpan sebagai data URL di dalam AppSettings. Batas ini menjaga satu baris
    // pengaturan tetap wajar sehingga pajak muatan /api/settings tidak membengkak dan
    // salinan basis data tidak melonjak karena satu gambar.
    public const int MaxLogoBytes = 512 * 1024;
    private const int MaxLogoDataUrlLength = MaxLogoBytes / 3 * 4 + 128;

    private static readonly Regex LogoPattern = new(
        @"^data:image/(?<jenis>png|jpeg|webp);base64,(?<isi>[A-Za-z0-9+/]+={0,2})$",
        RegexOptions.CultureInvariant);

    public static string Name(string? tersimpan) =>
        string.IsNullOrWhiteSpace(tersimpan) ? DefaultName : tersimpan.Trim();

    public static string Tagline(string? tersimpan) =>
        string.IsNullOrWhiteSpace(tersimpan) ? DefaultTagline : tersimpan.Trim();

    /// <summary>
    /// Mengembalikan pesan galat untuk nilai merek yang tidak sah, atau null bila sah.
    /// Kunci selain tiga kunci merek tidak diperiksa di sini.
    /// </summary>
    public static string? Validate(string key, string? value)
    {
        return key switch
        {
            NameKey => ValidateText(value, MaxNameLength, "Nama toko", wajib: true),
            TaglineKey => ValidateText(value, MaxTaglineLength, "Slogan toko", wajib: false),
            LogoKey => ValidateLogo(value),
            _ => null,
        };
    }

    private static string? ValidateText(string? nilai, int panjangMaks, string label, bool wajib)
    {
        var teks = (nilai ?? string.Empty).Trim();
        if (wajib && teks.Length == 0)
            return $"{label} tidak boleh kosong.";
        if (teks.Length > panjangMaks)
            return $"{label} maksimal {panjangMaks} karakter.";
        if (teks.Any(char.IsControl))
            return $"{label} tidak boleh memuat karakter kendali.";
        return null;
    }

    private static string? ValidateLogo(string? nilai)
    {
        // Kosong berarti "kembali ke logo bawaan", bukan galat.
        if (string.IsNullOrWhiteSpace(nilai)) return null;
        if (nilai.Length > MaxLogoDataUrlLength)
            return $"Logo terlalu besar. Maksimal {MaxLogoBytes / 1024} KB.";

        var cocok = LogoPattern.Match(nilai);
        if (!cocok.Success)
            return "Logo harus berupa gambar PNG, JPEG, atau WebP.";

        byte[] isi;
        try
        {
            isi = Convert.FromBase64String(cocok.Groups["isi"].Value);
        }
        catch (FormatException)
        {
            return "Isi logo bukan data gambar yang sah.";
        }

        if (isi.Length == 0) return "Isi logo kosong.";
        if (isi.Length > MaxLogoBytes)
            return $"Logo terlalu besar. Maksimal {MaxLogoBytes / 1024} KB.";

        // Jenis di data URL harus cocok dengan byte awalnya. Tanpa pemeriksaan ini, berkas apa pun
        // bisa menyamar sebagai gambar dan peramban baru menolaknya saat dirender.
        var jenis = cocok.Groups["jenis"].Value;
        var sah = jenis switch
        {
            "png" => Cocok(isi, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            "jpeg" => Cocok(isi, [0xFF, 0xD8, 0xFF]),
            "webp" => isi.Length >= 12
                      && Cocok(isi, "RIFF"u8.ToArray())
                      && isi[8] == (byte)'W' && isi[9] == (byte)'E' && isi[10] == (byte)'B' && isi[11] == (byte)'P',
            _ => false,
        };
        return sah ? null : "Isi gambar tidak cocok dengan jenis yang dinyatakan.";
    }

    private static bool Cocok(byte[] isi, ReadOnlySpan<byte> awalan) =>
        isi.Length >= awalan.Length && isi.AsSpan(0, awalan.Length).SequenceEqual(awalan);
}
