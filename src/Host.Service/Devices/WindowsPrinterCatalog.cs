using Microsoft.Win32;
using Seruni22Bakery.Application.Devices;

// Setiap jalur di kelas ini diperiksa dengan Tersedia sebelum registry disentuh; anotasi
// platform pada tiap metode akan memaksa pemanggil memeriksa hal yang sama sekali lagi.
#pragma warning disable CA1416

namespace Seruni22Bakery.Host.Devices;

/// <summary>
/// Daftar printer dibaca dari registry Windows (tempat spooler mendaftarkan printer terpasang).
/// Tidak ada nama contoh: kalau daftar kosong, memang tidak ada printer terpasang.
/// </summary>
public sealed class WindowsPrinterCatalog : IPrinterCatalog
{
    private const string KunciPrinter = @"SYSTEM\CurrentControlSet\Control\Print\Printers";
    private const string KunciDefault = @"Software\Microsoft\Windows NT\CurrentVersion\Windows";

    public bool Tersedia => OperatingSystem.IsWindows();

    public IReadOnlyList<string> DaftarNama()
    {
        if (!Tersedia) return [];
        try
        {
            using var kunci = Registry.LocalMachine.OpenSubKey(KunciPrinter);
            if (kunci is null) return [];
            return kunci.GetSubKeyNames()
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            // Registry bisa ditolak kebijakan mesin. Daftar kosong lebih jujur daripada
            // menampilkan printer yang tidak bisa dipakai.
            return [];
        }
    }

    public string? NamaDefault()
    {
        if (!Tersedia) return null;
        try
        {
            using var kunci = Registry.CurrentUser.OpenSubKey(KunciDefault);
            var perangkat = kunci?.GetValue("Device") as string;
            if (string.IsNullOrWhiteSpace(perangkat)) return null;
            var nama = perangkat.Split(',')[0].Trim();
            return nama.Length == 0 ? null : nama;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
