using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Balapan dua permintaan pada satu pesanan/PO. Semua uji memakai basis data SQLite sungguhan
/// dengan dua koneksi, karena yang diuji adalah keputusan baca-tulis di dalam transaksi.
/// </summary>
public class ConcurrencyAndAtomicityTests
{
    private const int Display = 3;
    private const int Kitchen = 2;
    private const int Warehouse = 1;
    private const int Pengguna = 1;

    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;
    private readonly SalesOrderRepository _orders;
    private readonly SalesReturnRepository _returns;
    private readonly LocationStockRepository _stock;
    private readonly JournalRepository _journals;
    private readonly AccountingService _accounting;
    private readonly OrderService _orderService;
    private readonly PurchaseOrderRepository _purchases;
    private readonly PurchaseService _purchaseService;
    private readonly ProductionRepository _production;

    public ConcurrencyAndAtomicityTests()
    {
        _factory = new DbConnectionFactory(_db.ConnectionString);
        _orders = new SalesOrderRepository(_factory);
        _returns = new SalesReturnRepository(_factory);
        _stock = new LocationStockRepository(_factory);
        _journals = new JournalRepository(_factory);
        _accounting = new AccountingService(_journals, new AccountRepository(_factory));
        _orderService = new OrderService(
            _orders, _stock, _journals,
            new DocumentSequenceRepository(_factory), _accounting, _returns,
            new SettingRepository(_factory));
        _purchases = new PurchaseOrderRepository(_factory);
        _purchaseService = new PurchaseService(_purchases, _stock, _accounting);
        _production = new ProductionRepository(_factory);
    }

    [Fact]
    public async Task DuaPelunasanBersamaan_HanyaSatuTercatat_DanEtalaseTerpotongSekali()
    {
        var produk = await ProdukDenganEtalaseAsync("Roti Serentak", 5);
        var orderId = await BuatPesananAsync("Pelanggan Serentak", produk, quantity: 2, total: 300_000);

        var hasil = await Task.WhenAll(
            _orderService.AddPaymentAsync(new AddPaymentRequest(orderId, 300_000, "Tunai", Pengguna, "a")),
            _orderService.AddPaymentAsync(new AddPaymentRequest(orderId, 300_000, "Tunai", Pengguna, "b")));

        hasil.Count(r => r.Success).Should().Be(1,
            "dua pelunasan penuh pada pesanan yang sama tidak boleh sama-sama diterima");
        _db.Count("SalesPayments", $"SalesOrderId = {orderId}").Should().Be(1);
        _db.Scalar($"SELECT Status FROM SalesOrders WHERE Id = {orderId};").Should().Be("Paid");
        _db.Count("FinishedGoodsMovements", $"MovementType = 'Sale' AND SourceId = {orderId}").Should().Be(1,
            "stok hanya boleh dikurangi sekali meski dua pembayaran datang bersamaan");
        (await _stock.GetFinishedGoodsTotalAsync(produk)).Should().Be(3m);
    }

    [Fact]
    public async Task DuaPerubahanStatusBersamaan_StokTerpotongSekali()
    {
        var produk = await ProdukDenganEtalaseAsync("Roti Status", 5);
        var orderId = await BuatPesananAsync("Pelanggan Status", produk, quantity: 2, total: 300_000);

        var hasil = await Task.WhenAll(
            _orderService.UpdateStatusAsync(orderId, OrderStatus.Processing, Pengguna),
            _orderService.UpdateStatusAsync(orderId, OrderStatus.Processing, Pengguna));

        hasil.Should().Contain(r => r.Success);
        _db.Scalar($"SELECT Status FROM SalesOrders WHERE Id = {orderId};").Should().Be("Processing");
        _db.Count("FinishedGoodsMovements", $"MovementType = 'Sale' AND SourceId = {orderId}").Should().Be(1,
            "perubahan status kembar tidak boleh memotong etalase dua kali");
        (await _stock.GetFinishedGoodsTotalAsync(produk)).Should().Be(3m);
    }

    [Fact]
    public async Task PerubahanStatusDariSalinanBasi_Ditolak_TanpaMengubahStok()
    {
        var produk = await ProdukDenganEtalaseAsync("Roti Basi", 5);
        var orderId = await BuatPesananAsync("Pelanggan Basi", produk, quantity: 2, total: 300_000);
        var basi = await _orders.GetByIdAsync(orderId);

        (await _orderService.UpdateStatusAsync(orderId, OrderStatus.Processing, Pengguna)).Success.Should().BeTrue();

        // Meniru layar yang masih memegang pesanan Pending saat pengguna lain sudah memprosesnya.
        var aksi = async () => await _orders.SaveStatusChangeWithJournalAsync(
            basi!, basi!.Status, basi.StockDeducted, basi.TotalPaid(),
            [],
            [new FinishedGoodsChange(Display, produk, -2m, FinishedGoodsMovementType.Sale,
                "SalesOrder", orderId, Pengguna, "salinan basi")],
            []);

        await aksi.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*berubah di perangkat lain*");
        (await _stock.GetFinishedGoodsTotalAsync(produk)).Should().Be(3m);
        _db.Count("FinishedGoodsMovements", $"MovementType = 'Sale' AND SourceId = {orderId}").Should().Be(1);
    }

