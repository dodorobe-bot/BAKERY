using System.Globalization;
using System.Text;

namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Tanggal bisnis mengikuti zona waktu operasi toko, bukan UTC: tanpa ini,
/// "hari ini" dan "jatuh tempo" salah selama 00:00-07:59 WIB.
///
/// Zona waktunya dibaca dari pengaturan aplikasi saat layanan mulai, supaya kunci
/// 'timezone' di AppSettings benar-benar menentukan perhitungan dan bukan hiasan.
/// </summary>
public static class BusinessTime
{
    public const string ZoneIdBawaan = "Asia/Jakarta";

    private static TimeZoneInfo _zone = Resolve(ZoneIdBawaan);

    public static TimeZoneInfo Zone => _zone;

    /// <summary>Nama zona yang sedang berlaku, untuk ditampilkan kembali di layar pengaturan.</summary>
    public static string ZonaAktif => _zone.Id;

    private static TimeZoneInfo Resolve(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>
    /// Memasang zona waktu operasi. Nilai kosong memuat bawaan; nama yang tidak dikenal
    /// tidak dipakai diam-diam — pesan galatnya dikembalikan supaya penulis pengaturan
    /// tahu bahwa "hari ini" aplikasinya belum berubah.
    /// </summary>
    public static string? KonfigurasikanZona(string? zoneId)
    {
        var id = string.IsNullOrWhiteSpace(zoneId) ? ZoneIdBawaan : zoneId.Trim();
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return null;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"Zona waktu '{id}' tidak dikenal mesin ini; perhitungan tanggal tetap memakai '{_zone.Id}'.";
        }
    }

    /// <summary>
    /// Menerjemahkan cap waktu UTC ke waktu toko. Dipisah supaya batas hari bisa diuji dengan
    /// waktu tetap: 23:59 dan 00:01 WIB harus jatuh ke tanggal yang berbeda walau UTC-nya
    /// masih tanggal yang sama.
    /// </summary>
    public static DateTime DariUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    public static DateTime Now() => DariUtc(DateTime.UtcNow);

    public static DateTime Today() => Now().Date;

    /// <summary>
    /// Awal hari kalender toko dalam UTC. Cap waktu kejadian ditulis UTC, jadi rentang
    /// laporan yang dipilih pemilik sebagai tanggal toko harus diterjemahkan dulu ke batas
    /// UTC; tanpa ini penjualan pukul 01:00 WIB jatuh ke tanggal UTC kemarin.
    /// </summary>
    public static DateTime AwalHariUtc(DateTime tanggalToko)
    {
        var lokal = DateTime.SpecifyKind(tanggalToko.Date, DateTimeKind.Unspecified);
        // Zona dengan pergantian DST tepat tengah malam bisa membuat 00:00 tidak ada di kalender.
        while (_zone.IsInvalidTime(lokal)) lokal = lokal.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(lokal, _zone);
    }

    /// <summary>
    /// Membaca cap waktu dari basis data. Kolom lama di beberapa tabel ditulis sebagai waktu
    /// toko tanpa penanda zona; nilai seperti itu dianggap waktu toko supaya riwayat lama tidak
    /// bergeser tujuh jam. Nilai berzona (Z atau offset) dikembalikan sebagai UTC.
    /// </summary>
    public static DateTime BacaCapWaktu(string teks)
    {
        if (!DateTime.TryParse(teks, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var waktu))
            return DateTime.MinValue;
        if (waktu.Kind == DateTimeKind.Utc) return waktu;
        if (waktu.Kind == DateTimeKind.Local) return waktu.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(waktu, DateTimeKind.Unspecified), _zone);
    }
}

/// <summary>
/// Nilai uang disimpan dalam satuan minor (sen = Rp x 100) di seluruh kontrak API,
/// jadi setiap pembacaan untuk tampilan harus dibagi 100.
/// </summary>
public static class Money
{
    public static decimal ToRupiah(long minorUnits) => minorUnits / 100m;

    /// <summary>Rp 1.500.000. Dipformat manual agar tidak bergantung pada ICU yang tersedia.</summary>
    public static string Format(long minorUnits)
    {
        var rupiah = Math.Round(ToRupiah(minorUnits), 0, MidpointRounding.AwayFromZero);
        var digits = Math.Abs(rupiah).ToString("0", CultureInfo.InvariantCulture);
        var grouped = new StringBuilder(digits.Length + digits.Length / 3 + 3);
        if (rupiah < 0) grouped.Append('-');
        grouped.Append("Rp ");
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) grouped.Append('.');
            grouped.Append(digits[i]);
        }
        return grouped.ToString();
    }
}
