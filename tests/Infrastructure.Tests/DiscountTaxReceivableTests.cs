using System.Globalization;
using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Kasus B2a-B2d, B3a-B3d, B4a-B4c, B6a-B6b, B7a-B7b: diskon, PPN, kembalian, dan piutang usaha
/// dihitung serta dibukukan server.
///
/// Angka Acuan-nya sama dengan lembar uji angka (paket Rp 130.000, diskon 10%, PPN 11%) dan sudah
/// diukur hidup lewat probe-b.mjs di DB kosong. Tes ini menjaga angka itu tetap sama lewat jalur
/// layanan: snapshot pesanan, jurnal pengakuan, uang muka vs piutang, dan pembalik retur.
/// </summary>
public class DiscountTaxReceivableTests
{
    private const int Pengguna = 1;

    // Nilai barang Rp 130.000; semua nominal di bawah dalam sen.
    private const long NilaiBarang = 13_000_000;
    private const long Diskon = 1_300_000;        // 10%
    private const long Ppn = 1_287_000;           // 11% x 117.000
    private const long Tagihan = 12_987_000;      // 117.000 + 12.870
    private const long Pendapatan = 13_000_000;   // tagihan - PPN + diskon
    private const long UangMuka = 5_000_000;      // Rp 50.000
    private const long Piutang = 7_987_000;       // Rp 79.870 = tagihan - uang muka

    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;
    private readonly ProductionRepository _production;
    private readonly LocationStockRepository _stock;
    private readonly SalesOrderRepository _orders;
    private readonly SalesReturnRepository _returns;
    private readonly JournalRepository _journals;
    private readonly SettingRepository _settings;
    private readonly AccountingService _accounting;
    private readonly OrderService _orderService;
    private readonly SalesReturnService _returnService;

    public DiscountTaxReceivableTests()
    {
        _factory = new DbConnectionFactory(_db.ConnectionString);
        _production = new ProductionRepository(_factory);
        _stock = new LocationStockRepository(_factory);
        _orders = new SalesOrderRepository(_factory);
        _returns = new SalesReturnRepository(_factory);
        _journals = new JournalRepository(_factory);
        _settings = new SettingRepository(_factory);
        _accounting = new AccountingService(_journals, new AccountRepository(_factory));
        _orderService = new OrderService(
            _orders, _stock, _journals,
            new DocumentSequenceRepository(_factory), _accounting, _returns, _settings);
        _returnService = new SalesReturnService(
            _orders, _returns, _stock,
            new DocumentSequenceRepository(_factory), _accounting);
    }

    private Task SetPajakAsync(bool aktif, double tarif, bool inklusif) =>
        _settings.SaveManyAsync(new Dictionary<string, string>
        {
            [TaxSettings.EnabledKey] = aktif ? "1" : "0",
            [TaxSettings.RateKey] = tarif.ToString(CultureInfo.InvariantCulture),
            [TaxSettings.InclusiveKey] = inklusif ? "1" : "0"
        });

