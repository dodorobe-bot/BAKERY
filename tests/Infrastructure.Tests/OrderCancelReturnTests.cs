using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Pembatalan pesanan setelah sebagian unit diretur. Sebelum diperbaiki, pembatalan mengembalikan
/// seluruh jumlah pesanan (stok hantu sebesar unit yang sudah diretur) dan membalik jurnal
/// penjualan penuh, sementara jurnal retur tidak ikut terbalik sehingga pendapatan menjadi negatif.
/// </summary>
public class OrderCancelReturnTests
{
    private const int Display = 3;
    private const int Pengguna = 1;

    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;
    private readonly ProductionRepository _production;
    private readonly LocationStockRepository _stock;
    private readonly SalesOrderRepository _orders;
    private readonly SalesReturnRepository _returns;
    private readonly JournalRepository _journals;
    private readonly AccountingService _accounting;
    private readonly OrderService _orderService;
    private readonly SalesReturnService _returnService;

    public OrderCancelReturnTests()
    {
        _factory = new DbConnectionFactory(_db.ConnectionString);
        _production = new ProductionRepository(_factory);
        _stock = new LocationStockRepository(_factory);
        _orders = new SalesOrderRepository(_factory);
        _returns = new SalesReturnRepository(_factory);
        _journals = new JournalRepository(_factory);
        _accounting = new AccountingService(_journals, new AccountRepository(_factory));
        _orderService = new OrderService(
            _orders, new CustomerRepository(_factory), _stock, _journals,
            new DocumentSequenceRepository(_factory), _accounting, _returns);
        _returnService = new SalesReturnService(
            _orders, _returns, new ProductTemplateRepository(_factory), _stock,
            new DocumentSequenceRepository(_factory), _accounting);
    }

