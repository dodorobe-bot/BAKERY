using System.Globalization;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Devices;

/// <summary>
/// Satu pintu untuk semua akses perangkat kasir. Endpoint hanya boleh memanggil layanan ini;
/// tidak ada jalur dari layar ke driver, antrean cetak, atau sistem berkas secara langsung.
///
/// Urutan yang dijaga: pesanan sudah tersimpan di basis data sebelum pekerjaan cetak dibuat.
/// Kegagalan printer dicatat pada pekerjaan cetak dan dikembalikan sebagai pesan, tidak pernah
/// membatalkan atau mengulang penjualan.
/// </summary>
public sealed class PosDeviceService
{
    public static readonly TimeSpan BatasCetak = TimeSpan.FromSeconds(20);

    private readonly IPrintJobRepository _jobs;
    private readonly ISettingRepository _settings;
    private readonly IReceiptPrinter _printer;
    private readonly IPrinterCatalog _catalog;

    public PosDeviceService(
        IPrintJobRepository jobs,
        ISettingRepository settings,
        IReceiptPrinter printer,
        IPrinterCatalog catalog)
    {
        _jobs = jobs;
        _settings = settings;
        _printer = printer;
        _catalog = catalog;
    }

    public async Task<DeviceSettings> GetSettingsAsync()
    {
        var semua = await _settings.GetAllAsync();
        return BacaPengaturan(semua);
    }

    public async Task<DeviceSnapshot> GetSnapshotAsync()
    {
        var semua = await _settings.GetAllAsync();
        var pengaturan = BacaPengaturan(semua);
        var daftar = _catalog.Tersedia
            ? _catalog.DaftarNama().Select(n => new PrinterInfo(n, string.Equals(n, _catalog.NamaDefault(), StringComparison.OrdinalIgnoreCase))).ToList()
            : [];

        var mode = semua.TryGetValue(DeviceSettings.KunciMode, out var m) && !string.IsNullOrWhiteSpace(m)
            ? m
            : DeviceSettings.ModeDriverWindows;

        string catatan;
        if (!_catalog.Tersedia)
            catatan = "Pencetakan perangkat hanya tersedia saat aplikasi berjalan di Windows.";
        else if (daftar.Count == 0)
            catatan = "Tidak ada printer terpasang di Windows ini. Pasang driver printer lebih dulu.";
        else if (string.IsNullOrWhiteSpace(pengaturan.PrinterName))
            catatan = "Printer belum dipilih. Pilih salah satu printer terpasang lalu cetak struk uji.";
        else if (!daftar.Any(p => string.Equals(p.Name, pengaturan.PrinterName, StringComparison.OrdinalIgnoreCase)))
            catatan = $"Printer '{pengaturan.PrinterName}' tidak ditemukan di Windows ini. Pilih ulang printer.";
        else
            catatan = string.Empty;

        return new DeviceSnapshot(
            _printer.Tersedia,
            OperatingSystem.IsWindows() ? "windows" : "lain",
            daftar,
            pengaturan,
            mode,
            catatan);
    }

    public async Task<Result<DeviceSettings>> SimpanPengaturanAsync(
        IReadOnlyDictionary<string, string> nilai, int? userId)
    {
        var dikenal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DeviceSettings.KunciPrinter,
            DeviceSettings.KunciMode,
            DeviceSettings.KunciLebar,
            DeviceSettings.KunciAutoPrint,
            DeviceSettings.KunciSuffix,
        };
        var asing = nilai.Keys.Where(k => !dikenal.Contains(k)).ToList();
        if (asing.Count > 0)
            return Result<DeviceSettings>.Fail(ErrorCodes.ValidationFailed,
                $"Pengaturan perangkat tidak dikenal: {string.Join(", ", asing)}.");

