using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Alur produksi → stok produk jadi → penjualan, diuji pada basis data sungguhan karena
/// keseluruhan rantai inilah yang harus tetap konsisten saat pengguna menekan tombol.
/// </summary>
public class FinishedGoodsFlowTests
{
    private const int Display = 3;
    private const int Kitchen = 2;
    private const int Pengguna = 1;

    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;
    private readonly ProductionRepository _production;
    private readonly LocationStockRepository _stock;
    private readonly SalesOrderRepository _orders;
    private readonly JournalRepository _journals;
    private readonly OrderService _orderService;

    public FinishedGoodsFlowTests()
    {
        _factory = new DbConnectionFactory(_db.ConnectionString);
        _production = new ProductionRepository(_factory);
        _stock = new LocationStockRepository(_factory);
        _orders = new SalesOrderRepository(_factory);
        _journals = new JournalRepository(_factory);
        var accounting = new AccountingService(_journals, new AccountRepository(_factory));
        _orderService = new OrderService(
            _orders,
            _stock,
            _journals,
            new DocumentSequenceRepository(_factory),
            accounting,
            new SalesReturnRepository(_factory));
    }

    private int NewProduct(string name)
    {
        _db.Exec($"INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                 $"VALUES ('{name}', 'Roti', 1500000, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM ProductTemplates WHERE Name = '{name}';"));
    }

    private async Task<int> NewIngredientWithKitchenStock(string name, decimal qty)
    {
        _db.Exec($"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 $"VALUES ('{name}', 'kg', 100000, 0, 0, 1);");
        var id = Convert.ToInt32(_db.Scalar($"SELECT Id FROM Ingredients WHERE Name = '{name}';"));
        await _stock.ApplyAsync(new[]
        {
            new StockChange(Kitchen, id, qty, MovementType.Restock, "Test", null, Pengguna, null)
        });
        return id;
    }

    /// <summary>SPK 4 unit: 3 baik, 1 dibuang; bahan 2 kg @Rp 1.000 (sen) = 200.000.</summary>
    private async Task<int> SelesaikanSpkAsync(int product, int ingredient, int goodUnits)
    {
        var wo = new WorkOrder
        {
            OrderNumber = $"SPK-UJI-{Guid.NewGuid():N}"[..20],
            ProductTemplateId = product,
            TargetQuantity = goodUnits + 1,
            LaborCost = 40_000,
            OverheadCost = 20_000,
            CreatedByUserId = Pengguna
        };
        // WorkOrderId diisi repository dari id yang baru dibuat, jadi 0 di sini sengaja.
        var bahan = new[]
        {
            new WorkOrderMaterial { IngredientId = ingredient, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000 }
        };
        var id = await _production.CreateAsync(wo, bahan);

        var materials = new[]
        {
            new WorkOrderMaterial { WorkOrderId = id, IngredientId = ingredient, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000 }
        };
        var scraps = new[]
        {
            new WorkOrderScrap { WorkOrderId = id, Quantity = 1, ReasonCategory = "Gosong", ActionTaken = "Scrap_Loss", CreatedByUserId = Pengguna }
        };

        const long materialCost = 200_000;
        long loss = ProductionCostRules.LossCost(materialCost, 1, goodUnits + 1);
        var acct = new AccountingService(_journals, new AccountRepository(_factory));
        var journal = await acct.BuildProductionCompletionAsync(
            id, wo.OrderNumber, materialCost, loss, wo.LaborCost, wo.OverheadCost, goodUnits, Pengguna);
        long hppPerUnit = ProductionCostRules.CostPerGoodUnit(materialCost, wo.LaborCost, wo.OverheadCost, loss, goodUnits);

        await _production.CompleteWorkOrderAsync(id, goodUnits, 1, materials, scraps, journal, hppPerUnit);
        return id;
    }

    private Task<decimal> SaldoEtalaseAsync(int product) => _stock.GetFinishedGoodsTotalAsync(product);

    [Fact]
    public async Task SelesaiSPK_ProdukJadiMasukEtalaseDenganNilaiHppLengkap()
    {
        var product = NewProduct("Roti Etalase");
        var ingredient = await NewIngredientWithKitchenStock("Tepung Etalase", 10m);

        await SelesaikanSpkAsync(product, ingredient, goodUnits: 3);

        (await SaldoEtalaseAsync(product)).Should().Be(3m);
        var baris = await _stock.GetFinishedGoodsStockAsync(Display);
        baris.Single(s => s.ProductTemplateId == product).Quantity.Should().Be(3m);

        // Nilai per unit = (bahan yang lolos + tenaga kerja + overhead) / unit baik = 70.000
        var nilai = Convert.ToInt64(_db.Scalar(
            $"SELECT UnitCostMinorUnits FROM FinishedGoodsMovements WHERE ProductTemplateId = {product};"));
        nilai.Should().Be(70_000);

        // Bahan habis terpakai di dapur, bukan di etalase
        (await _stock.GetTotalStockAsync(ingredient)).Should().Be(8m);
    }

    [Fact]
    public async Task JurnalPenyelesaianSPK_MemuatTenagaKerjaDanOverheadSertaSeimbang()
    {
        var product = NewProduct("Roti Jurnal");
        var ingredient = await NewIngredientWithKitchenStock("Tepung Jurnal", 10m);
        var woId = await SelesaikanSpkAsync(product, ingredient, goodUnits: 3);

        var entries = await _journals.GetBySourceDocumentAsync("WorkOrder", woId);
        entries.Should().ContainSingle();
        var entri = entries[0];

        long debit = entri.Lines.Sum(l => l.DebitAmount);
        long kredit = entri.Lines.Sum(l => l.CreditAmount);
        debit.Should().Be(kredit, "entri produksi yang tidak seimbang merusak laporan laba rugi");

        var gaji = await AccountIdByCode("5-003");
        var operasional = await AccountIdByCode("5-002");
        var kas = await AccountIdByCode("1-001");
        entri.Lines.Should().Contain(l => l.AccountId == gaji && l.DebitAmount == 40_000);
        entri.Lines.Should().Contain(l => l.AccountId == operasional && l.DebitAmount == 20_000);
        entri.Lines.Should().Contain(l => l.AccountId == kas && l.CreditAmount == 60_000);
    }

    [Fact]
    public async Task PerubahanStatusMenjadiProses_KurangiProdukJadiBukanBahanMentah()
    {
        var product = NewProduct("Roti Jual");
        var ingredient = await NewIngredientWithKitchenStock("Tepung Jual", 10m);
        await SelesaikanSpkAsync(product, ingredient, goodUnits: 3);
        var bahanSebelum = await _stock.GetTotalStockAsync(ingredient);

        var orderId = await BuatPesananAsync("Pelanggan Jual", product, quantity: 2);
        var hasil = await _orderService.UpdateStatusAsync(orderId, OrderStatus.Processing, Pengguna);

        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        (await SaldoEtalaseAsync(product)).Should().Be(1m);
        var bahanSesudah = await _stock.GetTotalStockAsync(ingredient);
        bahanSesudah.Should().Be(bahanSebelum, "bahan sudah dikurangi saat produksi, penjualan hanya menggeser etalase");
    }

    [Fact]
    public async Task Pembatalan_KembalikanStokTepatSatuKali()
    {
        var product = NewProduct("Roti Batal");
        var ingredient = await NewIngredientWithKitchenStock("Tepung Batal", 10m);
        await SelesaikanSpkAsync(product, ingredient, goodUnits: 3);

        var orderId = await BuatPesananAsync("Pelanggan Batal", product, quantity: 2);
        (await _orderService.UpdateStatusAsync(orderId, OrderStatus.Processing, Pengguna)).Success.Should().BeTrue();
        (await SaldoEtalaseAsync(product)).Should().Be(1m);

        (await _orderService.UpdateStatusAsync(orderId, OrderStatus.Cancelled, Pengguna, "Pelanggan membatalkan")).Success
            .Should().BeTrue();
        (await SaldoEtalaseAsync(product)).Should().Be(3m);

        // Percobaan pembatalan kedua tidak boleh menambah stok lagi.
        var kedua = await _orderService.UpdateStatusAsync(orderId, OrderStatus.Cancelled, Pengguna, "dua kali");
        kedua.Success.Should().BeFalse();
        (await SaldoEtalaseAsync(product)).Should().Be(3m);
        _db.Count("FinishedGoodsMovements", $"MovementType = 'CancelRestore' AND SourceId = {orderId}").Should().Be(1);
    }

    [Fact]
    public async Task StokEtalaseKurang_StatusDanSaldoTidakBerubahSamaSekali()
    {
        var product = NewProduct("Roti Kurang");
        var ingredient = await NewIngredientWithKitchenStock("Tepung Kurang", 10m);
        await SelesaikanSpkAsync(product, ingredient, goodUnits: 3);

        var orderId = await BuatPesananAsync("Pelanggan Besar", product, quantity: 10);
        var hasil = await _orderService.UpdateStatusAsync(orderId, OrderStatus.Processing, Pengguna);

        hasil.Success.Should().BeFalse();
        hasil.ErrorMessage.Should().Contain("tidak cukup");
        (await SaldoEtalaseAsync(product)).Should().Be(3m, "mutasi yang ditolak tidak boleh meninggalkan saldo separuh jalan");
        StatusPesanan(orderId).Should().Be("Pending", "transaksi yang gagal tidak mengubah status pesanan");
    }

    [Fact]
    public async Task PesananLangsungLunas_EtalaseTerpotongSaatDibuat()
    {
        var product = NewProduct("Roti Lunas");
        var ingredient = await NewIngredientWithKitchenStock("Tepung Lunas", 10m);
        await SelesaikanSpkAsync(product, ingredient, goodUnits: 5);

        // Uang muka = total → status langsung Paid tanpa perubahan status.
        var orderId = await BuatPesananAsync("Pelanggan Tunai", product, quantity: 2, total: 300_000, dp: 300_000);

        (await SaldoEtalaseAsync(product)).Should().Be(3m);
        Convert.ToInt32(_db.Scalar($"SELECT StockDeducted FROM SalesOrders WHERE Id = {orderId};")).Should().Be(1);
    }

    [Fact]
    public async Task PelunasanLewatPembayaran_MengurangiEtalaseSekaliSaja()
    {
        var product = NewProduct("Roti Cicil");
        var ingredient = await NewIngredientWithKitchenStock("Tepung Cicil", 10m);
        await SelesaikanSpkAsync(product, ingredient, goodUnits: 5);

        var orderId = await BuatPesananAsync("Pelanggan Cicil", product, quantity: 2, total: 300_000, dp: 100_000);
        (await SaldoEtalaseAsync(product)).Should().Be(5m, "belum lunas dan belum diproses, stok belum bergerak");

        (await _orderService.AddPaymentAsync(new AddPaymentRequest(orderId, 200_000, "Tunai", Pengguna)))
            .Success.Should().BeTrue();
        (await SaldoEtalaseAsync(product)).Should().Be(3m);

        // Pesanan yang sudah lunas tidak bisa mundur ke Siap Diambil: status lunas adalah status
        // akhir. Yang dipastikan di sini adalah penolakan itu tidak memotong etalase lagi.
        var naik = await _orderService.UpdateStatusAsync(orderId, OrderStatus.Ready, Pengguna);
        naik.Success.Should().BeFalse("status Lunas tidak boleh mundur ke tahap sebelumnya");
        naik.ErrorMessage.Should().Contain("lunas");
        (await SaldoEtalaseAsync(product)).Should().Be(3m);
        _db.Count("FinishedGoodsMovements", $"MovementType = 'Sale' AND SourceId = {orderId}").Should().Be(1);
    }

    [Fact]
    public async Task JumlahPesananNol_DitolakSebelumMenyentuhBasisData()
    {
        var product = NewProduct("Roti Nol");
        var hasil = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            "Pelanggan Nol", null, "Uji", DateTime.UtcNow.AddDays(1), 100_000, 0,
            product, null, Pengguna, null, 0m));

        hasil.Success.Should().BeFalse();
        hasil.ErrorMessage.Should().Contain("harus lebih dari nol");
        _db.Count("SalesOrders", "CustomerNameSnapshot = 'Pelanggan Nol'").Should().Be(0);
    }

    [Fact]
    public async Task RingkasanHpp_CocokDenganBiayaYangDibebankanSaatSpkSelesai()
    {
        var product = NewProduct("Roti HPP");
        var ingredient = await NewIngredientWithKitchenStock("Tepung HPP", 10m);
        await SelesaikanSpkAsync(product, ingredient, goodUnits: 3);

        var summary = await new CostSummaryService(
            new ProductTemplateRepository(_factory), _production).GetAsync();
        var baris = summary.Products.Single(p => p.ProductId == product);

        var nilaiStok = Convert.ToInt64(_db.Scalar(
            $"SELECT UnitCostMinorUnits FROM FinishedGoodsMovements WHERE ProductTemplateId = {product};"));
        nilaiStok.Should().Be(70_000);
        baris.ActualHppMinorUnits.Should().Be(nilaiStok,
            "HPP di layar harus sama dengan biaya yang benar-benar dibebankan ke stok produk jadi");
        baris.CompletedWorkOrders.Should().Be(1);
        baris.GoodUnits.Should().Be(3);
        baris.ScrapLossMinorUnits.Should().Be(50_000,
            "1 dari 4 unit dibuang, jadi seperempat bahan Rp200.000 menjadi beban — bukan bagian HPP");
        summary.ScrapLossMinorUnits.Should().Be(50_000);
        summary.WeightedActualHppMinorUnits.Should().Be(70_000);
    }

    [Fact]
    public async Task RingkasanHpp_MenghitungSeluruhSpkSelesai_TanpaBatasDaftarTerbaru()
    {
        var product = NewProduct("Roti Banyak");
        _db.Exec("WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x < 205) " +
                 "INSERT INTO WorkOrders (OrderNumber, ProductTemplateId, TargetQuantity, Status, " +
                 "ActualGoodQty, ActualFailedQty, TotalMaterialCost, LaborCost, OverheadCost) " +
                 $"SELECT 'SPK-BANYAK-' || x, {product}, 1, 'Completed', 1, 0, 0, 10000, 0 FROM n;");

        var summary = await new CostSummaryService(
            new ProductTemplateRepository(_factory), _production).GetAsync();
        var baris = summary.Products.Single(p => p.ProductId == product);

        baris.CompletedWorkOrders.Should().Be(205,
            "ringkasan tidak boleh diam-diam hanya memuat 200 SPK terbaru");
        baris.GoodUnits.Should().Be(205);
        baris.ActualHppMinorUnits.Should().Be(10_000);
        summary.CompletedWorkOrders.Should().Be(205);
    }

    [Fact]
    public async Task MetodePembayaranDiLuarTunaiTransfer_DitolakTanpaMencatatPembayaran()
    {
        var product = NewProduct("Roti Metode");
        var orderId = await BuatPesananAsync("Pelanggan Metode", product, quantity: 1, total: 300_000);

        var hasil = await _orderService.AddPaymentAsync(new AddPaymentRequest(orderId, 100_000, "Cash", Pengguna));
        hasil.Success.Should().BeFalse();
        hasil.ErrorMessage.Should().Contain("Tunai");
        _db.Count("SalesPayments", $"SalesOrderId = {orderId}").Should().Be(0);

        // Ejaan berbeda huruf besar-kecil tetap diterima, tetapi disimpan dalam bentuk baku
        // supaya rekap kas per metode tidak terpecah.
        (await _orderService.AddPaymentAsync(new AddPaymentRequest(orderId, 100_000, "transfer", Pengguna)))
            .Success.Should().BeTrue();
        ((string)_db.Scalar($"SELECT Method FROM SalesPayments WHERE SalesOrderId = {orderId};")!)
            .Should().Be("Transfer");
    }

    private async Task<int> BuatPesananAsync(
        string nama, int product, decimal quantity, long total = 500_000, long dp = 0)
    {
        var hasil = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            nama, null, "Uji integrasi", DateTime.UtcNow.AddDays(1), total, dp,
            product, null, Pengguna, null, quantity));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        return hasil.Data;
    }

    private string StatusPesanan(int orderId) =>
        (string)_db.Scalar($"SELECT Status FROM SalesOrders WHERE Id = {orderId};")!;

    private async Task<int> AccountIdByCode(string code)
    {
        var akun = await new AccountRepository(_factory).GetByCodeAsync(code);
        akun.Should().NotBeNull($"bagan akun harus punya {code} sebelum jurnal produksi bisa diuji");
        return akun!.Id;
    }
}
