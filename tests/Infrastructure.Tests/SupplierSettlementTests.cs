using FluentAssertions;
using Microsoft.Data.Sqlite;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Kasus uji angka D5 (baris 45 workbook): "retur sebagian, lalu bayar hutang" menuntut saldo
/// hutang turun tepat. Angka di bawah dipilih supaya setiap kaki bisa diperiksa terpisah:
/// struk Rp 25.000/kg × 10 kg = Rp 250.000 hutang, retur 3 kg melepas Rp 75.000, pembayaran
/// Rp 100.000 menyisakan Rp 75.000 — dan tidak ada jalur lain yang bisa menurunkan 2-002.
/// </summary>
public class SupplierSettlementTests
{
    private const string KindBahan = "Ingredient";
    private const int Gudang = 1;
    private const int Dapur = 2;
    private const int Pengguna = 1;
    private const long HargaStruk = 2_500_000;   // Rp 25.000/kg dalam sen

    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;
    private readonly LocationStockRepository _stock;
    private readonly PurchaseOrderRepository _purchases;
    private readonly SupplierSettlementRepository _settlement;
    private readonly PurchaseService _purchaseService;
    private readonly SupplierSettlementService _service;
    private readonly int _pemasok;

    public SupplierSettlementTests()
    {
        _factory = new DbConnectionFactory(_db.ConnectionString);
        _stock = new LocationStockRepository(_factory);
        _purchases = new PurchaseOrderRepository(_factory);
        _settlement = new SupplierSettlementRepository(_factory);
        var accounting = new AccountingService(new JournalRepository(_factory), new AccountRepository(_factory));
        var sequences = new DocumentSequenceRepository(_factory);
        _purchaseService = new PurchaseService(_purchases, _stock, accounting);
        _service = new SupplierSettlementService(_settlement, _stock, sequences, accounting);

        _db.Exec("INSERT INTO Suppliers (Name, IsActive) VALUES ('Pemasok Hutang', 1);");
        _pemasok = Convert.ToInt32(_db.Scalar("SELECT Id FROM Suppliers WHERE Name = 'Pemasok Hutang';"));
    }

