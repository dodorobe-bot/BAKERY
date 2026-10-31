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
    public async Task EksporPembelian_TidakMemotongJumlahPecahan()
    {
        // Regresi: nilai baris dulu dihitung (long)qty * harga, sehingga 2,5 kg × Rp10.000
        // keluar sebagai Rp20.000 — setengah kilogram terbuang dari nilai yang dibayar.
        QuestPDF.Settings.License = LicenseType.Community;
        using var db = new TempDatabase();
        db.Exec("INSERT INTO Suppliers (Name) VALUES ('Pemasok Pecahan');");
        db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                "VALUES ('Tepung Pecahan', 'kg', 1000000, 0, 0, 1);");
        db.Exec("INSERT INTO PurchaseOrders (PoNumber, SupplierId, Status) " +
                "SELECT 'PO-PECAHAN', Id, 'Sent' FROM Suppliers WHERE Name = 'Pemasok Pecahan';");
        db.Exec("INSERT INTO PurchaseOrderLines (PurchaseOrderId, IngredientId, OrderedQty, UnitCostMinorUnits, Unit) " +
                "SELECT po.Id, i.Id, 2.5, 1000000, 'kg' FROM PurchaseOrders po, Ingredients i " +
                "WHERE po.PoNumber = 'PO-PECAHAN' AND i.Name = 'Tepung Pecahan';");

        var factory = new DbConnectionFactory(db.ConnectionString);
        var service = new ExportService(
            new SalesOrderRepository(factory), new JournalRepository(factory),
            new IngredientRepository(factory), new PurchaseOrderRepository(factory));

        var (data, _, _) = await service.ExportPurchasesAsync("xlsx");
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(data));
        var nilai = wb.Worksheet("Pembelian").Cell(4, 5).GetDouble();
        // Kolom Excel berisi rupiah (bukan unit minor): 2.500.000 minor = Rp25.000.
        nilai.Should().Be(25_000, "2,5 kg × Rp10.000 = Rp25.000, bukan Rp20.000 karena pecahan dipotong");
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