    [Fact]
    public async Task PesananBersamaanNomorBaruSama_SatuPelanggan_PoinTidakHilang()
    {
        var telepon = $"0812{Guid.NewGuid():N}"[..14];

        var hasil = await Task.WhenAll(
            _orderService.CreateOrderAsync(new CreateOrderRequest(
                "Pelanggan A", telepon, "Uji serentak", DateTime.UtcNow.AddDays(1),
                1_000_000, 0, null, null, Pengguna)),
            _orderService.CreateOrderAsync(new CreateOrderRequest(
                "Pelanggan B", telepon, "Uji serentak", DateTime.UtcNow.AddDays(1),
                2_000_000, 0, null, null, Pengguna)));

        hasil.Should().OnlyContain(r => r.Success,
            "nomor telepon yang sama tidak boleh membuat pesanan kedua gagal karena UNIQUE");
        _db.Count("Customers", $"Phone = '{telepon}'").Should().Be(1);
        Convert.ToInt32(_db.Scalar($"SELECT LoyaltyPoints FROM Customers WHERE Phone = '{telepon}';"))
            .Should().Be(3, "1 poin per Rp10.000 untuk 1 juta dan 2 juta, tanpa ada yang tertimpa");
        _db.Count("SalesOrders", $"PhoneSnapshot = '{telepon}' AND CustomerId IS NOT NULL").Should().Be(2);
    }

    [Fact]
    public async Task DuaPenerimaanBersamaan_SisaTerimaHanyaMasukSekali_PoStatusDihitungUlang()
    {
        _db.Exec("INSERT INTO Suppliers (Name, IsActive) VALUES ('Pemasok Serentak', 1);");
        var supplier = Convert.ToInt32(_db.Scalar("SELECT Id FROM Suppliers WHERE Name = 'Pemasok Serentak';"));
        _db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 "VALUES ('Bahan Serentak', 'kg', 100000, 0, 0, 1);");
        var bahan = Convert.ToInt32(_db.Scalar("SELECT Id FROM Ingredients WHERE Name = 'Bahan Serentak';"));

        var poId = await _purchases.CreateAsync(new PurchaseOrder
        {
            PoNumber = "PO-SERENTAK-1",
            SupplierId = supplier,
            Status = PurchaseOrderStatus.Sent,
            OrderDate = DateTime.UtcNow,
            CreatedByUserId = Pengguna,
            Lines = { new PurchaseOrderLine { IngredientId = bahan, OrderedQty = 10m, UnitCostMinorUnits = 100_000, Unit = "kg" } }
        });
        var baris = (await _purchases.GetByIdAsync(poId))!.Lines.Single();

        ReceiveGoodsDto Permintaan() => new(null, Pengguna, new List<ReceiveGoodsLineDto>
        {
            new(baris.Id, bahan, 10m, 100_000, Warehouse)
        });

        var hasil = await Task.WhenAll(
            _purchaseService.ReceiveGoodsAsync(poId, Permintaan()),
            _purchaseService.ReceiveGoodsAsync(poId, Permintaan()));

