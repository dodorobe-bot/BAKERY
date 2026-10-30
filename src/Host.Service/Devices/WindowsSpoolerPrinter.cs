using System.Drawing;
using System.Drawing.Printing;
using Seruni22Bakery.Application.Devices;

// CetakAsync menolak lebih dulu ketika tidak berjalan di Windows, jadi semua panggilan GDI di
// bawahnya hanya bisa terjadi di platform yang mendukungnya.
#pragma warning disable CA1416

namespace Seruni22Bakery.Host.Devices;

/// <summary>
/// Jalur pertama yang diwajibkan: cetak senyap lewat driver Windows yang sudah terpasang.
/// Isi struk digambar sebagai teks monospace selebar kertas (58/80 mm). Pemotong kertas dan
/// laci uang mengikuti kemampuan driver/printer masing-masing dan tidak diklaim di sini.
/// </summary>
public sealed class WindowsSpoolerPrinter : IReceiptPrinter
{
    public bool Tersedia => OperatingSystem.IsWindows();
    public string NamaAdapter => "windows-driver";

    public Task CetakAsync(string printerName, string isi, int paperWidthMm, CancellationToken ct)
    {
        if (!Tersedia)
            throw new PlatformNotSupportedException("Pencetakan langsung hanya tersedia di Windows.");
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("Nama printer kosong.");

        // PrintDocument bekerja sinkron pada thread pemanggil; jalankan di thread pool supaya
        // permintaan HTTP tidak tertahan dan batas waktu di layanan tetap bisa bekerja.
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var baris = isi.Replace("\r", "").Split('\n');
            using var dokumen = new PrintDocument();
            dokumen.DocumentName = "Seruni 22 - struk";
            dokumen.PrintController = new StandardPrintController(); // tanpa dialog progres

            dokumen.PrinterSettings.PrinterName = printerName;
            if (!dokumen.PrinterSettings.IsValid)
                throw new InvalidOperationException($"Printer '{printerName}' tidak valid atau tidak terpasang di Windows.");

            // Tinggi kertas mengikuti jumlah baris supaya printer tidak menarik kertas kosong
            // panjang. Beberapa driver mengabaikan ukuran kustom dan memakai ukuran bawaannya;
            // itu batas perangkat, bukan sesuatu yang bisa dipaksakan dari aplikasi.
            var barisEfektif = Math.Max(baris.Length, 12);
            var tinggiMm = Math.Clamp(18 + barisEfektif * 4.6, 60.0, 2000.0);
            dokumen.DefaultPageSettings.PaperSize = new PaperSize(
                $"Struk {paperWidthMm}mm",
                MmKePerseratusInci(paperWidthMm),
                MmKePerseratusInci(tinggiMm));
            dokumen.DefaultPageSettings.Margins = new Margins(3, 3, 5, 5);
            dokumen.DefaultPageSettings.Landscape = false;

            var ukuranFont = paperWidthMm == 58 ? 8f : 9f;
            using var font = new Font("Consolas", ukuranFont, FontStyle.Regular, GraphicsUnit.Point);

            dokumen.PrintPage += (_, e) =>
            {
                var g = e.Graphics!;
                var y = (float)e.MarginBounds.Top;
                using var kuas = new SolidBrush(Color.Black);
                var tinggiBaris = font.GetHeight(g);
                foreach (var teks in baris)
                {
                    if (y > e.MarginBounds.Bottom - tinggiBaris) break;
                    g.DrawString(teks, font, kuas, e.MarginBounds.Left, y, StringFormat.GenericTypographic);
                    y += tinggiBaris;
                }
            };

            dokumen.Print();
        }, ct);
    }

    private static int MmKePerseratusInci(double mm) =>
        (int)Math.Round(mm / 25.4 * 100, MidpointRounding.AwayFromZero);
}
