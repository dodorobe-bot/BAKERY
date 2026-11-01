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
    string ScannerSuffix,
    string Mode = DeviceSettings.ModeDriverWindows,
    string EscPosHost = "",
    int EscPosPort = 9100,
    bool EscPosPotong = false)
{
    public const string KunciPrinter = "device.printer.name";
    public const string KunciMode = "device.printer.mode";
    public const string KunciLebar = "device.paper.width_mm";
    public const string KunciAutoPrint = "device.printer.auto_print";
    public const string KunciSuffix = "device.scanner.suffix";
    public const string KunciEscPosHost = "device.escpos.host";
    public const string KunciEscPosPort = "device.escpos.port";
    public const string KunciEscPosPotong = "device.escpos.cut";

    public const string ModeDriverWindows = "windows-driver";
    public const string ModeEscPosRaw = "escpos-raw";

    public static readonly int[] LebarDidukung = [58, 80];
    public const int PortEscPosBawaan = 9100;

    /// <summary>
    /// Tujuan yang dilaporkan ke layar dan ke antrean cetak. Pada jalur raw nama printer Windows
    /// tidak dipakai: riwayat harus jujur menunjukkan alamat yang dikirim, bukan driver yang
    /// tidak pernah disentuh. Kosong bila jalur raw belum punya alamat.
    /// </summary>
    public string NamaTujuan => string.Equals(Mode, ModeEscPosRaw, StringComparison.OrdinalIgnoreCase)
        ? (string.IsNullOrWhiteSpace(EscPosHost) ? "" : $"tcp://{EscPosHost}:{EscPosPort}")
        : PrinterName;

    public bool PakaiEscPos => string.Equals(Mode, ModeEscPosRaw, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Tujuan cetak yang sudah terseleksi. Layanan yang membentuknya dari pengaturan; adaptor
/// tidak pernah membaca basis data atau registry sendiri.
/// </summary>
/// <param name="PotongKertas">hanya dipakai jalur ESC/POS; jalur driver mengabaikannya karena
/// pemotongan mengikuti kemampuan driver printer.</param>
public sealed record PrintTarget(
    string Mode,
    string PrinterName,
    string Host,
    int Port,
    int PaperWidthMm,
    bool PotongKertas = false);

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
    Task CetakAsync(PrintTarget target, string isi, CancellationToken ct);
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
