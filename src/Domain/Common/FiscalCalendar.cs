using System.Globalization;

namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Satu bulan kalender buku. Rentang disimpan sebagai tanggal toko (bukan waktu) karena kolom
/// TransactionDate di basis data juga tanggal toko murni dalam format yyyy-MM-dd, sehingga
/// perbandingan di pemicu cukup pencocokan teks.
/// </summary>
public sealed record FiscalMonth(string Name, DateTime StartDate, DateTime EndDate)
{
    public string StartText => StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string EndText => EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string Key => StartDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

/// <summary>
/// Perhitungan periode buku dan aturan tanggal yang bisa dipilih pengguna. Sengaja di Domain:
/// layar, layanan, dan pemicu basis data harus memakai batas yang sama supaya pesan penolakan
/// tidak berbeda-beda tergantung jalur mana yang lebih dulu menahan.
/// </summary>
public static class FiscalCalendar
{
    /// <summary>Sama dengan batas tanggal laporan di jalur MCP; cukup untuk koreksi satu tahun ke belakang.</summary>
    public const int MaxHistoryDays = 366;

    private static readonly string[] NamaBulan =
    [
        "Januari", "Februari", "Maret", "April", "Mei", "Juni",
        "Juli", "Agustus", "September", "Oktober", "November", "Desember"
    ];

    public static FiscalMonth MonthOf(DateTime tanggal)
    {
        var awal = new DateTime(tanggal.Year, tanggal.Month, 1);
        return new FiscalMonth($"{NamaBulan[tanggal.Month - 1]} {tanggal.Year}", awal, awal.AddMonths(1).AddDays(-1));
    }

    /// <summary>Menerima "yyyy-MM"; format lain ditolak supaya tidak ada bulan yang salah tutup.</summary>
    public static bool TryParseMonth(string? teks, out FiscalMonth? bulan)
    {
        bulan = null;
        if (string.IsNullOrWhiteSpace(teks)) return false;
        if (!DateTime.TryParseExact(teks.Trim(), "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;
        bulan = MonthOf(parsed);
        return true;
    }

    /// <summary>Menerima "yyyy-MM-dd"; dipakai layar yang mengirim tanggal sebagai teks biasa.</summary>
    public static bool TryParseDate(string? teks, out DateTime tanggal)
    {
        tanggal = default;
        if (string.IsNullOrWhiteSpace(teks)) return false;
        return DateTime.TryParseExact(teks.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out tanggal);
    }

    /// <summary>
    /// Tanggal biaya: kosong berarti hari bisnis hari ini, tidak boleh ke masa depan, dan paling
    /// lama 366 hari ke belakang. Salah ketik tahun adalah kegagalan yang paling sering terjadi
    /// pada kolom tanggal, jadi angkanya ikut ditulis di pesan supaya pengguna melihat tahun mana
    /// yang sedang dipakai sistem.
    /// </summary>
    public static bool TryExpenseDate(string? teks, out DateTime tanggal, out string? galat)
    {
        var hariIni = BusinessTime.Today();
        galat = null;

        if (string.IsNullOrWhiteSpace(teks))
        {
            tanggal = hariIni;
            return true;
        }

        if (!TryParseDate(teks, out tanggal))
        {
            galat = $"Tanggal biaya harus berformat YYYY-MM-DD, bukan \"{teks?.Trim()}\".";
            return false;
        }

        if (tanggal > hariIni)
        {
            galat = $"Tanggal biaya {tanggal:yyyy-MM-dd} melewati hari bisnis hari ini ({hariIni:yyyy-MM-dd}); " +
                    "beban belum bisa dicatat untuk masa depan.";
            return false;
        }

        if (tanggal < hariIni.AddDays(-MaxHistoryDays))
        {
            galat = $"Tanggal biaya {tanggal:yyyy-MM-dd} lebih tua dari {MaxHistoryDays} hari ke belakang " +
                    $"(batas {hariIni.AddDays(-MaxHistoryDays):yyyy-MM-dd}); periksa apakah tahunnya salah ketik.";
            return false;
        }

        return true;
    }
}
