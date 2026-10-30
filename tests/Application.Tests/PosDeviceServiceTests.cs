using FluentAssertions;
using Seruni22Bakery.Application.Devices;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace BakeryDesktop.Application.Tests;

/// <summary>
/// Antrean cetak adalah tempat paling mudah bagi penjualan untuk "hilang": printer mati lalu
/// aplikasi menganggap transaksi batal, atau klik ulang membuat baris kedua. Tes ini memakai
/// adaptor palsu (bukan perangkat nyata) untuk menuntut urutan yang tetap: pekerjaan tercatat,
/// kegagalan ditandai, dan pesanan tidak pernah diubah oleh jalur cetak.
/// </summary>
public class PosDeviceServiceTests
{
    private sealed class PengaturanPalsu : ISettingRepository
    {
        public Dictionary<string, string> Nilai { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> GetAsync(string key) =>
            Task.FromResult(Nilai.TryGetValue(key, out var v) ? v : null);

        public Task<Dictionary<string, string>> GetAllAsync() => Task.FromResult(new Dictionary<string, string>(Nilai));

        public Task SetAsync(string key, string value, int? userId = null)
        {
            Nilai[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class AntreanPalsu : IPrintJobRepository
    {
        public List<PrintJob> Job { get; } = [];
        public List<string> Galat { get; } = [];
        public DateTime? BatasMenggantung { get; private set; }

        public Task<int> CreateAsync(PrintJob job)
        {
            job.Id = Job.Count + 1;
            Job.Add(job);
            return Task.FromResult(job.Id);
        }

        public Task MarkPrintedAsync(int id, DateTime finishedAt)
        {
            var job = Job.Single(j => j.Id == id);
            job.Status = PrintJobStatus.Printed;
            job.FinishedAt = finishedAt;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(int id, string error, DateTime finishedAt)
        {
            var job = Job.Single(j => j.Id == id);
            job.Status = PrintJobStatus.Failed;
            job.LastError = error;
            job.Attempts++;
            job.FinishedAt = finishedAt;
            Galat.Add(error);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PrintJob>> GetRecentAsync(int limit = 50) =>
            Task.FromResult<IReadOnlyList<PrintJob>>(Job.Take(limit).ToList());

        public Task<int> FailStalePendingAsync(string error, DateTime createdBefore)
        {
            BatasMenggantung = createdBefore;
            return Task.FromResult(2);
        }
    }

    private sealed class PrinterPalsu : IReceiptPrinter
    {
        public bool Tersedia { get; set; } = true;
        public string NamaAdapter => "palsu";
        public Exception? Gagal { get; set; }
        public List<string> Dicetak { get; } = [];

        public Task CetakAsync(string printerName, string isi, int paperWidthMm, CancellationToken ct)
        {
            if (Gagal is not null) throw Gagal;
            Dicetak.Add(isi);
            return Task.CompletedTask;
        }
    }

    private sealed class KatalogPalsu : IPrinterCatalog
    {
        public bool Tersedia { get; set; } = true;
        public List<string> Nama { get; } = ["Printer Uji"];
        public IReadOnlyList<string> DaftarNama() => Nama;
        public string? NamaDefault() => Nama.FirstOrDefault();
    }

    private static SalesOrder Pesanan() => new()
    {
        Id = 11,
        OrderNumber = "ORD-2026-000011",
        CustomerNameSnapshot = "Bapak Andi",
        Description = "Bolu pisang",
        PickupDate = new DateTime(2026, 10, 30),
        TotalAmount = 2_000_000,
        Quantity = 2,
        CreatedAt = DateTime.UtcNow,
    };

    private static (PosDeviceService Layanan, AntreanPalsu Antrean, PrinterPalsu Printer, PengaturanPalsu Setelan, KatalogPalsu Katalog)
        Buat(string? printer = "Printer Uji", bool printerTersedia = true, bool katalogTersedia = true)
    {
        var setelan = new PengaturanPalsu();
        if (printer is not null) setelan.Nilai[DeviceSettings.KunciPrinter] = printer;
        var antrean = new AntreanPalsu();
        var adapter = new PrinterPalsu { Tersedia = printerTersedia };
        var katalog = new KatalogPalsu { Tersedia = katalogTersedia };
        return (new PosDeviceService(antrean, setelan, adapter, katalog), antrean, adapter, setelan, katalog);
    }

    [Fact]
    public async Task CetakStruk_Berhasil_MenandaiPrintedDanMenyimpanIsi()
    {
        var (layanan, antrean, printer, _, _) = Buat();

        var hasil = await layanan.CetakStrukAsync(Pesanan(), cetakUlang: false, userId: 3);

        hasil.Berhasil.Should().BeTrue();
        hasil.JobId.Should().NotBeNull();
        antrean.Job.Should().ContainSingle().Which.Status.Should().Be(PrintJobStatus.Printed);
        printer.Dicetak.Should().ContainSingle().Which.Should().Contain("ORD-2026-000011");
    }

    [Fact]
    public async Task CetakStruk_PrinterMelempar_JobFailedDanPesananTidakBerubah()
    {
        var (layanan, antrean, printer, _, _) = Buat();
        printer.Gagal = new InvalidOperationException("Kertas habis");
        var pesanan = Pesanan();
        var sebelum = (pesanan.Status, pesanan.TotalAmount, pesanan.Quantity);

        var hasil = await layanan.CetakStrukAsync(pesanan, cetakUlang: false, userId: 3);

        hasil.Berhasil.Should().BeFalse();
        hasil.ErrorCode.Should().Be(ErrorCodes.PrintFailed);
        hasil.Pesan.Should().Contain("Transaksi tetap tersimpan");
        antrean.Job.Should().ContainSingle().Which.Status.Should().Be(PrintJobStatus.Failed);
        antrean.Galat.Should().Contain("Kertas habis");
        (pesanan.Status, pesanan.TotalAmount, pesanan.Quantity).Should().Be(sebelum,
            "jalur cetak tidak boleh menyentuh pesanan");
    }

    [Fact]
    public async Task CetakStruk_BatasWaktu_DilaporkanSebagaiPrinterTidakMerespons()
    {
        var (layanan, antrean, printer, _, _) = Buat();
        printer.Gagal = new OperationCanceledException();

        var hasil = await layanan.CetakStrukAsync(Pesanan(), cetakUlang: false, userId: 3);

        hasil.Berhasil.Should().BeFalse();
        hasil.Pesan.Should().Contain("20 detik");
        antrean.Job.Single().LastError.Should().Contain("20 detik");
    }

    [Fact]
    public async Task CetakStruk_TanpaPrinterTerdaftar_TidakMembuatPekerjaan()
    {
        var (layanan, antrean, printer, _, _) = Buat(printer: null);

        var hasil = await layanan.CetakStrukAsync(Pesanan(), cetakUlang: false, userId: 3);

        hasil.ErrorCode.Should().Be(ErrorCodes.PrinterNotConfigured);
        hasil.Pesan.Should().Contain("Transaksi tetap tersimpan");
        antrean.Job.Should().BeEmpty("tidak ada pekerjaan untuk printer yang belum dipilih");
        printer.Dicetak.Should().BeEmpty();
    }

    [Fact]
    public async Task CetakStruk_PrinterTidakTerpasang_DitolakDenganPesanJelas()
    {
        var (layanan, antrean, printer, _, _) = Buat(printer: "Printer Lama");

        var hasil = await layanan.CetakStrukAsync(Pesanan(), cetakUlang: false, userId: 3);

        hasil.ErrorCode.Should().Be(ErrorCodes.PrinterNotFound);
        hasil.Pesan.Should().Contain("Printer Lama");
        antrean.Job.Should().BeEmpty();
        printer.Dicetak.Should().BeEmpty();
    }

    [Fact]
    public async Task CetakStruk_NonWindows_DitolakSebelumMenyentuhAdapter()
    {
        var (layanan, antrean, printer, _, _) = Buat(printerTersedia: false);

        var hasil = await layanan.CetakStrukAsync(Pesanan(), cetakUlang: false, userId: 3);

        hasil.ErrorCode.Should().Be(ErrorCodes.PrintFailed);
        hasil.Pesan.Should().Contain("hanya tersedia saat aplikasi berjalan di Windows");
        antrean.Job.Should().BeEmpty();
        printer.Dicetak.Should().BeEmpty();
    }

    [Fact]
    public async Task CetakStruk_CetakUlang_PayloadMemuatPenanda()
    {
        var (layanan, _, printer, _, _) = Buat();

        await layanan.CetakStrukAsync(Pesanan(), cetakUlang: true, userId: 3);

        printer.Dicetak.Single().Should().Contain("*** CETAK ULANG ***");
    }

    [Fact]
    public async Task CetakStruk_IsiTidakDiambilDariPemanggil_TetapiDariPesanan()
    {
        var (layanan, _, printer, _, _) = Buat();

        await layanan.CetakStrukAsync(Pesanan(), cetakUlang: false, userId: 3);

        printer.Dicetak.Single().Should().Contain("Rp 20.000").And.Contain("2 x Rp 10.000");
    }

    [Fact]
    public async Task PantauStatus_TanpaPrinter_TerdeteksiDariKatalog()
    {
        var (layanan, _, _, _, katalog) = Buat(printer: null);
        katalog.Nama.Clear();

        var snapshot = await layanan.GetSnapshotAsync();

        snapshot.Printers.Should().BeEmpty();
        snapshot.Catatan.Should().Contain("Tidak ada printer terpasang");
    }

    [Fact]
    public async Task PantauStatus_PrinterTersimpanSudahTidakAda_DiberiCatatan()
    {
        var (layanan, _, _, _, _) = Buat(printer: "Printer Lama");

        var snapshot = await layanan.GetSnapshotAsync();

        snapshot.Catatan.Should().Contain("Printer Lama").And.Contain("tidak ditemukan");
    }

    [Fact]
    public async Task PantauStatus_NonWindows_TidakMengarangDukungan()
    {
        var (layanan, _, _, _, katalog) = Buat();
        katalog.Tersedia = false;

        var snapshot = await layanan.GetSnapshotAsync();

        snapshot.Printers.Should().BeEmpty();
        snapshot.Catatan.Should().Contain("hanya tersedia saat aplikasi berjalan di Windows");
    }

    [Fact]
    public async Task SimpanPengaturan_KunciAsing_Ditolak()
    {
        var (layanan, _, _, setelan, _) = Buat();

        var hasil = await layanan.SimpanPengaturanAsync(
            new Dictionary<string, string> { ["device.misterius"] = "1" }, userId: 1);

        hasil.Success.Should().BeFalse();
        hasil.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
        hasil.ErrorMessage.Should().Contain("device.misterius");
        setelan.Nilai.Should().NotContainKey("device.misterius");
    }

    [Theory]
    [InlineData("57")]
    [InlineData("80.5")]
    [InlineData("")]
    public async Task SimpanPengaturan_LebarTidakDidukung_Ditolak(string lebar)
    {
        var (layanan, _, _, setelan, _) = Buat();

        var hasil = await layanan.SimpanPengaturanAsync(
            new Dictionary<string, string> { [DeviceSettings.KunciLebar] = lebar }, userId: 1);

        hasil.Success.Should().BeFalse();
        hasil.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
        setelan.Nilai.Should().NotContain(DeviceSettings.KunciLebar, "nilai tidak sah tidak boleh tersimpan");
    }

    [Fact]
    public async Task SimpanPengaturan_ModeEscPosBelumTersedia_Ditolak()
    {
        var (layanan, _, _, _, _) = Buat();

        var hasil = await layanan.SimpanPengaturanAsync(
            new Dictionary<string, string> { [DeviceSettings.KunciMode] = "escpos-raw" }, userId: 1);

        hasil.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
        hasil.ErrorMessage.Should().Contain("windows-driver");
    }

    [Fact]
    public async Task SimpanPengaturan_PrinterTidakTerpasang_Ditolak()
    {
        var (layanan, _, _, setelan, _) = Buat();

        var hasil = await layanan.SimpanPengaturanAsync(
            new Dictionary<string, string> { [DeviceSettings.KunciPrinter] = "Printer Karangan" }, userId: 1);

        hasil.ErrorCode.Should().Be(ErrorCodes.PrinterNotFound);
        setelan.Nilai[DeviceSettings.KunciPrinter].Should().Be("Printer Uji",
            "printer yang tidak terpasang tidak boleh menggantikan pilihan yang sah");
    }

    [Fact]
    public async Task SimpanPengaturan_Sah_TersimpanDanTerbacaUlang()
    {
        var (layanan, _, _, setelan, _) = Buat(printer: null);
        setelan.Nilai["outlet_name"] = "Cabang Kota";

        var hasil = await layanan.SimpanPengaturanAsync(new Dictionary<string, string>
        {
            [DeviceSettings.KunciPrinter] = "Printer Uji",
            [DeviceSettings.KunciLebar] = "58",
            [DeviceSettings.KunciAutoPrint] = "1",
            [DeviceSettings.KunciSuffix] = "enter",
        }, userId: 9);

        hasil.Success.Should().BeTrue();
        hasil.Data!.PrinterName.Should().Be("Printer Uji");
        hasil.Data.PaperWidthMm.Should().Be(58);
        hasil.Data.AutoPrint.Should().BeTrue();

        var dibacaUlang = await layanan.GetSettingsAsync();
        dibacaUlang.Should().Be(hasil.Data, "pengaturan perangkat harus bertahan, bukan hanya di state layar");
    }

    [Fact]
    public async Task PulihkanJobMenggantung_MemakaiBatasWaktuNyata()
    {
        var (layanan, antrean, _, _, _) = Buat();

        var jumlah = await layanan.PulihkanJobMenggantungAsync();

        jumlah.Should().Be(2);
        antrean.BatasMenggantung.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(-10), TimeSpan.FromMinutes(1));
    }
}
