using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// BUG-032 dan BUG-034: harga pokok yang dibukukan harus harga batch yang benar-benar
/// berpindah, bukan harga master yang sedang terpasang saat tombol ditekan. Angka di bawah
/// dipilih supaya perhitungan lama (harga master / rata-rata bergerak) memberi hasil yang
/// BERBEDA — kalau suatu hari kode kembali ke perhitungan itu, tes ini yang menyala merah.
/// </summary>
public class CostLayerFifoTests
{
    // CostLayerLedger internal: nama jenis lapisan ditulis seperti yang tersimpan di tabel.
    private const string KindBahan = "Ingredient";
    private const string KindProdukJadi = "FinishedGoods";

    private const int Gudang = 1;
    private const int Dapur = 2;
    private const int Etalase = 3;
    private const int Pengguna = 1;

    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;
    private readonly ProductionRepository _production;
    private readonly LocationStockRepository _stock;
    private readonly SalesOrderRepository _orders;
    private readonly SalesReturnRepository _returns;
    private readonly JournalRepository _journals;
    private readonly PurchaseOrderRepository _purchases;
    private readonly AccountingService _accounting;
    private readonly OrderService _orderService;
    private readonly PurchaseService _purchaseService;
    private readonly int _pemasok;

    public CostLayerFifoTests()
    {
        _factory = new DbConnectionFactory(_db.ConnectionString);
        _production = new ProductionRepository(_factory);
        _stock = new LocationStockRepository(_factory);
        _orders = new SalesOrderRepository(_factory);
        _returns = new SalesReturnRepository(_factory);
        _journals = new JournalRepository(_factory);
        _purchases = new PurchaseOrderRepository(_factory);
        _accounting = new AccountingService(_journals, new AccountRepository(_factory));
        _orderService = new OrderService(
            _orders, _stock, _journals,
            new DocumentSequenceRepository(_factory), _accounting, _returns,
            new SettingRepository(_factory));
        _purchaseService = new PurchaseService(_purchases, _stock, _accounting);

        _db.Exec("INSERT INTO Suppliers (Name, IsActive) VALUES ('Pemasok Lapisan', 1);");
        _pemasok = Convert.ToInt32(_db.Scalar("SELECT Id FROM Suppliers WHERE Name = 'Pemasok Lapisan';"));
    }