    private int NewProduct(string name)
    {
        _db.Exec($"INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                 $"VALUES ('{name}', 'Roti', 1500000, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM ProductTemplates WHERE Name = '{name}';"));
    }

    /// <summary>SPK 5 unit baik; bahan 2 kg @Rp 1.000 (sen) = 200.000.</summary>
    private async Task<int> IsiEtalaseAsync(int product, int goodUnits)
    {
        _db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 "VALUES ('Tepung Batal', 'kg', 100000, 0, 0, 1);");
        var ingredient = Convert.ToInt32(_db.Scalar("SELECT Id FROM Ingredients WHERE Name = 'Tepung Batal';"));
        await _stock.ApplyAsync(new[]
        {
            new StockChange(2, ingredient, 5m, MovementType.Restock, "Test", null, Pengguna, null)
        });

        var wo = new WorkOrder
        {
            OrderNumber = $"SPK-BATAL-{Guid.NewGuid():N}"[..20],
            ProductTemplateId = product,
            TargetQuantity = goodUnits,
            LaborCost = 40_000,
            OverheadCost = 20_000,
            CreatedByUserId = Pengguna
        };
        var id = await _production.CreateAsync(wo, new[]
        {
            new WorkOrderMaterial { IngredientId = ingredient, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000 }
        });
        var materials = new[]
        {
            new WorkOrderMaterial { WorkOrderId = id, IngredientId = ingredient, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000 }
        };
        const long materialCost = 200_000;
        var journal = await _accounting.BuildProductionCompletionAsync(
            id, wo.OrderNumber, materialCost, 0, wo.LaborCost, wo.OverheadCost, Pengguna);
        long hpp = ProductionCostRules.CostPerGoodUnit(materialCost, wo.LaborCost, wo.OverheadCost, 0, goodUnits);
        await _production.CompleteWorkOrderAsync(id, goodUnits, 0, materials, [], journal, hpp);
        return id;
    }

    private async Task<int> BuatPesananLunasAsync(int product, decimal quantity, long total)
    {
        var hasil = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            "Pelanggan Batal", null, "Uji pembatalan", DateTime.UtcNow.AddDays(1),
            total, total, product, null, Pengguna, null, quantity));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        return hasil.Data;
    }

    [Fact]
    public async Task BatalSetelahRetur_Ditolak_DanStokTidakBertambahLagi()
    {
        var product = NewProduct("Roti Retur Batal");
        await IsiEtalaseAsync(product, goodUnits: 5);
        var orderId = await BuatPesananLunasAsync(product, quantity: 2, total: 300_000);
        (await _stock.GetFinishedGoodsTotalAsync(product)).Should().Be(3m);

        var retur = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 150_000, "Rusak saat diantar", "Tunai", Pengguna));
        retur.Success.Should().BeTrue(retur.ErrorMessage);
        (await _stock.GetFinishedGoodsTotalAsync(product)).Should().Be(4m);

        var batal = await _orderService.UpdateStatusAsync(orderId, OrderStatus.Cancelled, Pengguna, "uji");

        batal.Success.Should().BeFalse("pesanan yang sudah diretur tidak boleh dibatalkan dengan pengembalian stok penuh");
        batal.ErrorMessage.Should().Contain("retur");
        (await _stock.GetFinishedGoodsTotalAsync(product)).Should().Be(4m, "tidak ada unit yang dikembalikan dua kali");
        _db.Count("FinishedGoodsMovements", $"MovementType = 'CancelRestore' AND SourceId = {orderId}").Should().Be(0);
        _db.Scalar($"SELECT Status FROM SalesOrders WHERE Id = {orderId};").Should().Be("Paid");
    }

    [Fact]
    public async Task BatalTanpaRetur_MengembalikanStokSekali_DanJurnalKembaliNol()
    {
        var product = NewProduct("Roti Batal Murni");
        await IsiEtalaseAsync(product, goodUnits: 5);

        // Titik acuan kas diambil sebelum pesanan: SPK juga menyentuh kas (tenaga kerja dan
        // overhead), jadi yang dibandingkan adalah kembali tidaknya kas ke keadaan sebelum pesanan.
        var hari = BusinessTime.Now().Date;
        var arusAwal = await _journals.GetCashFlowAsync(hari, "1-001");
        var harianAwal = await _journals.GetDailyTotalsAsync(hari.AddDays(-1), hari.AddDays(1));
        var saldoAwal = await _journals.GetAccountBalanceAsync("4-001");

        var orderId = await BuatPesananLunasAsync(product, quantity: 2, total: 300_000);
        (await _stock.GetFinishedGoodsTotalAsync(product)).Should().Be(3m);

        var batal = await _orderService.UpdateStatusAsync(orderId, OrderStatus.Cancelled, Pengguna, "pelanggan batal");
        batal.Success.Should().BeTrue(batal.ErrorMessage);

        (await _stock.GetFinishedGoodsTotalAsync(product)).Should().Be(5m);
        _db.Count("FinishedGoodsMovements", $"MovementType = 'CancelRestore' AND SourceId = {orderId}").Should().Be(1);

        // Entri asli berstatus Reversed dan pembaliknya Posted. Penjumlahan harus memuat keduanya,
        // jadi pendapatan, kas, dan rekap harian kembali ke keadaan sebelum pesanan — bukan minus.
        var arus = await _journals.GetCashFlowAsync(hari, "1-001");
        (arus.Masuk - arus.Keluar).Should().Be(arusAwal.Masuk - arusAwal.Keluar,
            "kas bersih kembali seperti sebelum pesanan dibuat");
        (arus.Masuk - arusAwal.Masuk).Should().Be(300_000, "uang pesanan tetap tercatat masuk sekali");
        (arus.Keluar - arusAwal.Keluar).Should().Be(300_000, "uang pesanan dikembalikan sekali");

        var harian = await _journals.GetDailyTotalsAsync(hari.AddDays(-1), hari.AddDays(1));
        harian.Sum(h => h.Pendapatan).Should().Be(harianAwal.Sum(h => h.Pendapatan),
            "pendapatan pesanan yang dibatalkan tidak boleh tersisa sebagai angka negatif");
        harian.Sum(h => h.Beban).Should().Be(harianAwal.Sum(h => h.Beban),
            "pembatalan tidak menambah atau mengurangi beban");

        var rentang = await _journals.GetRangeSummaryAsync(hari.AddDays(-1), hari.AddDays(1), null);
        var pendapatan = rentang.Accounts.Single(a => a.Code == "4-001");
        pendapatan.Debit.Should().Be(pendapatan.Credit);

        var saldo = await _journals.GetAccountBalanceAsync("4-001");
        saldo.Should().Be(saldoAwal, "neraca dan laba-rugi harus memakai aturan yang sama");
    }
}
