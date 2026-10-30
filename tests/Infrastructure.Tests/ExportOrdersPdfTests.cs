using FluentAssertions;
using QuestPDF.Infrastructure;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Ekspor PDF penjualan pernah selalu gagal karena lebar kolom tetap melampaui isi halaman A4
/// tegak. Tes ini menghasilkan berkas sungguhan dari basis data hasil migrasi supaya kesalahan
/// tata letak tidak kembali tanpa ketahuan.
/// </summary>
public class ExportOrdersPdfTests
{
    [Fact]
    public async Task EksporPenjualanPdf_MenghasilkanBerkasPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        using var db = new TempDatabase();
        db.Exec("INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, DownPayment, Status) " +
                "VALUES ('ORD-2026-10-28-001', 'Pelanggan Uji', 'Roti tawar', '2026-10-28', 3040000, 1000000, 'Pending');");

        var factory = new DbConnectionFactory(db.ConnectionString);
        var service = new ExportService(
            new SalesOrderRepository(factory),
            new JournalRepository(factory),
            new IngredientRepository(factory),
            new PurchaseOrderRepository(factory));

        var (data, contentType, filename) = await service.ExportOrdersAsync(
            new DateTime(2026, 10, 28), new DateTime(2026, 10, 28), "pdf");

        contentType.Should().Be("application/pdf");
        filename.Should().EndWith(".pdf");
        data.Length.Should().BeGreaterThan(1_000);
        data[0].Should().Be((byte)'%');
        data[1].Should().Be((byte)'P');
        data[2].Should().Be((byte)'D');
        data[3].Should().Be((byte)'F');
    }

    [Fact]
    public async Task EksporPeriodeLama_TidakTerpotongOlehBatasDaftarTerbaru()
    {
        // Regresi: ekspor dulu mengambil 500 pesanan terbaru lalu menyaring tanggal di memori,
        // jadi periode lama keluar kosong walau pesanannya ada di basis data.
        QuestPDF.Settings.License = LicenseType.Community;
        using var db = new TempDatabase();
        db.Exec("WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x < 505) " +
                "INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, DownPayment, Status) " +
                "SELECT 'ORD-BARU-' || x, 'Pelanggan Baru', 'Roti', '2026-10-20', 100000, 0, 'Pending' FROM n;");
        db.Exec("INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, DownPayment, Status) " +
                "VALUES ('ORD-LAMA-001', 'Pelanggan Lama', 'Roti lama', '2026-01-05', 250000, 0, 'Paid');");

        var factory = new DbConnectionFactory(db.ConnectionString);
        var repo = new SalesOrderRepository(factory);
        var lama = await repo.GetByPickupDateRangeAsync(new DateTime(2026, 1, 5), new DateTime(2026, 1, 5));
        lama.Should().ContainSingle(o => o.OrderNumber == "ORD-LAMA-001");

        var service = new ExportService(
            repo, new JournalRepository(factory), new IngredientRepository(factory),
            new PurchaseOrderRepository(factory));
        var (data, _, filename) = await service.ExportOrdersAsync(
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 5), "xlsx");
        filename.Should().Contain("2026-01-05");
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(data));
        var isi = wb.Worksheets.SelectMany(ws => ws.CellsUsed()).Select(c => c.GetString()).ToList();
        isi.Should().Contain("Pelanggan Lama", "pesanan periode lama harus ikut di berkas ekspor");
        isi.Should().Contain("ORD-LAMA-001");
        isi.Should().NotContain("Pelanggan Baru", "pesanan di luar rentang tidak boleh muncul");
    }

    [Fact]
    public async Task KepalaEkspor_MengikutiNamaTokoDiPengaturan()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        using var db = new TempDatabase();
        db.Exec("INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, DownPayment, Status) " +
                "VALUES ('ORD-MEREK-001', 'Pelanggan Uji', 'Roti', '2026-10-28', 100000, 0, 'Paid');");

        var factory = new DbConnectionFactory(db.ConnectionString);
        await new SettingRepository(factory).SetAsync("brand_name", "Toko Roti Bu Ratna");

        var service = new ExportService(
            new SalesOrderRepository(factory), new JournalRepository(factory),
            new IngredientRepository(factory), new PurchaseOrderRepository(factory),
            new SettingRepository(factory));

        var (data, _, _) = await service.ExportOrdersAsync(
            new DateTime(2026, 10, 28), new DateTime(2026, 10, 28), "xlsx");

        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(data));
        var isi = wb.Worksheets.SelectMany(ws => ws.CellsUsed()).Select(c => c.GetString()).ToList();
        isi.Should().Contain("Toko Roti Bu Ratna - Laporan Penjualan",
            "berkas yang dikirim ke luar harus memakai merek yang berlaku saat ekspor");
    }
}