    private int BahanBaru(string nama, long hargaMaster)
    {
        _db.Exec($"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 $"VALUES ('{nama}', 'kg', {hargaMaster}, 0, 0, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM Ingredients WHERE Name = '{nama}';"));
    }

    private int ProdukBaru(string nama)
    {
        _db.Exec($"INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                 $"VALUES ('{nama}', 'Roti', 1500000, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM ProductTemplates WHERE Name = '{nama}';"));
    }

    /// <summary>Penerimaan sungguhan: harga struk masuk jadi lapisan, jurnal 1-003/1-004 ikut tertulis.</summary>
    private async Task TerimaBahanAsync(int bahan, decimal jumlah, long hargaSatuan, int lokasi)
    {
        var po = new PurchaseOrder
        {
            PoNumber = $"PO-LAP-{Guid.NewGuid():N}"[..20],
            SupplierId = _pemasok,
            Status = PurchaseOrderStatus.Sent,
            OrderDate = DateTime.UtcNow,
            CreatedByUserId = Pengguna,
            Lines =
            {
                new PurchaseOrderLine
                {
                    IngredientId = bahan, OrderedQty = jumlah, UnitCostMinorUnits = hargaSatuan, Unit = "kg"
                }
            }
        };
        var poId = await _purchases.CreateAsync(po);
        var baris = (await _purchases.GetByIdAsync(poId))!.Lines.Single();

        var hasil = await _purchaseService.ReceiveGoodsAsync(poId, new ReceiveGoodsDto(null, Pengguna,
            new List<ReceiveGoodsLineDto> { new(baris.Id, bahan, jumlah, hargaSatuan, lokasi) }));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
    }

    /// <summary>SPK sampai selesai: bahan keluar dari lapisan dapur, hasil masuk etalase.</summary>
    private async Task<(int Id, long Hpp)> ProduksiAsync(
        int produk, int bahan, int unitBaik, long hargaBahanRencana, long tenaga, long overhead, decimal kg = 2m)
    {
        var wo = new WorkOrder
        {
            OrderNumber = $"SPK-LAP-{Guid.NewGuid():N}"[..20],
            ProductTemplateId = produk,
            TargetQuantity = unitBaik,
            LaborCost = tenaga,
            OverheadCost = overhead,
            CreatedByUserId = Pengguna
        };
        var id = await _production.CreateAsync(wo, new[]
        {
            new WorkOrderMaterial { IngredientId = bahan, StandardQty = kg, ActualQty = kg, UnitCost = hargaBahanRencana }
        });
        var materials = new[]
        {
            new WorkOrderMaterial
            {
                WorkOrderId = id, IngredientId = bahan, StandardQty = kg, ActualQty = kg, UnitCost = hargaBahanRencana
            }
        };

        long materialCost = Bulat(kg * hargaBahanRencana);
        var journal = await _accounting.BuildProductionCompletionAsync(
            id, wo.OrderNumber, materialCost, 0, tenaga, overhead, unitBaik, Pengguna);
        var hpp = ProductionCostRules.CostPerGoodUnit(materialCost, tenaga, overhead, 0, unitBaik);
        await _production.CompleteWorkOrderAsync(id, unitBaik, 0, materials, [], journal, hpp);
        return (id, hpp);
    }

    private async Task<int> PesananLunasAsync(int produk, decimal quantity, long total)
    {
        var hasil = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            "Pelanggan Lapisan", null, "Uji lapisan", DateTime.UtcNow.AddDays(1),
            total, total, produk, null, Pengguna, null, quantity));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        return hasil.Data;
    }

    private List<(decimal, long)> Lapisan(string kind, int itemId, int lokasi)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Quantity, UnitCostMinorUnits FROM StockCostLayers
             WHERE ItemKind = @k AND ItemId = @i AND LocationId = @l AND Quantity > 0
             ORDER BY Id;";
        cmd.Parameters.AddWithValue("@k", kind);
        cmd.Parameters.AddWithValue("@i", itemId);
        cmd.Parameters.AddWithValue("@l", lokasi);
        using var reader = cmd.ExecuteReader();
        var hasil = new List<(decimal, long)>();
        while (reader.Read()) hasil.Add((reader.GetDecimal(0), reader.GetInt64(1)));
        return hasil;
    }

    private long NilaiLapisan(string kind, int itemId, int lokasi = 0) =>
        Convert.ToInt64(_db.Scalar(
            "SELECT COALESCE(SUM(CAST(ROUND(Quantity * UnitCostMinorUnits) AS INTEGER)), 0) " +
            $"FROM StockCostLayers WHERE ItemKind = '{kind}' AND ItemId = {itemId}" +
            (lokasi == 0 ? ";" : $" AND LocationId = {lokasi};")));

    private long Saldo(string kodeAkun) =>
        Convert.ToInt64(_db.Scalar(@"
            SELECT COALESCE(SUM(jl.DebitAmount - jl.CreditAmount), 0)
              FROM JournalLines jl
              JOIN Accounts a ON a.Id = jl.AccountId
              JOIN JournalEntries je ON je.Id = jl.JournalEntryId
             WHERE a.Code = '" + kodeAkun + "' AND je.Status IN ('Posted','Reversed');"));

    private (long Debit, long Credit) Baris(string kode, string source, int sourceId) =>
        (Convert.ToInt64(_db.Scalar($@"
            SELECT COALESCE(SUM(jl.DebitAmount), 0)
              FROM JournalLines jl
              JOIN Accounts a ON a.Id = jl.AccountId
              JOIN JournalEntries je ON je.Id = jl.JournalEntryId
             WHERE a.Code = '{kode}' AND je.Status IN ('Posted','Reversed')
               AND je.SourceDocumentType = '{source}' AND je.SourceDocumentId = {sourceId};")),
         Convert.ToInt64(_db.Scalar($@"
            SELECT COALESCE(SUM(jl.CreditAmount), 0)
              FROM JournalLines jl
              JOIN Accounts a ON a.Id = jl.AccountId
              JOIN JournalEntries je ON je.Id = jl.JournalEntryId
             WHERE a.Code = '{kode}' AND je.Status IN ('Posted','Reversed')
               AND je.SourceDocumentType = '{source}' AND je.SourceDocumentId = {sourceId};")));

    private IntegrityCheck AmbilCek(string nama) =>
        new DatabaseIntegrity(_factory).Check().Single(c => c.Name == nama);

    private static long Bulat(decimal nilai) => (long)Math.Round(nilai, 0, MidpointRounding.AwayFromZero);

    [Fact]
    public async Task BahanKeluar_MenghabiskanLapisanTertua_BukanHargaMasterTerbaru()
    {
        var bahan = BahanBaru("Gula Lapisan", 250_000);
        await TerimaBahanAsync(bahan, 4m, 50_000, Gudang);
        await TerimaBahanAsync(bahan, 6m, 200_000, Gudang);

        await _stock.TransferStockAsync(Gudang, Dapur, bahan, 5m, Pengguna, "pindah dapur");

        // 4 kg @ Rp 500 + 1 kg @ Rp 2.000 = Rp 400. Perhitungan BUG-032 (harga master Rp 2.500/kg)
        // akan membukukan Rp 1.250 untuk barang yang sama.
        Baris("1-004", "LocationTransfer", Gudang).Debit.Should().Be(400_000);
        Baris("1-003", "LocationTransfer", Gudang).Credit.Should().Be(400_000);

        Lapisan(KindBahan, bahan, Gudang).Should().Equal((5m, 200_000L));
        Lapisan(KindBahan, bahan, Dapur).Should().Equal((4m, 50_000L), (1m, 200_000L));
        NilaiLapisan(KindBahan, bahan).Should().Be(400_000 + 1_000_000,
            "pindah lokasi memindahkan nilai, tidak menambah atau menguranginya");
    }

    [Fact]
    public async Task HargaBahanNaikSetelahTerima_TransferTetapMemakaiHargaStruk()
    {
        var bahan = BahanBaru("Mentega Lapisan", 50_000);
        await TerimaBahanAsync(bahan, 10m, 50_000, Gudang);

        // Layar Bahan Baku dipakai menaikkan harga master ke Rp 1.200/kg setelah barang diterima.
        _db.Exec($"UPDATE Ingredients SET PricePerUnit = 1200000 WHERE Id = {bahan};");

        await _stock.TransferStockAsync(Gudang, Dapur, bahan, 3m, Pengguna, null);

        Baris("1-004", "LocationTransfer", Gudang).Debit.Should().Be(150_000,
            "tiga kilogram yang pindah tetap berharga Rp 500/kg walau master sudah Rp 12.000/kg");
        Saldo("1-003").Should().Be(NilaiLapisan(KindBahan, bahan, Gudang));
    }

    [Fact]
    public async Task ProduksiDuaBatchDenganBiayaBeda_SaatPenjualanMemakaiBatchTua()
    {
        var produk = ProdukBaru("Roti Lapisan");
        var bahan = BahanBaru("Tepung Lapisan", 100_000);
        await TerimaBahanAsync(bahan, 10m, 100_000, Dapur);

        // Pagi: bahan 200.000 + tenaga kerja 100.000 untuk 5 unit -> HPP 60.000 per unit.
        var (_, hppPagi) = await ProduksiAsync(produk, bahan, 5, 100_000, tenaga: 100_000, overhead: 0);
        // Sore: tenaga kerja tidak dipakai -> HPP 40.000 per unit, harga bahan sama.
        var (_, hppSore) = await ProduksiAsync(produk, bahan, 5, 100_000, tenaga: 0, overhead: 0);
        hppPagi.Should().Be(60_000);
        hppSore.Should().Be(40_000);

        Lapisan(KindProdukJadi, produk, Etalase).Should().Equal((5m, 60_000L), (5m, 40_000L));
        var rataRata = Convert.ToInt64(_db.Scalar(
            $"SELECT AverageCostMinorUnits FROM FinishedGoodsStock WHERE ProductTemplateId = {produk};"));
        rataRata.Should().Be(50_000, "kolom rata-rata kini rata-rata tertimbang lapisan yang tersisa");

        var orderId = await PesananLunasAsync(produk, quantity: 7, total: 700_000);

        Baris("5-005", "SalesOrder", orderId).Debit.Should().Be(380_000,
            "lima unit pertama dihargai HPP pagi, dua sisanya HPP sore — bukan 7 x rata-rata 50.000");
        Baris("1-006", "SalesOrder", orderId).Credit.Should().Be(380_000);
        Lapisan(KindProdukJadi, produk, Etalase).Should().Equal((3m, 40_000L));
        NilaiLapisan(KindProdukJadi, produk).Should().Be(120_000);

        // Nilai yang dibebankan ke 5-005 tercatat per mutasi, jadi jejaknya bisa dibaca balik.
        Convert.ToInt64(_db.Scalar(
            "SELECT ValueMinorUnits FROM FinishedGoodsMovements " +
            $"WHERE MovementType = 'Sale' AND SourceId = {orderId};")).Should().Be(380_000);
    }

    [Fact]
    public async Task HargaBahanNaikDiTengahProduksi_SelisihSvkDibukukanKeBebanSelisih()
    {
        var produk = ProdukBaru("Roti Selisih");
        var bahan = BahanBaru("Tepung Selisih", 100_000);
        await TerimaBahanAsync(bahan, 5m, 200_000, Dapur);

        // SPK direncanakan dengan harga lama Rp 1.000/kg, tetapi dapur melepaskan lapisan Rp 2.000/kg.
        var (spk, hpp) = await ProduksiAsync(produk, bahan, 5, 100_000, tenaga: 0, overhead: 0);
        hpp.Should().Be(40_000, "kapitalisasi stok produk jadi memakai biaya rencana SPK");

        Baris("1-006", "WorkOrder", spk).Debit.Should().Be(200_000);
        Baris("1-004", "WorkOrder", spk).Credit.Should().Be(400_000,
            "jurnal produksi melepas biaya rencana, jurnal selisih melepas kekurangan lapisan FIFO");
        Baris("5-006", "WorkOrder", spk).Debit.Should().Be(200_000,
            "2 kg x (2.000 - 1.000) menjadi Beban Selisih Persediaan, bukan hilang tanpa jejak");

        Saldo("1-004").Should().Be(600_000);
        NilaiLapisan(KindBahan, bahan, Dapur).Should().Be(600_000);
        AmbilCek("nilai lapisan bahan sama dengan saldo akun persediaan").Passed.Should().BeTrue();
    }

    [Fact]
    public async Task RantaiLengkapBeliProduksiJual_PeriksaBasisDataTetapHijau()
    {
        var produk = ProdukBaru("Roti Rantai");
        var bahan = BahanBaru("Tepung Rantai", 100_000);
        await TerimaBahanAsync(bahan, 10m, 100_000, Gudang);
        await _stock.TransferStockAsync(Gudang, Dapur, bahan, 8m, Pengguna, "bahan produksi");
        await ProduksiAsync(produk, bahan, 4, 100_000, tenaga: 40_000, overhead: 20_000);
        await ProduksiAsync(produk, bahan, 4, 100_000, tenaga: 0, overhead: 0);
        var orderId = await PesananLunasAsync(produk, quantity: 6, total: 600_000);

        var saldoJadi = Saldo("1-006");
        var nilaiJadi = NilaiLapisan(KindProdukJadi, produk);
        saldoJadi.Should().Be(nilaiJadi, "saldo buku besar dan nilai stok menjelaskan barang yang sama");
        Saldo("1-003").Should().Be(NilaiLapisan(KindBahan, bahan, Gudang));
        Saldo("1-004").Should().Be(NilaiLapisan(KindBahan, bahan, Dapur));

        AmbilCek("lapisan biaya menutupi stok fisik").Passed.Should().BeTrue();
        AmbilCek("nilai stok produk jadi sama dengan saldo 1-006").Passed.Should().BeTrue();
        AmbilCek("nilai lapisan bahan sama dengan saldo akun persediaan").Passed.Should().BeTrue();
        AmbilCek("jurnal posted seimbang").Passed.Should().BeTrue();

        // Enam unit terjual: 4 x 65.000 (batch pagi) + 2 x 50.000 (batch sore).
        Baris("5-005", "SalesOrder", orderId).Debit.Should().Be(4 * 65_000 + 2 * 50_000);
    }

    [Fact]
    public async Task LapisanDihapusSalinanLuar_PeriksaBasisDataMenyalaMerah()
    {
        var bahan = BahanBaru("Gula Yatim", 100_000);
        await TerimaBahanAsync(bahan, 4m, 100_000, Gudang);

        var sebelum = AmbilCek("lapisan biaya menutupi stok fisik");
        sebelum.Passed.Should().BeTrue("basis data yang baru diisi lewat layar memang sehat");

        // Suntingan langsung (atau salinan yang kehilangan tabel lapisan) membuat barang di rak
        // tidak lagi punya harga: stok fisiknya ada, lapisan tidak ada.
        _db.Exec("DELETE FROM StockCostLayers WHERE ItemKind = 'Ingredient';");

        var sesudah = AmbilCek("lapisan biaya menutupi stok fisik");
        sesudah.Passed.Should().BeFalse();
        sesudah.Detail.Should().Contain("Gula Yatim").And.Contain("Gudang").And.Contain("lapisan 0");
    }

    [Fact]
    public void Migrasi019_BackfillLapisan_MempertahankanNilaiBukuSebelumnya()
    {
        // migrator hanya menjalankan versi di atas MAX(SchemaVersion), jadi migrasi terbaru harus
        // dicabut lebih dulu — kalau tidak, penurunan ke 018 tampak berhasil tapi Migrate() di bawah
        // tidak menaikkan apa pun dan tabel lapisan tetap hilang.
        MigrationRollback.Run(_db, "023_IngredientUnitConversion.down.sql");
        MigrationRollback.Run(_db, "022_PeriodCloseGuard.down.sql");
        MigrationRollback.Run(_db, "021_SupplierReturnsAndPayments.down.sql");
        MigrationRollback.Run(_db, "020_DiscountTaxAndReceivables.down.sql");
        MigrationRollback.Run(_db, "019_CostLayersForInventory.down.sql");
        _db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 "VALUES ('Cokelat Warisan', 'kg', 300000, 0, 0, 1);");
        var bahan = Convert.ToInt32(_db.Scalar("SELECT Id FROM Ingredients WHERE Name = 'Cokelat Warisan';"));
        _db.Exec($"INSERT INTO LocationStock (LocationId, IngredientId, Quantity) VALUES ({Gudang}, {bahan}, 7);");
        _db.Exec("INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                 "VALUES ('Nastar Warisan', 'Kue', 1500000, 1);");
        var produk = Convert.ToInt32(_db.Scalar("SELECT Id FROM ProductTemplates WHERE Name = 'Nastar Warisan';"));
        _db.Exec($"INSERT INTO FinishedGoodsStock (LocationId, ProductTemplateId, Quantity, AverageCostMinorUnits) " +
                 $"VALUES ({Etalase}, {produk}, 5, 61234);");

        new DatabaseMigrator(_db.ConnectionString).Migrate();

        // Nilai buku lama adalah 7 x harga master dan 5 x biaya rata-rata; backfill harus
        // memindahkan angka itu apa adanya supaya saldo jurnal tetap cocok setelah peningkatan.
        Lapisan(KindBahan, bahan, Gudang).Should().Equal((7m, 300_000L));
        NilaiLapisan(KindBahan, bahan).Should().Be(2_100_000);
        Lapisan(KindProdukJadi, produk, Etalase).Should().Equal((5m, 61_234L));
        NilaiLapisan(KindProdukJadi, produk).Should().Be(306_170);
        _db.Count("StockCostLayers", "SourceType = 'Migration019'").Should().Be(2);
    }

    [Fact]
    public async Task BahanKeluarTanpaLapisanDiatasnya_DitolakDenganPetunjukPeriksaBasisData()
    {
        var bahan = BahanBaru("Gula Tanpa Lapisan", 100_000);
        await TerimaBahanAsync(bahan, 4m, 100_000, Gudang);
        _db.Exec("DELETE FROM StockCostLayers WHERE ItemKind = 'Ingredient';");

        var act = () => _stock.TransferStockAsync(Gudang, Dapur, bahan, 1m, Pengguna, null);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Gula Tanpa Lapisan").And.Contain("Periksa Basis Data");
        _db.Count("StockMovements", $"IngredientId = {bahan} AND Quantity = -1").Should().Be(0,
            "mutasi yang ditolak tidak boleh meninggalkan kaki tunggal di buku mutasi");
        Convert.ToDecimal(_db.Scalar(
            $"SELECT Quantity FROM LocationStock WHERE IngredientId = {bahan} AND LocationId = {Gudang};"))
            .Should().Be(4m);
    }
}
