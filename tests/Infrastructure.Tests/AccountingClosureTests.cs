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
    private async Task<long> ProduksiAsync(int produk, int bahan, int goodUnits)
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
        return hpp;
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
        var hpp = await ProduksiAsync(produk, bahan, goodUnits: 5);
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

    [Fact]
    public async Task Retur_MengembalikanStokDenganHppSaatTerjual_BukanResepHariIni()
    {
        var bahan = BahanBaru("Bahan HPP Retur", 100_000);
        var produk = ProdukBaru("Roti HPP Retur");
        var hpp = await ProduksiAsync(produk, bahan, goodUnits: 5);
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
}