    private int BahanBaru(string nama, long hargaMaster)
    {
        _db.Exec($"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 $"VALUES ('{nama}', 'kg', {hargaMaster}, 0, 0, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM Ingredients WHERE Name = '{nama}';"));
    }

    /// <summary>Penerimaan sungguhan lewat layanan, supaya struk + jurnal hutangnya terbentuk sah.</summary>
    private async Task<int> TerimaAsync(int bahan, decimal jumlah, long hargaSatuan, int lokasi = Gudang)
    {
        var po = new PurchaseOrder
        {
            PoNumber = $"PO-HUT-{Guid.NewGuid():N}"[..20],
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

        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM GoodsReceipts WHERE PurchaseOrderId = {poId};"));
    }

    private async Task<int> ReturAsync(int strukId, decimal jumlah, string alasan = "Kemasan rusak") =>
        await ReturAsync(strukId, jumlah, BarisStruk(strukId), alasan);

    private async Task<int> ReturAsync(int strukId, decimal jumlah, int barisId, string alasan = "Kemasan rusak")
    {
        var hasil = await _service.ReturnAsync(strukId, new ReturnSupplierRequest(
            Pengguna, alasan, "Uji angka D5",
            new List<ReturnSupplierLineRequest> { new(barisId, jumlah, null) }));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        return hasil.Data;
    }

    private async Task<int> BayarAsync(int strukId, long jumlah)
    {
        var hasil = await _service.PayAsync(strukId, new PaySupplierRequest(Pengguna, jumlah, "Tunai", null));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        return hasil.Data;
    }

    private int BarisStruk(int strukId) =>
        Convert.ToInt32(_db.Scalar($"SELECT Id FROM GoodsReceiptLines WHERE GoodsReceiptId = {strukId};"));

    /// <summary>
    /// Penerimaan dua baris sekaligus, untuk menguji pagar per baris dan pagar per struk secara
    /// terpisah. Id baris dipetakan per bahan supaya tidak bergantung pada urutan penyimpanan.
    /// </summary>
    private async Task<(int Struk, Dictionary<int, int> Baris)> TerimaDuaBarisAsync(
        (int Bahan, decimal Jumlah) pertama, (int Bahan, decimal Jumlah) kedua)
    {
        var po = new PurchaseOrder
        {
            PoNumber = $"PO-DUA-{Guid.NewGuid():N}"[..20],
            SupplierId = _pemasok,
            Status = PurchaseOrderStatus.Sent,
            OrderDate = DateTime.UtcNow,
            CreatedByUserId = Pengguna,
        };
        foreach (var (bahan, jumlah) in new[] { pertama, kedua })
            po.Lines.Add(new PurchaseOrderLine
            {
                IngredientId = bahan, OrderedQty = jumlah, UnitCostMinorUnits = HargaStruk, Unit = "kg"
            });

        var poId = await _purchases.CreateAsync(po);
        var detail = await _purchases.GetByIdAsync(poId);
        var terima = detail!.Lines.Select(l =>
            new ReceiveGoodsLineDto(l.Id, l.IngredientId, l.OrderedQty, HargaStruk, Gudang)).ToList();
        var hasil = await _purchaseService.ReceiveGoodsAsync(poId, new ReceiveGoodsDto(null, Pengguna, terima));
        hasil.Success.Should().BeTrue(hasil.ErrorMessage);

        var struk = Convert.ToInt32(_db.Scalar($"SELECT Id FROM GoodsReceipts WHERE PurchaseOrderId = {poId};"));
        var baris = new Dictionary<int, int>();
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IngredientId, Id FROM GoodsReceiptLines WHERE GoodsReceiptId = @s;";
        cmd.Parameters.AddWithValue("@s", struk);
        using var pembaca = cmd.ExecuteReader();
        while (pembaca.Read()) baris[pembaca.GetInt32(0)] = pembaca.GetInt32(1);
        return (struk, baris);
    }

    /// <summary>Sisipan mentah melewati layanan dan repository — hanya pemicu basis data yang tersisa.</summary>
    private void SisipkanBarisRetur(int returId, int barisId, int bahan, decimal jumlah)
    {
        var nilai = Convert.ToInt64(Math.Round(jumlah * HargaStruk, 0, MidpointRounding.AwayFromZero));
        _db.Exec("INSERT INTO SupplierReturnLines (SupplierReturnId, GoodsReceiptLineId, IngredientId, LocationId, " +
                 $"Quantity, UnitCostMinorUnits, ValueMinorUnits) VALUES ({returId}, {barisId}, {bahan}, {Gudang}, " +
                 $"{jumlah}, {HargaStruk}, {nilai});");
    }

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

    /// <summary>
    /// Saldo 2-002 dilihat dari sisi pemilik utang (kredit dikurangi debit), karena `Saldo`
    /// memakai konvensi buku besar debit-kurang-kredit dan akun kewajiban bersaldo normal kredit.
    /// </summary>
    private long HutangUsaha => -Saldo("2-002");

    private long NilaiLapisan(int bahan, int lokasi) =>
        Convert.ToInt64(_db.Scalar(
            "SELECT COALESCE(SUM(CAST(ROUND(Quantity * UnitCostMinorUnits) AS INTEGER)), 0) " +
            $"FROM StockCostLayers WHERE ItemKind = '{KindBahan}' AND ItemId = {bahan} AND LocationId = {lokasi};"));

    private IntegrityCheck AmbilCek(string nama) =>
        new DatabaseIntegrity(_factory).Check().Single(c => c.Name == nama);

    private decimal Stok(int bahan, int lokasi) => Convert.ToDecimal(_db.Scalar(
        $"SELECT COALESCE(SUM(Quantity), 0) FROM LocationStock WHERE IngredientId = {bahan} AND LocationId = {lokasi};"));

    [Fact]
    public async Task ReturSebagianLaluBayarHutang_SisaStrukTurunTepat()
    {
        var bahan = BahanBaru("Terigu Retur", HargaStruk);
        var struk = await TerimaAsync(bahan, 10m, HargaStruk);
        var hutangAwal = HutangUsaha;
        hutangAwal.Should().Be(25_000_000, "sepuluh kilogram Rp 25.000/kg = Rp 250.000 hutang usaha");

        var retur = await ReturAsync(struk, 3m);
        var bayar = await BayarAsync(struk, 10_000_000);

        var view = await _settlement.GetPayableAsync(struk);
        view!.ReceivedValueMinorUnits.Should().Be(25_000_000);
        view.ReturnedValueMinorUnits.Should().Be(7_500_000);
        view.PaidValueMinorUnits.Should().Be(10_000_000);
        view.BalanceMinorUnits.Should().Be(7_500_000, "sisa = terima - retur - bayar, bukan - retur saja");

        HutangUsaha.Should().Be(hutangAwal - 17_500_000,
            "retur Rp 75.000 plus pembayaran Rp 100.000 menurunkan hutang Rp 175.000");
        Baris("2-002", "SupplierReturn", retur).Debit.Should().Be(7_500_000);
        Baris("1-003", "SupplierReturn", retur).Credit.Should().Be(7_500_000);
        Baris("2-002", "SupplierPayment", bayar).Debit.Should().Be(10_000_000);
        Baris("1-001", "SupplierPayment", bayar).Credit.Should().Be(10_000_000);

        Stok(bahan, Gudang).Should().Be(7m, "tiga kilogram benar-benar keluar dari gudang");
        Saldo("1-003").Should().Be(NilaiLapisan(bahan, Gudang),
            "persediaan di buku tetap sama dengan lapisan FIFO yang tersisa");

        var nomor = (string)_db.Scalar($"SELECT ReturnNumber FROM SupplierReturns WHERE Id = {retur}")!;
        nomor.Should().StartWith("RTB-");
        var nomorBayar = (string)_db.Scalar($"SELECT PaymentNumber FROM SupplierPayments WHERE Id = {bayar}")!;
        nomorBayar.Should().StartWith("PPB-");

        var mutasi = await _stock.GetMovementsAsync(Gudang, 50);
        mutasi.Single(m => m.SourceType == "SupplierReturn").MovementType
            .Should().Be("Retur Pemasok", "buku mutasi tidak menyebut retur pemasok sebagai koreksi manual");

        AmbilCek("hutang pemasok per struk tidak pernah negatif").Passed.Should().BeTrue();
        AmbilCek("retur dan pembayaran pemasok punya jurnal").Passed.Should().BeTrue();
        AmbilCek("nilai lapisan bahan sama dengan saldo akun persediaan").Passed.Should().BeTrue();
        AmbilCek("nomor dokumen tidak ada yang kembar").Passed.Should().BeTrue();
    }

    [Fact]
    public async Task ReturSetelahStrukLunas_DitolakDenganPesanLunas()
    {
        // Keputusan desain: struk yang sudah dibayar penuh tidak bisa diretur lagi, karena
        // mengurangi hutang di bawah nol akan menampilkan piutang yang dilabeli utang.
        var bahan = BahanBaru("Gula Lunas", HargaStruk);
        var struk = await TerimaAsync(bahan, 4m, HargaStruk);
        await BayarAsync(struk, 10_000_000);

        var hasil = await _service.ReturnAsync(struk, new ReturnSupplierRequest(
            Pengguna, "Salah kirim", null,
            new List<ReturnSupplierLineRequest> { new(BarisStruk(struk), 1m, null) }));

        hasil.Success.Should().BeFalse("hutang nol tidak boleh dikecilkan lagi");
        hasil.ErrorMessage.Should().Contain("sudah lunas");
        _db.Count("SupplierReturns").Should().Be(0, "penolakan tidak boleh meninggalkan dokumen setengah jadi");
        Stok(bahan, Gudang).Should().Be(4m);
        Saldo("2-002").Should().Be(0, "satu-satunya gerak 2-002 adalah pembayaran tadi");
    }

    [Fact]
    public async Task PembayaranMelebihiSisaHutangStruk_DitolakDenganSisaYangTepat()
    {
        var bahan = BahanBaru("Mentega Lunas", HargaStruk);
        var struk = await TerimaAsync(bahan, 2m, HargaStruk);
        await ReturAsync(struk, 1m);

        var hasil = await _service.PayAsync(struk, new PaySupplierRequest(Pengguna, 5_000_000, "Tunai", null));

        hasil.Success.Should().BeFalse();
        hasil.ErrorMessage.Should().Contain("melebihi sisa hutang struk ini").And.Contain("Rp 25.000");
        _db.Count("SupplierPayments").Should().Be(0);
        Saldo("1-001").Should().Be(0, "kas tidak tersentuh oleh pembayaran yang ditolak");
    }

    [Fact]
    public async Task ReturMelebihiJumlahDiterimaPadaBaris_DitolakSebelumMenulisApaPun()
    {
        var bahan = BahanBaru("Susu Retur", HargaStruk);
        var struk = await TerimaAsync(bahan, 6m, HargaStruk);

        var hasil = await _service.ReturnAsync(struk, new ReturnSupplierRequest(
            Pengguna, "Semua saja", null,
            new List<ReturnSupplierLineRequest> { new(BarisStruk(struk), 7m, null) }));

        hasil.Success.Should().BeFalse();
        hasil.ErrorMessage.Should().Contain("melebihi sisa yang bisa diretur");
        _db.Count("SupplierReturns").Should().Be(0);
        Stok(bahan, Gudang).Should().Be(6m);
    }

    [Fact]
    public async Task ReturKeduaHanyaBolehSisaJumlahPertama()
    {
        var bahan = BahanBaru("Telur Retur", HargaStruk);
        var struk = await TerimaAsync(bahan, 10m, HargaStruk);
        await ReturAsync(struk, 6m);

        var hasil = await _service.ReturnAsync(struk, new ReturnSupplierRequest(
            Pengguna, "Retur kedua", null,
            new List<ReturnSupplierLineRequest> { new(BarisStruk(struk), 5m, null) }));

        hasil.Success.Should().BeFalse("enam kilogram pertama sudah memakai sebagian baris yang sama");
        hasil.ErrorMessage.Should().Contain("4");
        (await _settlement.GetPayableAsync(struk))!.ReturnedValueMinorUnits.Should().Be(15_000_000,
            "retur yang ditolak tidak menambah nilai retur struk");
    }

    [Fact]
    public async Task BarisReturLanggarPemicu_InsertMentahDitolakBasisData()
    {
        // Kontrol untuk lapis ketiga: kalau layanan dan repository dilewati sekali pun, pemicu
        // tetap menjaga barang yang keluar tidak melebihi barang yang diterima.
        var murah = BahanBaru("Kemasan Pemicu Murah", HargaStruk);
        var banyak = BahanBaru("Kemasan Pemicu Banyak", HargaStruk);
        var (struk, baris) = await TerimaDuaBarisAsync((murah, 1m), (banyak, 10m));

        // Sisa hutang struk masih besar (11 kg), jadi hanya pagar per baris yang bisa menahan
        // retur 2 kg atas baris yang hanya menerima 1 kg.
        var returBaris = await ReturAsync(struk, 1m, baris[banyak]);
        var actBaris = () => SisipkanBarisRetur(returBaris, baris[murah], murah, 2m);
        actBaris.Should().Throw<SqliteException>()
            .WithMessage("*Jumlah retur melebihi barang yang diterima pada baris ini*");

        // Struk dengan satu baris yang belum pernah diretur tapi hutangnya sudah dilunasi
        // pembayaran: pagar per baris pasti lolos (5 kg diterima, baru 1 kg keluar), jadi pesan
        // yang muncul hanya bisa datang dari pagar beban struk.
        var (strukKecil, barisKecil) = await TerimaDuaBarisAsync((murah, 1m), (banyak, 5m));
        await ReturAsync(strukKecil, 1m, barisKecil[murah]);
        var lunas = await BayarAsync(strukKecil, 12_500_000);
        var actStruk = () => SisipkanBarisRetur(lunas, barisKecil[banyak], banyak, 1m);
        actStruk.Should().Throw<SqliteException>()
            .WithMessage("*Retur melebihi sisa hutang struk penerimaan ini*");

        _db.Count("SupplierReturnLines",
            $"GoodsReceiptLineId IN ({baris[murah]}, {barisKecil[banyak]})").Should().Be(0,
            "dua sisipan ilegal tidak ada yang tersimpan");
    }

    [Fact]
    public async Task HargaLapisanLebihMahalDariHargaStruk_JurnalSelisihMenjagaNilaiBuku()
    {
        // Dua struk harga berbeda untuk bahan yang sama, retur di struk murah: FIFO menghabiskan
        // lapisan tertua yang lebih mahal, jadi pelepasan hutang (harga struk) dan pengurangan
        // persediaan (nilai lapisan) berbeda. Tanpa kaki selisih, periksa-diri "nilai lapisan bahan
        // sama dengan saldo akun persediaan" menyala merah.
        var bahan = BahanBaru("Cokelat Dua Harga", 200_000);
        await TerimaAsync(bahan, 5m, 200_000);
        var strukMurah = await TerimaAsync(bahan, 5m, 100_000);

        var retur = await ReturAsync(strukMurah, 2m);

        Baris("2-002", "SupplierReturn", retur).Debit.Should().Be(200_000,
            "hutang dilepas sebesar harga struk yang diretur: 2 kg × Rp 1.000");
        Baris("1-003", "SupplierReturn", retur).Credit.Should().Be(400_000,
            "persediaan berkurang sebesar lapisan FIFO yang habis: 2 kg × Rp 2.000");
        Baris("5-006", "SupplierReturn", retur).Debit.Should().Be(200_000,
            "selisihnya menjadi beban, bukan hilang dari neraca");
        Saldo("1-003").Should().Be(NilaiLapisan(bahan, Gudang));
        Stok(bahan, Gudang).Should().Be(8m);
        AmbilCek("nilai lapisan bahan sama dengan saldo akun persediaan").Passed.Should().BeTrue();
        AmbilCek("hutang pemasok per struk tidak pernah negatif").Passed.Should().BeTrue();
    }

    [Fact]
    public async Task ReturDariDapur_MengkreditAkunPersediaanDapur()
    {
        var bahan = BahanBaru("Bahan Pindah Retur", HargaStruk);
        var struk = await TerimaAsync(bahan, 6m, HargaStruk, Gudang);
        await _stock.TransferStockAsync(Gudang, Dapur, bahan, 4m, Pengguna, "pindah dapur");

        var baris = BarisStruk(struk);
        var hasil = await _service.ReturnAsync(struk, new ReturnSupplierRequest(
            Pengguna, "Tidak jadi dipakai dapur", null,
            new List<ReturnSupplierLineRequest> { new(baris, 2m, Dapur) }));

        hasil.Success.Should().BeTrue(hasil.ErrorMessage);
        Baris("1-004", "SupplierReturn", hasil.Data).Credit.Should().Be(5_000_000,
            "barang yang diambil dari dapur mengurangi akun persediaan dapur");
        Stok(bahan, Dapur).Should().Be(2m);
        Stok(bahan, Gudang).Should().Be(2m);
    }

    [Fact]
    public async Task DokumenPemasokTanpaJurnal_PeriksaBasisDataMenyalaMerah()
    {
        var bahan = BahanBaru("Terigu Yatim", HargaStruk);
        var struk = await TerimaAsync(bahan, 3m, HargaStruk);
        var retur = await ReturAsync(struk, 1m);

        AmbilCek("retur dan pembayaran pemasok punya jurnal").Passed.Should().BeTrue();

        // Suntingan langsung yang memutus tautan jurnal harus ketahuan: dokumen uang tanpa jurnal
        // berarti 2-002 turun tanpa jejak di buku besar.
        _db.Exec($"UPDATE SupplierReturns SET JournalEntryId = NULL WHERE Id = {retur};");

        var sesudah = AmbilCek("retur dan pembayaran pemasok punya jurnal");
        sesudah.Passed.Should().BeFalse();
        sesudah.Detail.Should().Contain("RTB");
    }

    [Fact]
    public async Task StrukLunasTerakhir_HilangDariDaftarUtangDanMunculSaatLunasDimuat()
    {
        var bahan = BahanBaru("Tepung Daftar", HargaStruk);
        var struk = await TerimaAsync(bahan, 2m, HargaStruk);

        (await _settlement.GetPayablesAsync()).Select(p => p.GoodsReceiptId).Should().Contain(struk);

        await BayarAsync(struk, 5_000_000);

        (await _settlement.GetPayablesAsync()).Select(p => p.GoodsReceiptId).Should().NotContain(struk,
            " layar Utang Pemasok hanya menawarkan struk yang masih punya sisa");
        (await _settlement.GetPayablesAsync(includeLunas: true)).Select(p => p.GoodsReceiptId)
            .Should().Contain(struk);
        (await _settlement.GetPayablesAsync()).Count().Should().Be(0,
            "setelah satu-satunya struk lunas, daftar utang benar-benar kosong");
    }
}
