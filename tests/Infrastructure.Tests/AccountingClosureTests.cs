using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Penutupan jalur jurnal yang dulu bolong: penjualan tidak membebankan harga pokok,
/// transfer dan opname tidak menyentuh buku besar, dan retur menilai stok kembali dengan
/// resep hari ini alih-alih biaya saat barang terjual. Semua angka diperiksa dari baris
/// jurnal yang benar-benar tersimpan, bukan dari objek yang baru dibentuk.
/// </summary>
public class AccountingClosureTests
{
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
    private readonly SalesReturnService _returnService;
    private readonly PurchaseService _purchaseService;

    public AccountingClosureTests()
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
            new DocumentSequenceRepository(_factory), _accounting, _returns);
        _returnService = new SalesReturnService(
            _orders, _returns, _stock,
            new DocumentSequenceRepository(_factory), _accounting);
        _purchaseService = new PurchaseService(
            _purchases, _stock, _accounting);
    }

    private int BahanBaru(string nama, long harga)
    {
        _db.Exec($"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 $"VALUES ('{nama}', 'kg', {harga}, 0, 0, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM Ingredients WHERE Name = '{nama}';"));
    }

    private int ProdukBaru(string nama)
    {
        _db.Exec($"INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                 $"VALUES ('{nama}', 'Roti', 1500000, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM ProductTemplates WHERE Name = '{nama}';"));
    }

    /// <summary>SPK 5 unit baik; bahan 2 kg @ Rp 1.000 = 200.000, tenaga 40.000, overhead 20.000.</summary>
    private async Task<(int Id, long Hpp)> ProduksiAsync(int produk, int bahan, int goodUnits)
    {
        await _stock.ApplyAsync(new[]
        {
            new StockChange(Dapur, bahan, 5m, MovementType.Restock, "Test", null, Pengguna, null)
        });

        var wo = new WorkOrder
        {
            OrderNumber = $"SPK-HPP-{Guid.NewGuid():N}"[..20],
            ProductTemplateId = produk,
            TargetQuantity = goodUnits,
            LaborCost = 40_000,
            OverheadCost = 20_000,
            CreatedByUserId = Pengguna
        };
        var id = await _production.CreateAsync(wo, new[]
        {
            new WorkOrderMaterial { IngredientId = bahan, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000 }
        });
        var materials = new[]
        {
            new WorkOrderMaterial { WorkOrderId = id, IngredientId = bahan, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000 }
        };
        const long materialCost = 200_000;
        var journal = await _accounting.BuildProductionCompletionAsync(
            id, wo.OrderNumber, materialCost, 0, wo.LaborCost, wo.OverheadCost, goodUnits, Pengguna);
        var hpp = ProductionCostRules.CostPerGoodUnit(
            materialCost, wo.LaborCost, wo.OverheadCost, 0, goodUnits);
        await _production.CompleteWorkOrderAsync(id, goodUnits, 0, materials, [], journal, hpp);
        return (id, hpp);
    }

    private async Task<int> PesananLunasAsync(int produk, decimal quantity, long total)
    {
        var hasil = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            "Pelanggan Jurnal", null, "Uji jurnal", DateTime.UtcNow.AddDays(1),
            total, total, produk, null, Pengguna, null, quantity));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        return hasil.Data;
    }

    /// <summary>
    /// Jumlah debit/kredit akun pada satu dokumen. Entri Reversed ikut dijumlahkan bersama
    /// pembaliknya, sama seperti laporan buku besar, supaya efek bersihnya terlihat.
    /// </summary>
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

    [Fact]
    public async Task PenjualanLunas_MembukukanHpp_DanPembatalanMenutupKembali()
    {
        var bahan = BahanBaru("Bahan HPP Jual", 100_000);
        var produk = ProdukBaru("Roti HPP Jual");
        var (_, hpp) = await ProduksiAsync(produk, bahan, goodUnits: 5);
        hpp.Should().Be(52_000);

        var orderId = await PesananLunasAsync(produk, quantity: 2, total: 300_000);

        var beban = Baris("5-005", "SalesOrder", orderId);
        beban.Debit.Should().Be(104_000, "HPP 2 unit = 2 × 52.000");
        beban.Credit.Should().Be(0);
        Baris("1-006", "SalesOrder", orderId).Credit.Should().Be(104_000,
            "persediaan produk jadi berkurang sebesar harga pokok yang dibebankan");

        var batal = await _orderService.UpdateStatusAsync(orderId, OrderStatus.Cancelled, Pengguna, "uji");
        batal.Success.Should().BeTrue(batal.ErrorMessage);

        Baris("5-005", "SalesOrder", orderId).Should().Be((104_000L, 104_000L),
            "pembalik mengkredit beban pokok sebesar yang tadi didebit, jadi efek bersihnya nol");
        Baris("1-006", "SalesOrder", orderId).Should().Be((104_000L, 104_000L),
            "nilai persediaan kembali seperti sebelum penjualan");
    }

    /// <summary>
    /// Nilai stok produk jadi persis seperti yang dipakai menu Inventaris: jumlah fisik dikali
    /// biaya rata-rata per unit yang tersimpan, bukan angka yang dihitung ulang di tes.
    /// </summary>
    private (decimal Quantity, long Value) NilaiStokJadi(int produk)
    {
        var jumlah = Convert.ToDecimal(_db.Scalar(
            $"SELECT COALESCE(SUM(Quantity), 0) FROM FinishedGoodsStock WHERE ProductTemplateId = {produk};"));
        var nilai = Convert.ToInt64(_db.Scalar(
            "SELECT COALESCE(SUM(CAST(ROUND(Quantity * AverageCostMinorUnits) AS INTEGER)), 0) " +
            $"FROM FinishedGoodsStock WHERE ProductTemplateId = {produk};"));
        return (jumlah, nilai);
    }

    [Fact]
    public async Task JurnalProduksi_SamaDenganNilaiStokProdukJadi()
    {
        var bahan = BahanBaru("Bahan Kapitalisasi", 100_000);
        var produk = ProdukBaru("Roti Kapitalisasi");

        var (woId, hpp) = await ProduksiAsync(produk, bahan, goodUnits: 5);
        hpp.Should().Be(52_000);

        // Bahan 200.000 + tenaga 40.000 + overhead 20.000 semuanya menempel di stok.
        Baris("1-006", "WorkOrder", woId).Should().Be((260_000L, 0L),
            "persediaan produk jadi harus didebit sebesar HPP penuh, bukan hanya bahan");
        Baris("1-004", "WorkOrder", woId).Credit.Should().Be(200_000);
        Baris("1-001", "WorkOrder", woId).Credit.Should().Be(60_000,
            "tenaga kerja dan overhead tetap dibayar tunai saat produksi");
        Baris("5-003", "WorkOrder", woId).Debit.Should().Be(0,
            "tenaga kerja yang sudah masuk stok tidak boleh dibebankan dua kali");
        Baris("5-002", "WorkOrder", woId).Debit.Should().Be(0,
            "overhead yang sudah masuk stok tidak boleh dibebankan dua kali");

        NilaiStokJadi(produk).Should().Be((5m, 260_000L),
            "saldo buku besar 1-006 dan nilai stok fisik harus memakai angka yang sama");
    }

    [Fact]
    public async Task PenjualanSeluruhStok_MenutupSaldoPersediaanJadiTanpaSelisih()
    {
        var bahan = BahanBaru("Bahan Habis Jual", 100_000);
        var produk = ProdukBaru("Roti Habis Jual");

        var (woId, hpp) = await ProduksiAsync(produk, bahan, goodUnits: 5);
        var orderId = await PesananLunasAsync(produk, quantity: 5, total: 500_000);

        Baris("5-005", "SalesOrder", orderId).Debit.Should().Be(hpp * 5,
            "HPP penjualan memakai biaya penuh yang tadi dikapitalisasi");
        Baris("1-006", "SalesOrder", orderId).Credit.Should().Be(260_000);
        NilaiStokJadi(produk).Should().Be((0m, 0L));

        Baris("1-006", "WorkOrder", woId).Debit.Should().Be(
            Baris("1-006", "SalesOrder", orderId).Credit,
            "stok yang terjual habis harus meninggalkan saldo nol di akun persediaan produk jadi");
    }

    [Fact]
    public async Task HppPerUnitTidakBulat_TetapSeimbangDanSamaDenganNilaiStok()
    {
        var bahan = BahanBaru("Bahan Pembulatan", 100_000);
        var produk = ProdukBaru("Roti Pembulatan");

        // 260.000 / 6 unit = 43.333,33 -> biaya per unit 43.333, nilai stok 259.998.
        var (woId, hpp) = await ProduksiAsync(produk, bahan, goodUnits: 6);
        hpp.Should().Be(43_333);

        Baris("1-006", "WorkOrder", woId).Debit.Should().Be(259_998);
        Baris("5-006", "WorkOrder", woId).Debit.Should().Be(2,
            "selisih pembulatan biaya per unit dibukukan, bukan dibiarkan menggantung");
        NilaiStokJadi(produk).Should().Be((6m, 259_998L));

        var debit = Convert.ToInt64(_db.Scalar(
            "SELECT COALESCE(SUM(jl.DebitAmount), 0) FROM JournalLines jl " +
            "JOIN JournalEntries je ON je.Id = jl.JournalEntryId " +
            $"WHERE je.SourceDocumentType = 'WorkOrder' AND je.SourceDocumentId = {woId};"));
        var kredit = Convert.ToInt64(_db.Scalar(
            "SELECT COALESCE(SUM(jl.CreditAmount), 0) FROM JournalLines jl " +
            "JOIN JournalEntries je ON je.Id = jl.JournalEntryId " +
            $"WHERE je.SourceDocumentType = 'WorkOrder' AND je.SourceDocumentId = {woId};"));
        debit.Should().Be(kredit);
    }

    [Fact]
    public async Task Retur_MengembalikanStokDenganHppSaatTerjual_BukanResepHariIni()
    {
        var bahan = BahanBaru("Bahan HPP Retur", 100_000);
        var produk = ProdukBaru("Roti HPP Retur");
        var (_, hpp) = await ProduksiAsync(produk, bahan, goodUnits: 5);
        var orderId = await PesananLunasAsync(produk, quantity: 2, total: 300_000);

        // Harga bahan naik setelah penjualan: retur tidak boleh memakai harga baru.
        _db.Exec($"UPDATE Ingredients SET PricePerUnit = 1000000 WHERE Id = {bahan};");

        var retur = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 150_000, "Rusak saat diantar", "Tunai", Pengguna));
        retur.Success.Should().BeTrue(retur.ErrorMessage);

        Convert.ToInt64(_db.Scalar(
            "SELECT UnitCostMinorUnits FROM FinishedGoodsMovements " +
            $"WHERE MovementType = 'Return' AND SourceId = {retur.Data} ORDER BY Id DESC LIMIT 1;"))
            .Should().Be(hpp, "stok kembali pada biaya saat terjual, bukan harga resep yang sudah berubah");

        Baris("1-006", "SalesReturn", retur.Data).Debit.Should().Be(hpp);
        Baris("5-005", "SalesReturn", retur.Data).Credit.Should().Be(hpp);

        // Pesanan tersisa 1 unit yang masih terjual: sisa beban = 52.000, bukan 104.000.
        Baris("5-005", "SalesOrder", orderId).Should().Be((104_000L, 0L));
        (Baris("5-005", "SalesOrder", orderId).Debit - Baris("5-005", "SalesReturn", retur.Data).Credit)
            .Should().Be(hpp);
    }

    [Fact]
    public async Task Transfer_MenulisJurnalDrDapurCrGudang_DanNolDitolak()
    {
        var bahan = BahanBaru("Bahan Transfer", 100_000);
        await _stock.ApplyAsync(new[]
        {
            new StockChange(Gudang, bahan, 5m, MovementType.Restock, "Test", null, Pengguna, null)
        });

        await _stock.TransferStockAsync(Gudang, Dapur, bahan, 2m, Pengguna, "produksi pagi");

        var gudang = Baris("1-003", "LocationTransfer", Gudang);
        var dapur = Baris("1-004", "LocationTransfer", Gudang);
        dapur.Debit.Should().Be(200_000);
        gudang.Credit.Should().Be(200_000);
        dapur.Debit.Should().Be(gudang.Credit, "nilai yang keluar dari gudang sama dengan yang masuk dapur");

        var gerakSebelum = _db.Count("StockMovements");
        var nol = () => _stock.TransferStockAsync(Gudang, Dapur, bahan, 0m, Pengguna, null);
        await nol.Should().ThrowAsync<InvalidOperationException>();
        _db.Count("StockMovements").Should().Be(gerakSebelum, "transfer nol bukan keberhasilan diam-diam");
    }

    [Fact]
    public async Task Opname_MenulisSelisihKeBebanPersediaan_DuaArah()
    {
        var bahan = BahanBaru("Bahan Opname", 100_000);
        await _stock.ApplyAsync(new[]
        {
            new StockChange(Gudang, bahan, 5m, MovementType.Restock, "Test", null, Pengguna, null)
        });

        await _stock.AdjustStockAsync(Gudang, bahan, 3m, Pengguna, "rusak dimakan hama");
        Baris("5-006", "StockAdjustment", 1).Debit.Should().Be(200_000,
            "selisih kurang 2 kg × Rp 1.000 dibebankan ke beban selisih persediaan");
        Baris("1-003", "StockAdjustment", 1).Credit.Should().Be(200_000);

        await _stock.AdjustStockAsync(Gudang, bahan, 4m, Pengguna, "hasil hitung ulang");
        Baris("1-003", "StockAdjustment", 1).Debit.Should().Be(100_000,
            "selisih lebih menambah persediaan dan mengurangi beban selisih");
        Baris("5-006", "StockAdjustment", 1).Credit.Should().Be(100_000);
    }

    [Fact]
    public async Task Penerimaan_DidebitSesuaiLokasiTujuan_BukanSelaluGudang()
    {
        _db.Exec("INSERT INTO Suppliers (Name, IsActive) VALUES ('Pemasok Jurnal', 1);");
        var supplier = Convert.ToInt32(_db.Scalar("SELECT Id FROM Suppliers WHERE Name = 'Pemasok Jurnal';"));
        var bahanDapur = BahanBaru("Bahan Dapur Terima", 50_000);
        var bahanGudang = BahanBaru("Bahan Gudang Terima", 50_000);

        var po = new PurchaseOrder
        {
            PoNumber = "PO-JURNAL-1",
            SupplierId = supplier,
            Status = PurchaseOrderStatus.Sent,
            OrderDate = DateTime.UtcNow,
            CreatedByUserId = Pengguna,
            Lines =
            {
                new PurchaseOrderLine { IngredientId = bahanDapur, OrderedQty = 2m, UnitCostMinorUnits = 50_000, Unit = "kg" },
                new PurchaseOrderLine { IngredientId = bahanGudang, OrderedQty = 3m, UnitCostMinorUnits = 50_000, Unit = "kg" }
            }
        };
        var poId = await _purchases.CreateAsync(po);
        var tersimpan = await _purchases.GetByIdAsync(poId);
        var barisDapur = tersimpan!.Lines.Single(l => l.IngredientId == bahanDapur);
        var barisGudang = tersimpan.Lines.Single(l => l.IngredientId == bahanGudang);

        var hasil = await _purchaseService.ReceiveGoodsAsync(poId, new ReceiveGoodsDto(null, Pengguna, new List<ReceiveGoodsLineDto>
        {
            new(barisDapur.Id, bahanDapur, 2m, 50_000, Dapur),
            new(barisGudang.Id, bahanGudang, 3m, 50_000, Gudang)
        }));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);

        var receiptId = Convert.ToInt32(_db.Scalar("SELECT Id FROM GoodsReceipts ORDER BY Id DESC LIMIT 1;"));
        _db.Count("StockMovements", $"SourceType = 'GoodsReceipt' AND SourceId = {receiptId}")
            .Should().Be(2, "setiap mutasi penerimaan harus menunjuk struk penerimaan yang sama dengan jurnal");
        _db.Count("StockMovements",
                $"SourceId = {receiptId} AND LocationId = {Dapur} AND IngredientId = {bahanDapur} AND Quantity = 2")
            .Should().Be(1, "lokasi mutasi harus sama dengan lokasi tujuan baris penerimaan");
        Baris("1-004", "GoodsReceipt", receiptId).Debit.Should().Be(100_000,
            "bahan yang langsung masuk dapur didebit ke persediaan dapur");
        Baris("1-003", "GoodsReceipt", receiptId).Debit.Should().Be(150_000);
        Baris("2-002", "GoodsReceipt", receiptId).Credit.Should().Be(250_000);

        // Etalase bukan tempat bahan baku: penerimaan ke sana harus ditolak sebelum menyentuh stok.
        var po2 = new PurchaseOrder
        {
            PoNumber = "PO-JURNAL-2",
            SupplierId = supplier,
            Status = PurchaseOrderStatus.Sent,
            OrderDate = DateTime.UtcNow,
            CreatedByUserId = Pengguna,
            Lines =
            {
                new PurchaseOrderLine { IngredientId = bahanGudang, OrderedQty = 1m, UnitCostMinorUnits = 50_000, Unit = "kg" }
            }
        };
        var po2Id = await _purchases.CreateAsync(po2);
        var baris2 = (await _purchases.GetByIdAsync(po2Id))!.Lines.Single();
        var tolak = await _purchaseService.ReceiveGoodsAsync(po2Id, new ReceiveGoodsDto(null, Pengguna, new List<ReceiveGoodsLineDto>
        {
            new(baris2.Id, bahanGudang, 1m, 50_000, Etalase)
        }));
        tolak.Success.Should().BeFalse();
        tolak.ErrorMessage.Should().Contain("gudang atau dapur");
        _db.Count("GoodsReceipts", $"PurchaseOrderId = {po2Id}").Should().Be(0);
    }

    private int JurnalLayarBahan(int bahan) =>
        _db.Count("JournalEntries", $"SourceDocumentType = 'IngredientMaster' AND SourceDocumentId = {bahan}");

    /// <summary>
    /// Stok awal yang diketik di layar Bahan Baku adalah barang yang sudah ada di rak, tapi
    /// jalurnya dulu hanya menggerakkan LocationStock. Hasilnya di basis data nyata: 8 bahan
    /// bernilai Rp 5,7 juta tidak pernah masuk buku besar dan saldo 1-003 tetap negatif, sementara
    /// seluruh pemeriksaan mandiri tetap hijau karena tidak ada jurnal yang tidak seimbang.
    /// </summary>
    [Fact]
    public async Task StokAwalLayarBahan_MembukukanPersediaanLawanModal()
    {
        var bahan = BahanBaru("Bahan Stok Awal", 300_000);

        await _stock.ApplyIngredientMasterStockAsync(Gudang, bahan, 10m, stokAwal: true, Pengguna, "Stok awal bahan baku");

        Baris("1-003", "IngredientMaster", bahan).Should().Be((3_000_000L, 0L),
            "10 kg dengan harga Rp 3.000 per kg masuk sebagai persediaan gudang");
        Baris("3-001", "IngredientMaster", bahan).Credit.Should().Be(3_000_000,
            "pasangannya modal, bukan beban: barangnya tidak dibeli dan tidak dipakai hari itu");
        Baris("5-006", "IngredientMaster", bahan).Should().Be((0L, 0L),
            "stok awal tidak boleh muncul sebagai penghematan di laporan laba rugi");
        JurnalLayarBahan(bahan).Should().Be(1);
        _db.Count("StockMovements",
            $"IngredientId = {bahan} AND MovementType = 'Restock' AND SourceType = 'IngredientMaster'")
            .Should().Be(1);
    }

    [Fact]
    public async Task KoreksiStokLayarBahan_MelawanBebanSelisihSepertiOpname()
    {
        var bahan = BahanBaru("Bahan Koreksi", 300_000);
        await _stock.ApplyIngredientMasterStockAsync(Gudang, bahan, 10m, stokAwal: true, Pengguna, "Stok awal bahan baku");

        await _stock.ApplyIngredientMasterStockAsync(Gudang, bahan, -2m, stokAwal: false, Pengguna, "Penyesuaian dari layar bahan baku");

        Baris("1-003", "IngredientMaster", bahan).Should().Be((3_000_000L, 600_000L));
        Baris("5-006", "IngredientMaster", bahan).Debit.Should().Be(600_000,
            "koreksi total stok adalah selisih persediaan, sama seperti hasil opname");
        _db.Count("StockMovements",
            $"IngredientId = {bahan} AND MovementType = 'Adjustment' AND SourceType = 'IngredientMaster'")
            .Should().Be(1);
    }

    [Fact]
    public async Task LayarBahan_SelisihNol_TidakMenulisMutasiMaupunJurnal()
    {
        var bahan = BahanBaru("Bahan Tanpa Selisih", 300_000);

        await _stock.ApplyIngredientMasterStockAsync(Gudang, bahan, 0m, stokAwal: false, Pengguna, "Penyesuaian dari layar bahan baku");

        _db.Count("StockMovements", $"IngredientId = {bahan}").Should().Be(0);
        JurnalLayarBahan(bahan).Should().Be(0);
    }

    /// <summary>
    /// Jurnal ditulis di transaksi mutasi yang sama, jadi penolakan stok tidak boleh menyisakan
    /// entri yang memotong persediaan untuk barang yang tidak pernah keluar.
    /// </summary>
    [Fact]
    public async Task KoreksiStokLayarBahan_YangMenurunkanStokDiBawahNol_RollbackTermasukJurnal()
    {
        var bahan = BahanBaru("Bahan Overdraft", 300_000);
        await _stock.ApplyIngredientMasterStockAsync(Gudang, bahan, 5m, stokAwal: true, Pengguna, "Stok awal bahan baku");
        var jurnalSebelum = JurnalLayarBahan(bahan);

        var act = () => _stock.ApplyIngredientMasterStockAsync(
            Gudang, bahan, -6m, stokAwal: false, Pengguna, "Penyesuaian dari layar bahan baku");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _stock.GetTotalStockAsync(bahan)).Should().Be(5m);
        JurnalLayarBahan(bahan).Should().Be(jurnalSebelum,
            "mutasi dan jurnal bergerak bersama atau tidak sama sekali");
    }
}