    private int ProdukBaru(string nama, long hargaJual)
    {
        _db.Exec($"INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                 $"VALUES ('{nama}', 'Roti', {hargaJual}, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM ProductTemplates WHERE Name = '{nama}';"));
    }

    /// <summary>
    /// Etalase diisi lewat SPK sungguhan: bahan 2 kg @Rp 1.000 (200.000 sen) tanpa tenaga kerja,
    /// jadi HPP per unit baik = 200.000 / unitBaik. Retur mengambil nilai ini, bukan HPP katalog.
    /// </summary>
    private async Task IsiEtalaseAsync(int produk, int unitBaik)
    {
        _db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 "VALUES ('Tepung Harga', 'kg', 100000, 0, 0, 1);");
        var bahan = Convert.ToInt32(_db.Scalar("SELECT Id FROM Ingredients WHERE Name = 'Tepung Harga';"));
        await _stock.ApplyAsync(new[]
        {
            new StockChange(2, bahan, 5m, MovementType.Restock, "Test", null, Pengguna, null)
        });

        var wo = new WorkOrder
        {
            OrderNumber = $"SPK-HRG-{Guid.NewGuid():N}"[..20],
            ProductTemplateId = produk,
            TargetQuantity = unitBaik,
            LaborCost = 0,
            OverheadCost = 0,
            CreatedByUserId = Pengguna
        };
        var id = await _production.CreateAsync(wo, new[]
        {
            new WorkOrderMaterial { IngredientId = bahan, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000 }
        });
        var materials = new[]
        {
            new WorkOrderMaterial
            {
                WorkOrderId = id, IngredientId = bahan, StandardQty = 2m, ActualQty = 2m, UnitCost = 100_000
            }
        };
        const long biayaBahan = 200_000;
        var jurnal = await _accounting.BuildProductionCompletionAsync(
            id, wo.OrderNumber, biayaBahan, 0, 0, 0, unitBaik, Pengguna);
        long hpp = ProductionCostRules.CostPerGoodUnit(biayaBahan, 0, 0, 0, unitBaik);
        await _production.CompleteWorkOrderAsync(id, unitBaik, 0, materials, [], jurnal, hpp);
    }

    private async Task<SalesOrder> MuatAsync(int orderId) => (await _orders.GetByIdAsync(orderId))!;

    /// <summary>
    /// Saldo debit - kredit atas entri yang berlaku (Posted dan Reversed dihitung). Arah ini
    /// sengaja bukan "saldo normal akun": untuk akun pasif/pendapatan nilainya jadi negatif
    /// ketika bertambah. Pembalikan pendapatan dan PPN karena itu menaikkan angkanya di sini.
    /// </summary>
    private long Saldo(string kodeAkun) =>
        Convert.ToInt64(_db.Scalar(@"
            SELECT COALESCE(SUM(jl.DebitAmount - jl.CreditAmount), 0)
              FROM JournalLines jl
              JOIN Accounts a ON a.Id = jl.AccountId
              JOIN JournalEntries je ON je.Id = jl.JournalEntryId
             WHERE a.Code = '" + kodeAkun + "' AND je.Status IN ('Posted','Reversed');"));

    private (long Debit, long Credit) Baris(string kode, string sumber, int sumberId) =>
        (Convert.ToInt64(_db.Scalar($@"
            SELECT COALESCE(SUM(jl.DebitAmount), 0)
              FROM JournalLines jl
              JOIN Accounts a ON a.Id = jl.AccountId
              JOIN JournalEntries je ON je.Id = jl.JournalEntryId
             WHERE a.Code = '{kode}' AND je.Status IN ('Posted','Reversed')
               AND je.SourceDocumentType = '{sumber}' AND je.SourceDocumentId = {sumberId};")),
         Convert.ToInt64(_db.Scalar($@"
            SELECT COALESCE(SUM(jl.CreditAmount), 0)
              FROM JournalLines jl
              JOIN Accounts a ON a.Id = jl.AccountId
              JOIN JournalEntries je ON je.Id = jl.JournalEntryId
             WHERE a.Code = '{kode}' AND je.Status IN ('Posted','Reversed')
               AND je.SourceDocumentType = '{sumber}' AND je.SourceDocumentId = {sumberId};")));

    private async Task<int> PesanAsync(
        int produk, decimal jumlah, long nilaiBarang, long uangMuka = 0,
        long? diskonNominal = null, double? diskonPersen = null)
    {
        var hasil = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            "Bu Ani", null, "Uji diskon & PPN", DateTime.UtcNow.AddDays(1),
            nilaiBarang, uangMuka, produk, null, Pengguna, null, jumlah,
            diskonNominal, diskonPersen));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        return hasil.Data;
    }

    // ===== B2a + B2b + B3a + B3b =====

    [Fact]
    public async Task DiskonPersenDanNominal_DisimpanSamaDanPemicuMenjagaRumus()
    {
        await SetPajakAsync(true, 11, false);
        var produk = ProdukBaru("Paket B1", 13_000_000);
        await IsiEtalaseAsync(produk, 5);

        var persen = await MuatAsync(await PesanAsync(produk, 1m, NilaiBarang, diskonPersen: 10));
        var nominal = await MuatAsync(await PesanAsync(produk, 1m, NilaiBarang, diskonNominal: Diskon));

        foreach (var order in new[] { persen, nominal })
        {
            order.SubtotalAmount.Should().Be(NilaiBarang);
            order.DiscountAmount.Should().Be(Diskon, "Rp 13.000 — Acuan B2a/B2b");
            order.TaxAmount.Should().Be(Ppn, "11% x Rp 117.000 = Rp 12.870 — Acuan B3a");
            order.TotalAmount.Should().Be(Tagihan, "Acuan B3b");
            order.TaxRatePercent.Should().Be(11);
            order.TaxInclusive.Should().BeFalse();
        }
        persen.TotalAmount.Should().Be(nominal.TotalAmount);

        // PPN tidak menambah pendapatan: 4-001 tetap harga katalog, sisanya jadi kewajiban.
        (Tagihan - Ppn + Diskon).Should().Be(NilaiBarang);

        // Penulisan yang melanggar rumus pemicu harus ditolak basis data, bukan diterima diam-diam.
        var melanggar = Record.Exception(() => _db.Exec(
            $"UPDATE SalesOrders SET TotalAmount = TotalAmount + 100 WHERE Id = {persen.Id};"));
        melanggar.Should().BeOfType<Microsoft.Data.Sqlite.SqliteException>();
        melanggar!.Message.Should().Contain("Total pesanan harus sama dengan subtotal");
        Convert.ToInt64(_db.Scalar(
            $"SELECT TotalAmount FROM SalesOrders WHERE Id = {persen.Id};")).Should().Be(Tagihan);
    }

    [Fact]
    public async Task DuaBentukDiskonSerentak_DanDiskonMelebihiNilaiBarang_Ditolak()
    {
        await SetPajakAsync(true, 11, false);
        var produk = ProdukBaru("Paket Kontrol", 13_000_000);
        await IsiEtalaseAsync(produk, 5);

        var dua = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            "Bu Ani", null, "Kontrol diskon", DateTime.UtcNow.AddDays(1), NilaiBarang, 0,
            produk, null, Pengguna, null, 1m, Diskon, 10));
        dua.Success.Should().BeFalse("kasir tidak boleh diberi dua diskon lalu salah satu dipakai diam-diam");
        dua.ErrorMessage.Should().Contain("Pilih salah satu diskon");

        var berlebihan = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            "Bu Ani", null, "Kontrol diskon", DateTime.UtcNow.AddDays(1), NilaiBarang, 0,
            produk, null, Pengguna, null, 1m, 20_000_000, null));
        berlebihan.Success.Should().BeFalse();
        berlebihan.ErrorMessage.Should().Contain("melebihi nilai barang");

        var negatif = await _orderService.CreateOrderAsync(new CreateOrderRequest(
            "Bu Ani", null, "Kontrol diskon", DateTime.UtcNow.AddDays(1), NilaiBarang, 0,
            produk, null, Pengguna, null, 1m, -1_000, null));
        negatif.Success.Should().BeFalse();
        negatif.ErrorMessage.Should().Contain("tidak boleh negatif");

        _db.Count("SalesOrders").Should().Be(0, "pesanan yang ditolak tidak meninggalkan baris");
    }

    // ===== B3c + B3d =====

    [Fact]
    public async Task PpnInklusif_TagihanTidakBertambahTetapiPajakTetapTerhutang()
    {
        await SetPajakAsync(true, 11, true);
        var produk = ProdukBaru("Paket Inklusif", 13_000_000);
        await IsiEtalaseAsync(produk, 5);

        var order = await MuatAsync(await PesanAsync(produk, 1m, NilaiBarang, diskonPersen: 10));

        order.TotalAmount.Should().Be(11_700_000, "Rp 117.000 sudah memuat PPN");
        order.TaxAmount.Should().Be(1_159_500, "Acuan B3d: Rp 11.595");
        order.TaxInclusive.Should().BeTrue();

        var lunas = await _orderService.AddPaymentAsync(new AddPaymentRequest(
            order.Id, order.TotalAmount, "Tunai", Pengguna));
        lunas.Success.Should().BeTrue(lunas.ErrorMessage);

        Baris("4-001", "SalesOrder", order.Id).Credit.Should().Be(11_840_500,
            "pendapatan = tagihan - PPN + diskon = 117.000 - 11.595 + 13.000");
        Baris("2-003", "SalesOrder", order.Id).Credit.Should().Be(1_159_500,
            "Acuan B3c: DPP Rp 105.405 dan PPN-nya berdiri sebagai kewajiban walau uangnya tidak bertambah");
        (order.TotalAmount - order.TaxAmount).Should().Be(10_540_500);
    }

    [Fact]
    public async Task TarifNaik_PesananLamaKekalSnapshot_PesananBaruMemakaiTarifBaru()
    {
        await SetPajakAsync(true, 11, false);
        var produk = ProdukBaru("Paket Snapshot", 13_000_000);
        await IsiEtalaseAsync(produk, 5);

        var lama = await MuatAsync(await PesanAsync(produk, 1m, NilaiBarang, diskonPersen: 10));

        await SetPajakAsync(true, 12, false);
        var baru = await MuatAsync(await PesanAsync(produk, 1m, NilaiBarang, diskonPersen: 10));

        lama.TaxAmount.Should().Be(1_287_000, "Acuan F3: pesanan lama tetap Rp 12.870");
        lama.TotalAmount.Should().Be(Tagihan);
        baru.TaxAmount.Should().Be(1_404_000, "12% x Rp 117.000 = Rp 14.040 untuk transaksi baru");
        baru.TotalAmount.Should().Be(13_104_000);
        baru.TaxRatePercent.Should().Be(12);
    }

    [Fact]
    public async Task PpnMati_TanpaBarisUtangPpnSamaSekali()
    {
        await SetPajakAsync(false, 11, false);
        var produk = ProdukBaru("Paket Tanpa Pajak", 13_000_000);
        await IsiEtalaseAsync(produk, 5);

        var order = await MuatAsync(await PesanAsync(produk, 1m, NilaiBarang, diskonPersen: 10));
        var lunas = await _orderService.AddPaymentAsync(new AddPaymentRequest(
            order.Id, order.TotalAmount, "Tunai", Pengguna));
        lunas.Success.Should().BeTrue(lunas.ErrorMessage);

        order.TaxAmount.Should().Be(0);
        order.TaxRatePercent.Should().BeNull("bukan tarif nol: pesanan ini memang tidak ber-PPN");
        order.TotalAmount.Should().Be(11_700_000);
        Baris("2-003", "SalesOrder", order.Id).Credit.Should().Be(0);
        Saldo("2-003").Should().Be(0);
    }

    // ===== B4a + B4b + B4c =====

    [Fact]
    public async Task KembalianTunai_KeluarDariLaciTetapiTidakPernahMasukBuku()
    {
        await SetPajakAsync(true, 11, false);
        var produk = ProdukBaru("Paket Tunai", 13_000_000);
        await IsiEtalaseAsync(produk, 5);
        var kasSebelum = Saldo("1-001");

        var order = await MuatAsync(await PesanAsync(produk, 1m, NilaiBarang, diskonPersen: 10));
        var hasil = await _orderService.AddPaymentAsync(new AddPaymentRequest(
            order.Id, Tagihan, "Tunai", Pengguna, null, null, 15_000_000));

        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        Convert.ToInt64(_db.Scalar(
            $"SELECT ChangeAmount FROM SalesPayments WHERE SalesOrderId = {order.Id};"))
            .Should().Be(2_013_000, "Acuan B4a: Rp 150.000 - Rp 129.870 = Rp 20.130 kembalian");

        (Saldo("1-001") - kasSebelum).Should().Be(Tagihan,
            "Acuan B4b: kas bertambah sebesar tagihan, bukan sebesar uang yang diserahkan");
        Convert.ToInt64(_db.Scalar(
            "SELECT COUNT(*) FROM JournalLines WHERE DebitAmount = 15000000 OR CreditAmount = 15000000;"))
            .Should().Be(0, "kembalian tidak pernah dibukukan");
    }

    [Fact]
    public async Task UangDiserahkanKurangAtauUntukTransfer_DitolakTanpaMenulisPembayaran()
    {
        await SetPajakAsync(true, 11, false);
        var produk = ProdukBaru("Paket Tolak Tender", 13_000_000);
        await IsiEtalaseAsync(produk, 5);
        var kasSebelum = Saldo("1-001");

        var order = await MuatAsync(await PesanAsync(produk, 1m, NilaiBarang, diskonPersen: 10));

        var kurang = await _orderService.AddPaymentAsync(new AddPaymentRequest(
            order.Id, Tagihan, "Tunai", Pengguna, null, null, 12_000_000));
        kurang.Success.Should().BeFalse();
        kurang.ErrorMessage.Should().Contain("kurang dari pembayaran");

        var transfer = await _orderService.AddPaymentAsync(new AddPaymentRequest(
            order.Id, Tagihan, "Transfer", Pengguna, null, null, 15_000_000));
        transfer.Success.Should().BeFalse("transfer datang persis sebesar yang masuk, tidak ada kembalian");
        transfer.ErrorMessage.Should().Contain("hanya diisi untuk pembayaran tunai");

        var lebih = await _orderService.AddPaymentAsync(new AddPaymentRequest(
            order.Id, Tagihan + 100_000, "Tunai", Pengguna));
        lebih.Success.Should().BeFalse();
        lebih.ErrorMessage.Should().Contain("melebihi sisa bayar");

        _db.Count("SalesPayments", $"SalesOrderId = {order.Id}").Should().Be(0);
        Saldo("1-001").Should().Be(kasSebelum);
        (await MuatAsync(order.Id)).Status.Should().Be(OrderStatus.Pending);
    }

    // ===== B7a + B7b =====

    [Fact]
    public async Task JualKredit_SaatDiserahkanPiutangLahir_DanPelunasanTidakMengakuiPendapatanLagi()
    {
        await SetPajakAsync(true, 11, false);
        var produk = ProdukBaru("Paket Kredit", 13_000_000);
        await IsiEtalaseAsync(produk, 5);
        var etalaseSebelum = await _stock.GetFinishedGoodsTotalAsync(produk);

        var orderId = await PesanAsync(produk, 1m, NilaiBarang, UangMuka, diskonPersen: 10);
        var order = await MuatAsync(orderId);

        order.Status.Should().Be(OrderStatus.Pending);
        order.RevenueRecognizedAt.Should().BeNull("uang muka hanyalah titipan selama barang belum diserahkan");
        Saldo("2-001").Should().Be(-UangMuka, "kredit 2-001 sebesar Rp 50.000 (saldo dibaca debit-kredit)");
        Saldo("1-002").Should().Be(0, "piutang belum boleh berdiri sebelum barang diserahkan");
        Baris("4-001", "SalesOrder", orderId).Credit.Should().Be(0);

        var serah = await _orderService.MarkDeliveredAsync(orderId, Pengguna);
        serah.Success.Should().BeTrue(serah.ErrorMessage);

        Saldo("1-002").Should().Be(Piutang, "Acuan B7a: Rp 129.870 - Rp 50.000 = Rp 79.870 piutang usaha");
        Saldo("2-001").Should().Be(0, "uang muka ditukar jadi bagian dari pengakuan pendapatan");
        Baris("4-001", "SalesOrder", orderId).Credit.Should().Be(Pendapatan,
            "pendapatan dikredit sebesar harga katalog Rp 130.000, bukan tagihan Rp 129.870");
        Baris("2-003", "SalesOrder", orderId).Credit.Should().Be(Ppn);
        Baris("4-002", "SalesOrder", orderId).Debit.Should().Be(Diskon,
            "diskon dibukukan di 4-002 supaya harga katalog aslinya masih terlihat");
        (await MuatAsync(orderId)).DeliveredAt.Should().NotBeNull();
        (await _stock.GetFinishedGoodsTotalAsync(produk)).Should().Be(etalaseSebelum - 1m);

        var serahLagi = await _orderService.MarkDeliveredAsync(orderId, Pengguna);
        serahLagi.Success.Should().BeFalse();
        serahLagi.ErrorMessage.Should().Contain("sudah diserahkan");
        Saldo("1-002").Should().Be(Piutang, "penyerahan ganda tidak boleh melipatgandakan piutang");
        (await _stock.GetFinishedGoodsTotalAsync(produk)).Should().Be(etalaseSebelum - 1m);

        var lunas = await _orderService.AddPaymentAsync(new AddPaymentRequest(
            orderId, Piutang, "Tunai", Pengguna));
        lunas.Success.Should().BeTrue(lunas.ErrorMessage);

        Saldo("1-002").Should().Be(0, "Acuan B7b: pelunasan memadamkan piutang, bukan menambah pendapatan");
        Baris("2-001", "SalesOrder", orderId).Credit.Should().Be(UangMuka,
            "setelah pendapatan diakui, uang masuk melunasi 1-002 dan tidak pernah ditahan lagi di 2-001");
        Baris("4-001", "SalesOrder", orderId).Credit.Should().Be(Pendapatan,
            "pengakuan pendapatan hanya terjadi sekali");
        (await MuatAsync(orderId)).Status.Should().Be(OrderStatus.Paid);
        (await _stock.GetFinishedGoodsTotalAsync(produk)).Should().Be(etalaseSebelum - 1m,
            "pembayaran terakhir tidak memotong etalase untuk kedua kalinya");
    }

    [Fact]
    public async Task BayarBertahap_SisaTagihanTetapSatuAngka()
    {
        await SetPajakAsync(true, 11, false);
        var produk = ProdukBaru("Paket Campuran", 13_000_000);
        await IsiEtalaseAsync(produk, 5);

        var orderId = await PesanAsync(produk, 1m, NilaiBarang, UangMuka, diskonPersen: 10);
        var serah = await _orderService.MarkDeliveredAsync(orderId, Pengguna);
        serah.Success.Should().BeTrue(serah.ErrorMessage);

        var cicil = await _orderService.AddPaymentAsync(new AddPaymentRequest(orderId, 3_000_000, "Tunai", Pengguna));
        cicil.Success.Should().BeTrue(cicil.ErrorMessage);
        var order = await MuatAsync(orderId);
        order.Status.Should().Be(OrderStatus.Pending);
        order.RemainingAmount().Should().Be(Tagihan - UangMuka - 3_000_000);
        Saldo("1-002").Should().Be(Piutang - 3_000_000,
            "piutang berdiri penuh saat penyerahan, lalu mengecil seukuran uang yang masuk — bukan 2-001 yang menumpuk");
        Saldo("2-001").Should().Be(0);

        var sisa = await _orderService.AddPaymentAsync(new AddPaymentRequest(
            orderId, order.RemainingAmount(), "Transfer", Pengguna));
        sisa.Success.Should().BeTrue(sisa.ErrorMessage);
        (await MuatAsync(orderId)).RemainingAmount().Should().Be(0);
        Saldo("1-002").Should().Be(0);
    }

    // ===== B6a + B6b =====

    [Fact]
    public async Task ReturSatuUnit_MenarikDiskonDanPpnSertaMengembalikanHppYangDibebankan()
    {
        await SetPajakAsync(true, 11, false);
        var produk = ProdukBaru("Roti Harga", 6_000_000);
        await IsiEtalaseAsync(produk, 5);

        var orderId = await PesanAsync(produk, 3m, 6_000_000, uangMuka: 5_994_000, diskonPersen: 10);
        var order = await MuatAsync(orderId);
        order.SubtotalAmount.Should().Be(6_000_000);
        order.DiscountAmount.Should().Be(600_000);
        order.TaxAmount.Should().Be(594_000);
        order.TotalAmount.Should().Be(5_994_000, "Rp 59.940 untuk 3 roti");

        var salah = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 2_000_000, "satu roti basi", "Tunai", Pengguna));
        salah.Success.Should().BeFalse("nilai retur mengikuti jumlah unit, bukan isian bebas");
        salah.ErrorMessage.Should().Contain("harus mengikuti jumlah yang diretur");

        var etalaseSebelum = await _stock.GetFinishedGoodsTotalAsync(produk);
        var kasSebelum = Saldo("1-001");
        var ppnSebelum = Saldo("2-003");
        var pendapatanSebelum = Saldo("4-001");
        var hppSebelum = Saldo("5-005");
        var persediaanSebelum = Saldo("1-006");

        var retur = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 1_998_000, "satu roti basi", "Tunai", Pengguna));
        retur.Success.Should().BeTrue(retur.ErrorMessage);

        _db.Count("SalesReturns", $"SalesOrderId = {orderId}").Should().Be(1);
        Convert.ToInt64(_db.Scalar(
            $"SELECT DiscountAmount FROM SalesReturns WHERE Id = {retur.Data};")).Should().Be(200_000);
        Convert.ToInt64(_db.Scalar(
            $"SELECT TaxAmount FROM SalesReturns WHERE Id = {retur.Data};")).Should().Be(198_000);

        Baris("4-001", "SalesReturn", retur.Data).Debit.Should().Be(2_000_000,
            "pendapatan yang dibalik = harga katalog 1 roti: refund + diskon - PPN");
        Baris("2-003", "SalesReturn", retur.Data).Debit.Should().Be(198_000,
            "PPN yang sudah dipungut ikut ditarik, bukan dibiarkan jadi kewajiban tanpa pesanan");
        Baris("4-002", "SalesReturn", retur.Data).Credit.Should().Be(200_000);
        Baris("1-001", "SalesReturn", retur.Data).Credit.Should().Be(1_998_000);

        (Saldo("1-001") - kasSebelum).Should().Be(-1_998_000);
        // 2-003 dan 4-001 ditarik lewat pendebitan, jadi dalam arah baca debit-kredit angkanya
        // NAIK: kewajiban PPN turun Rp 1.980 dan pendapatan turun Rp 20.000 — persis yang diukur
        // probe-b.mjs lewat /api/journal/balances, yang membaca kedua akun itu arah normal (K-D).
        (Saldo("2-003") - ppnSebelum).Should().Be(198_000);
        (Saldo("4-001") - pendapatanSebelum).Should().Be(2_000_000);
        (await _stock.GetFinishedGoodsTotalAsync(produk)).Should().Be(etalaseSebelum + 1m);

        // Nilai yang kembali ke rak adalah HPP yang benar-benar dibebankan saat menjual
        // (200.000 bahan untuk 5 unit = 40.000), bukan harga jual dan bukan HPP katalog.
        Baris("1-006", "SalesReturn", retur.Data).Debit.Should().Be(40_000);
        Baris("5-005", "SalesReturn", retur.Data).Credit.Should().Be(40_000);
        (Saldo("5-005") - hppSebelum).Should().Be(-40_000);
        (Saldo("1-006") - persediaanSebelum).Should().Be(40_000);
    }

    [Fact]
    public async Task ReturBertahap_ReturTerakhirMenutupSisaNilaiTanpaSisaPembulatan()
    {
        await SetPajakAsync(false, 11, false);
        var produk = ProdukBaru("Roti Ganjil", 100_000);
        await IsiEtalaseAsync(produk, 3);

        // Rp 3.000,10 untuk 3 unit: nilainya tidak habis dibagi tiga. Retur ke-1 dan ke-2 boleh
        // Rp 1.000,03; retur terakhir wajib mengambil sisa Rp 1.000,04 supaya total retur menutup
        // pesanan persis — kalau tidak, ada Rp 0,01 hilang yang tidak punya pesanan lagi.
        const long tagihan = 300_010;
        var orderId = await PesanAsync(produk, 3m, tagihan, uangMuka: tagihan);
        (await MuatAsync(orderId)).TotalAmount.Should().Be(tagihan);

        var kurang = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 100_001, "timbangan kurang", "Tunai", Pengguna));
        kurang.Success.Should().BeFalse("nilai retur mengikuti jumlah unit, bukan isian bebas");
        kurang.ErrorMessage.Should().Contain("harus mengikuti jumlah yang diretur");

        var pertama = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 100_003, "timbangan kurang", "Tunai", Pengguna));
        pertama.Success.Should().BeTrue(pertama.ErrorMessage);
        var kedua = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 100_003, "timbangan kurang", "Tunai", Pengguna));
        kedua.Success.Should().BeTrue(kedua.ErrorMessage);

        var perkalianUlang = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 100_003, "timbangan kurang", "Tunai", Pengguna));
        perkalianUlang.Success.Should().BeFalse(
            "sisa nilai pesanan sudah Rp 1.000,04; menghitung ulang proporsinya menyisakan 1 sen");

        var ketiga = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, tagihan - 2 * 100_003, "timbangan kurang", "Tunai", Pengguna));
        ketiga.Success.Should().BeTrue(ketiga.ErrorMessage);

        Convert.ToInt64(_db.Scalar(
            $"SELECT SUM(Amount) FROM SalesReturns WHERE SalesOrderId = {orderId};")).Should().Be(tagihan);
        Convert.ToInt64(_db.Scalar(
            $"SELECT COUNT(*) FROM SalesReturns WHERE SalesOrderId = {orderId};")).Should().Be(3);

        var lewat = await _returnService.CreateAsync(new CreateSalesReturnRequest(
            orderId, 1m, 100_003, "sudah habis", "Tunai", Pengguna));
        lewat.Success.Should().BeFalse();
        lewat.ErrorMessage.Should().Contain("melebihi sisa");
    }
}