        foreach (var (kunci, isi) in nilai)
        {
            var v = (isi ?? string.Empty).Trim();
            if (kunci.Equals(DeviceSettings.KunciPrinter, StringComparison.OrdinalIgnoreCase))
            {
                if (v.Length > 200)
                    return Result<DeviceSettings>.Fail(ErrorCodes.ValidationFailed, "Nama printer terlalu panjang.");
                if (v.Length > 0 && _catalog.Tersedia
                    && !_catalog.DaftarNama().Any(n => string.Equals(n, v, StringComparison.OrdinalIgnoreCase)))
                    return Result<DeviceSettings>.Fail(ErrorCodes.PrinterNotFound,
                        $"Printer '{v}' tidak terpasang di Windows ini.");
            }
            else if (kunci.Equals(DeviceSettings.KunciLebar, StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm)
                    || !DeviceSettings.LebarDidukung.Contains(mm))
                    return Result<DeviceSettings>.Fail(ErrorCodes.ValidationFailed,
                        "Lebar kertas harus 58 atau 80 mm.");
            }
            else if (kunci.Equals(DeviceSettings.KunciAutoPrint, StringComparison.OrdinalIgnoreCase))
            {
                if (v is not ("0" or "1"))
                    return Result<DeviceSettings>.Fail(ErrorCodes.ValidationFailed,
                        "Cetak otomatis harus '0' (mati) atau '1' (hidup).");
            }
            else if (kunci.Equals(DeviceSettings.KunciSuffix, StringComparison.OrdinalIgnoreCase))
            {
                // Hanya Enter yang benar-benar diuji pada alur pindai. Pilihan lain belum
                // ditangani layar, jadi ditolak alih-alih disimpan sebagai janji kosong.
                if (!string.Equals(v, "enter", StringComparison.OrdinalIgnoreCase))
                    return Result<DeviceSettings>.Fail(ErrorCodes.ValidationFailed,
                        "Akhiran pindai yang didukung saat ini hanya 'enter'.");
            }
            else if (kunci.Equals(DeviceSettings.KunciMode, StringComparison.OrdinalIgnoreCase))
            {
                // Jalur ESC/POS langsung belum diverifikasi pada perangkat nyata, jadi belum
                // bisa dipilih. Menyimpannya akan membuat status perangkat menyesatkan.
                if (!string.Equals(v, DeviceSettings.ModeDriverWindows, StringComparison.OrdinalIgnoreCase))
                    return Result<DeviceSettings>.Fail(ErrorCodes.ValidationFailed,
                        "Hanya mode 'windows-driver' yang didukung. Mode ESC/POS langsung belum tersedia.");
            }
        }

        foreach (var (kunci, isi) in nilai)
            await _settings.SetAsync(kunci, (isi ?? string.Empty).Trim(), userId);

        return Result<DeviceSettings>.Ok(await GetSettingsAsync());
    }

    /// <summary>
    /// Mencetak struk pesanan yang sudah tersimpan. Pemanggil wajib memuat ulang pesanan dari
    /// basis data; nilai struk tidak pernah diambil dari badan permintaan layar.
    /// </summary>
    public async Task<PrintOutcome> CetakStrukAsync(SalesOrder order, bool cetakUlang, int? userId)
    {
        var pengaturan = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(pengaturan.PrinterName))
            return new PrintOutcome(null, false, ErrorCodes.PrinterNotConfigured,
                "Printer belum dipilih. Buka Pengaturan Perangkat Kasir. Transaksi tetap tersimpan.");
        if (_catalog.Tersedia
            && !_catalog.DaftarNama().Any(n => string.Equals(n, pengaturan.PrinterName, StringComparison.OrdinalIgnoreCase)))
            return new PrintOutcome(null, false, ErrorCodes.PrinterNotFound,
                $"Printer '{pengaturan.PrinterName}' tidak terpasang di Windows ini. Periksa Pengaturan Perangkat Kasir. Transaksi tetap tersimpan.");
        if (!_printer.Tersedia)
            return new PrintOutcome(null, false, ErrorCodes.PrintFailed,
                "Pencetakan langsung hanya tersedia saat aplikasi berjalan di Windows. Transaksi tetap tersimpan.");

        var outlet = await _settings.GetAsync("outlet_name");
        var merek = await _settings.GetAsync(StoreBrand.NameKey);
        var slogan = await _settings.GetAsync(StoreBrand.TaglineKey);
        var isi = ReceiptRenderer.Render(order, outlet, pengaturan.PaperWidthMm, cetakUlang, merek, slogan);
        return await JalankanAsync(PrintJobKind.Receipt, order.Id, pengaturan, isi, userId);
    }

    public async Task<PrintOutcome> CetakUjiAsync(int? userId)
    {
        var pengaturan = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(pengaturan.PrinterName))
            return new PrintOutcome(null, false, ErrorCodes.PrinterNotConfigured,
                "Pilih printer lebih dulu sebelum mencetak struk uji.");
        if (_catalog.Tersedia
            && !_catalog.DaftarNama().Any(n => string.Equals(n, pengaturan.PrinterName, StringComparison.OrdinalIgnoreCase)))
            return new PrintOutcome(null, false, ErrorCodes.PrinterNotFound,
                $"Printer '{pengaturan.PrinterName}' tidak terpasang di Windows ini.");
        if (!_printer.Tersedia)
            return new PrintOutcome(null, false, ErrorCodes.PrintFailed,
                "Pencetakan langsung hanya tersedia saat aplikasi berjalan di Windows.");

        var outlet = await _settings.GetAsync("outlet_name");
        var merek = await _settings.GetAsync(StoreBrand.NameKey);
        var slogan = await _settings.GetAsync(StoreBrand.TaglineKey);
        var isi = ReceiptRenderer.RenderTest(outlet, pengaturan.PrinterName, pengaturan.PaperWidthMm, BusinessTime.Now(), merek, slogan);
        return await JalankanAsync(PrintJobKind.Test, null, pengaturan, isi, userId);
    }

    public Task<IReadOnlyList<PrintJob>> DaftarJobAsync(int limit = 50) =>
        _jobs.GetRecentAsync(Math.Clamp(limit, 1, 200));

    /// <summary>
    /// Pekerjaan yang masih Pending saat aplikasi berhenti tidak akan pernah selesai dengan
    /// sendirinya. Saat start berikutnya baris itu ditandai Failed supaya antrean jujur.
    /// </summary>
    public Task<int> PulihkanJobMenggantungAsync() =>
        _jobs.FailStalePendingAsync(
            "Aplikasi berhenti sebelum pencetakan selesai. Cetak ulang bila struk belum keluar.",
            DateTime.UtcNow.AddMinutes(-10));

    private async Task<PrintOutcome> JalankanAsync(
        PrintJobKind jenis, int? orderId, DeviceSettings pengaturan, string isi, int? userId)
    {
        var job = new PrintJob
        {
            OrderId = orderId,
            Kind = jenis,
            PrinterName = pengaturan.PrinterName,
            PaperWidthMm = pengaturan.PaperWidthMm,
            Status = PrintJobStatus.Pending,
            Payload = isi,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
        };
        var jobId = await _jobs.CreateAsync(job);

        try
        {
            using var cts = new CancellationTokenSource(BatasCetak);
            await _printer.CetakAsync(pengaturan.PrinterName, isi, pengaturan.PaperWidthMm, cts.Token);
            await _jobs.MarkPrintedAsync(jobId, DateTime.UtcNow);
            return new PrintOutcome(jobId, true, null, "Struk terkirim ke printer.");
        }
        catch (Exception ex)
        {
            var pesan = ex is TimeoutException or OperationCanceledException
                ? $"Printer tidak merespons dalam {BatasCetak.TotalSeconds:0} detik."
                : ex.Message;
            await _jobs.MarkFailedAsync(jobId, pesan, DateTime.UtcNow);
            return new PrintOutcome(jobId, false, ErrorCodes.PrintFailed,
                $"Struk gagal dicetak: {pesan} Transaksi tetap tersimpan; periksa printer lalu cetak ulang.");
        }
    }

    private static DeviceSettings BacaPengaturan(IReadOnlyDictionary<string, string> semua)
    {
        var printer = semua.TryGetValue(DeviceSettings.KunciPrinter, out var p) ? (p ?? "").Trim() : "";
        var lebar = semua.TryGetValue(DeviceSettings.KunciLebar, out var l)
                    && int.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm)
                    && DeviceSettings.LebarDidukung.Contains(mm)
            ? mm : 80;
        var auto = semua.TryGetValue(DeviceSettings.KunciAutoPrint, out var a) && a == "1";
        var suffix = semua.TryGetValue(DeviceSettings.KunciSuffix, out var s) && !string.IsNullOrWhiteSpace(s)
            ? s.Trim().ToLowerInvariant()
            : "enter";
        return new DeviceSettings(printer, lebar, auto, suffix);
    }
}