        hasil.Count(r => r.Success).Should().Be(1, "sisa terima tidak boleh masuk dua kali");
        Convert.ToDecimal(_db.Scalar($"SELECT ReceivedQty FROM PurchaseOrderLines WHERE Id = {baris.Id};"))
            .Should().Be(10m);
        _db.Scalar($"SELECT Status FROM PurchaseOrders WHERE Id = {poId};").Should().Be("Received",
            "status PO dihitung dari jumlah diterima yang tersimpan, bukan dari salinan sebelum transaksi");
        (await _stock.GetTotalStockAsync(bahan)).Should().Be(10m);
        _db.Count("GoodsReceipts", $"PurchaseOrderId = {poId}").Should().Be(1);
    }

    [Fact]
    public async Task BatalPo_SetelahSebagianDiterima_DitolakRepository()
    {
        _db.Exec("INSERT INTO Suppliers (Name, IsActive) VALUES ('Pemasok Batal', 1);");
        var supplier = Convert.ToInt32(_db.Scalar("SELECT Id FROM Suppliers WHERE Name = 'Pemasok Batal';"));
        _db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 "VALUES ('Bahan Batal', 'kg', 100000, 0, 0, 1);");
        var bahan = Convert.ToInt32(_db.Scalar("SELECT Id FROM Ingredients WHERE Name = 'Bahan Batal';"));

        var poId = await _purchases.CreateAsync(new PurchaseOrder
        {
            PoNumber = "PO-BATAL-1",
            SupplierId = supplier,
            Status = PurchaseOrderStatus.Sent,
            OrderDate = DateTime.UtcNow,
            CreatedByUserId = Pengguna,
            Lines = { new PurchaseOrderLine { IngredientId = bahan, OrderedQty = 10m, UnitCostMinorUnits = 100_000, Unit = "kg" } }
        });
        var baris = (await _purchases.GetByIdAsync(poId))!.Lines.Single();
        (await _purchaseService.ReceiveGoodsAsync(poId, new ReceiveGoodsDto(null, Pengguna,
            new List<ReceiveGoodsLineDto> { new(baris.Id, bahan, 4m, 100_000, Warehouse) })))
            .Success.Should().BeTrue();

        var aksi = async () => await _purchases.CancelAsync(poId);

        await aksi.Should().ThrowAsync<InvalidOperationException>().WithMessage("*retur pembelian*");
        _db.Scalar($"SELECT Status FROM PurchaseOrders WHERE Id = {poId};").Should().Be("PartialReceived",
            "pembatalan yang ditolak tidak boleh meninggalkan status Cancelled");
    }

    [Fact]
    public async Task ReturPadaPesananYangSudahDibatalkan_DitolakDiDalamTransaksi()
    {
        var produk = await ProdukDenganEtalaseAsync("Roti Retur Batal", 5);
        var orderId = await BuatPesananAsync("Pelanggan Retur Batal", produk, quantity: 2, total: 300_000);
        (await _orderService.UpdateStatusAsync(orderId, OrderStatus.Cancelled, Pengguna, "uji")).Success
            .Should().BeTrue();

        var ret = new SalesReturn
        {
            ReturnNumber = $"RET-{Guid.NewGuid():N}"[..16],
            SalesOrderId = orderId,
            ProductTemplateId = produk,
            Quantity = 1m,
            Amount = 150_000,
            Reason = "Uji balapan",
            RefundMethod = "Tunai",
            ReturnedAt = DateTime.UtcNow,
            CreatedByUserId = Pengguna
        };

        var aksi = async () => await _returns.CreateWithJournalAsync(ret, null, []);

        await aksi.Should().ThrowAsync<InvalidOperationException>().WithMessage("*dibatalkan*");
        _db.Count("SalesReturns", $"SalesOrderId = {orderId}").Should().Be(0);
    }

    private async Task<int> ProdukDenganEtalaseAsync(string nama, decimal unit)
    {
        _db.Exec($"INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                 $"VALUES ('{nama}', 'Roti', 1000000, 1);");
        var produk = Convert.ToInt32(_db.Scalar($"SELECT Id FROM ProductTemplates WHERE Name = '{nama}';"));
        _db.Exec($"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 $"VALUES ('Bahan {nama}', 'kg', 100000, 0, 0, 1);");
        var bahan = Convert.ToInt32(_db.Scalar($"SELECT Id FROM Ingredients WHERE Name = 'Bahan {nama}';"));
        await _stock.ApplyAsync([
            new StockChange(Kitchen, bahan, 10m, MovementType.Restock, "Test", null, Pengguna, null)
        ]);

        var target = (int)Math.Ceiling(unit);
        var wo = new WorkOrder
        {
            OrderNumber = $"SPK-{Guid.NewGuid():N}"[..20],
            ProductTemplateId = produk,
            TargetQuantity = target,
            LaborCost = 0,
            OverheadCost = 0,
            CreatedByUserId = Pengguna
        };
        var woId = await _production.CreateAsync(wo, [
            new WorkOrderMaterial { IngredientId = bahan, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000 }
        ]);
        var materials = new[]
        {
            new WorkOrderMaterial
            {
                WorkOrderId = woId, IngredientId = bahan,
                StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000
            }
        };
        const long materialCost = 200_000;
        var journal = await _accounting.BuildProductionCompletionAsync(
            woId, wo.OrderNumber, materialCost, 0, 0, 0, target, Pengguna);
        var hpp = ProductionCostRules.CostPerGoodUnit(materialCost, 0, 0, 0, target);
        await _production.CompleteWorkOrderAsync(woId, target, 0, materials, [], journal, hpp);
        return produk;
    }

    private async Task<int> BuatPesananAsync(
        string nama, int product, decimal quantity, long total, long dp = 0)
    {
        var hasil = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            nama, null, "Uji integrasi", DateTime.UtcNow.AddDays(1), total, dp,
            product, null, Pengguna, null, quantity));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        return hasil.Data;
    }
}
