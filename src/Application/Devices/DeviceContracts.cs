namespace Seruni22Bakery.Application.Devices;

/// <summary>
/// Pengaturan perangkat yang disimpan di basis data, bukan di state peramban: pilihan printer
/// dan lebar kertas harus bertahan setelah aplikasi ditutup, Windows di-restart, atau
/// instalasi diperbarui.
/// </summary>
public sealed record DeviceSettings(
    string PrinterName,
    int PaperWidthMm,
    bool AutoPrint,
    string ScannerSuffix)
{
    public const string KunciPrinter = "device.printer.name";
    public const string KunciMode = "device.printer.mode";
    public const string KunciLebar = "device.paper.width_mm";
    public const string KunciAutoPrint = "device.printer.auto_print";
    public const string KunciSuffix = "device.scanner.suffix";

    public const string ModeDriverWindows = "windows-driver";

    public static readonly int[] LebarDidukung = [58, 80];
}

public sealed record PrinterInfo(string Name, bool IsDefault);

/// <summary>
/// Keadaan perangkat saat ini. Semua isinya hasil pembacaan nyata (registry/spooler dan
/// AppSettings); tidak ada nama printer contoh yang dipalsukan saat tidak ada printer.
/// </summary>
public sealed record DeviceSnapshot(
    bool PerangkatDidukung,
    string Platform,
    IReadOnlyList<PrinterInfo> Printers,
    DeviceSettings Settings,
    string Mode,
    string Catatan);

public sealed record PrintOutcome(int? JobId, bool Berhasil, string? ErrorCode, string? Pesan);

/// <summary>Adaptor pencetak. Satu implementasi nyata per jalur; kegagalan dilempar sebagai exception.</summary>
public interface IReceiptPrinter
{
    bool Tersedia { get; }
    string NamaAdapter { get; }
    Task CetakAsync(string printerName, string isi, int paperWidthMm, CancellationToken ct);
}

/// <summary>Daftar printer terpasang. Implementasi tidak boleh mengarang nama.</summary>
public interface IPrinterCatalog
{
    bool Tersedia { get; }
    IReadOnlyList<string> DaftarNama();
    string? NamaDefault();
}

// ── Adaptor tahap berikutnya: disiapkan sebagai antarmuka, belum dipakai endpoint mana pun ──
// Laci uang hanya boleh terbuka setelah transaksi tunai memenuhi aturan usaha, layar pelanggan
// dan timbangan membutuhkan driver/SDK per model. Antarmuka ini sengaja belum punya
// implementasi produksi supaya tidak ada klaim "sudah didukung" sebelum diuji di perangkat.

public interface ICashDrawerAdapter
{
    bool Tersedia { get; }
    Task BukaAsync(CancellationToken ct);
}

public interface ICustomerDisplayAdapter
{
    bool Tersedia { get; }
    Task TampilkanAsync(string baris1, string baris2, CancellationToken ct);
}

public interface IScaleAdapter
{
    bool Tersedia { get; }
    Task<decimal> BacaBeratKgAsync(CancellationToken ct);
}
