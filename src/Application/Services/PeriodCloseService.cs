using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <summary>
/// Menutup dan membuka kembali bulan buku. Yang diubah hanya status satu baris FiscalPeriods:
/// tidak ada jurnal yang dihapus, dipindah, atau dinormalisasi, jadi tindakan ini selalu bisa
/// dibatalkan dan laporan periode tertutup tetap terbaca sama seperti sebelumnya.
///
/// Pagar yang sesungguhnya menahan penulisan ada di basis data (migrasi 022) dan di penulis
/// jurnal, supaya sisipan yang melewati aplikasi tetap kena. Layanan ini menjaga agar perintah
/// tutup itu sendiri tidak dilakukan pada bulan yang salah.
/// </summary>
public class PeriodCloseService
{
    private readonly IFiscalPeriodRepository _periods;

    public PeriodCloseService(IFiscalPeriodRepository periods) => _periods = periods;

    public async Task<IReadOnlyList<FiscalPeriodSummary>> ListAsync() => await _periods.ListAsync();

    public async Task<Result<FiscalPeriodSummary>> CloseAsync(string? monthText, int userId)
    {
        var bulan = ParseMonth(monthText);
        if (bulan is null)
            return Result<FiscalPeriodSummary>.Fail(ErrorCodes.ValidationFailed,
                $"Bulan harus berformat YYYY-MM, bukan \"{monthText?.Trim()}\".");

        var hariIni = BusinessTime.Today();
        if (bulan.StartDate > hariIni)
            return Result<FiscalPeriodSummary>.Fail(ErrorCodes.ValidationFailed,
                $"{bulan.Name} baru akan dimulai {bulan.StartText:yyyy-MM-dd}; periode yang belum berjalan " +
                "tidak perlu ditutup karena belum ada yang bisa dikunci.");

        var semua = await _periods.ListAsync();
        var ada = semua.FirstOrDefault(p => Mulai(p.StartDate) == bulan.StartDate);

        if (ada?.IsCurrent == true)
            return Result<FiscalPeriodSummary>.Fail(ErrorCodes.ValidationFailed,
                $"{bulan.Name} masih berjalan — hari bisnis hari ini ({hariIni:yyyy-MM-dd}) ada di dalamnya. " +
                "Tutup setelah bulan berganti, kalau memang laporan bulan ini sudah final.");

        if (ada is not null && ada.Status != "Open")
            return Result<FiscalPeriodSummary>.Fail(ErrorCodes.ValidationFailed,
                $"{bulan.Name} sudah berstatus {StatusLabel(ada.Status)}, jadi tidak perlu ditutup lagi.");

        // Bulan yang tidak muncul di daftar tidak punya satu pun jurnal. Menutupnya tetap sah —
        // pemilik yang sedang merapikan buku boleh mengunci bulan kosong — dan barisnya dibuat di
        // sini supaya riwayat siapa yang menutup tersimpan seperti pada bulan berisi transaksi.
        await _periods.UpsertStatusAsync(bulan.Name, bulan.StartText, bulan.EndText, "Closed", userId);

        var sesudah = await CariAsync(bulan.StartDate);
        return Result<FiscalPeriodSummary>.Ok(sesudah ?? throw new InvalidOperationException(
            $"Periode {bulan.Name} sudah ditandai tertutup tetapi tidak kembali dari daftar periode."));
    }

    public async Task<Result<FiscalPeriodSummary>> ReopenAsync(string? monthText, int userId)
    {
        var bulan = ParseMonth(monthText);
        if (bulan is null)
            return Result<FiscalPeriodSummary>.Fail(ErrorCodes.ValidationFailed,
                $"Bulan harus berformat YYYY-MM, bukan \"{monthText?.Trim()}\".");

        var semua = await _periods.ListAsync();
        var ada = semua.FirstOrDefault(p => Mulai(p.StartDate) == bulan.StartDate);

        if (ada is null || ada.Status == "Open")
            return Result<FiscalPeriodSummary>.Fail(ErrorCodes.ValidationFailed,
                $"{bulan.Name} tidak berstatus tertutup, jadi tidak ada pagar yang perlu dibuka.");

        if (ada.Status == "Locked")
            return Result<FiscalPeriodSummary>.Fail(ErrorCodes.Forbidden,
                $"{bulan.Name} dikunci permanen. Status Locked tidak bisa dibuka kembali dari aplikasi; " +
                "perubahan langsung di basis data akan tercatat oleh periksa-diri.");

        await _periods.UpsertStatusAsync(ada.Name, bulan.StartText, bulan.EndText, "Open", userId);

        var sesudah = await CariAsync(bulan.StartDate);
        return Result<FiscalPeriodSummary>.Ok(sesudah ?? throw new InvalidOperationException(
            $"Periode {bulan.Name} sudah dibuka kembali tetapi tidak kembali dari daftar periode."));
    }

    private static FiscalMonth? ParseMonth(string? monthText)
        => FiscalCalendar.TryParseMonth(monthText, out var bulan) ? bulan : null;

    private async Task<FiscalPeriodSummary?> CariAsync(DateTime awalBulan)
        => (await _periods.ListAsync()).FirstOrDefault(p => Mulai(p.StartDate) == awalBulan);

    /// <summary>Kolom tanggal dibaca sebagai teks yyyy-MM-dd; nilai yang tidak terbaca dianggap bukan bulan.</summary>
    private static DateTime Mulai(string tanggalTeks)
        => FiscalCalendar.TryParseDate(tanggalTeks, out var t) ? t.Date : DateTime.MinValue;

    private static string StatusLabel(string status) => status switch
    {
        "Closed" => "tertutup",
        "Locked" => "dikunci permanen",
        _ => status
    };
}
