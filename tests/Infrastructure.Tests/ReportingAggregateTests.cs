using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Angka dashboard, kotak tugas, dan analitik kini dihitung di SQL. Pengujian ini memakai
/// basis data hasil migrasi sungguhan dan membandingkan hasilnya dengan hitungan yang
/// ditulis tangan, supaya "lebih cepat" tidak pernah berarti "angkanya bergeser".
/// </summary>
public class ReportingAggregateTests
{
    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;

    public ReportingAggregateTests() => _factory = new DbConnectionFactory(_db.ConnectionString);

    private static string Hari(DateTime tanggal) => tanggal.ToString("yyyy-MM-dd");

    private int Pesanan(string pickup, long total, long dp, string status = "Pending",
        DateTime? createdAt = null, int? produk = null, decimal qty = 1m, DateTime? createdAtUtc = null)
    {
        // CreatedAt disimpan UTC seperti di produksi. Nilai dari tes adalah waktu toko, jadi
        // diterjemahkan dulu; createdAtUtc dipakai bila tes memang ingin menulis cap waktu UTC.
        var dibuat = createdAtUtc?.ToString("o")
            ?? TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(createdAt ?? BusinessTime.Now(), DateTimeKind.Unspecified),
                BusinessTime.Zone).ToString("o");
        _db.Exec($@"
            INSERT INTO SalesOrders (CustomerNameSnapshot, Description, PickupDate, TotalAmount,
                                     DownPayment, Quantity, Status, CreatedAt, ProductTemplateId)
            VALUES ('Pelanggan Uji', 'Kue', '{pickup}', {total}, {dp}, {qty}, '{status}', '{dibuat}', "
                   + (produk is null ? "NULL" : produk.ToString()) + ");");
        return Convert.ToInt32(_db.Scalar("SELECT last_insert_rowid();"));
    }

    private void Bayar(int pesananId, long nominal, string tanggal) =>
        _db.Exec($@"INSERT INTO SalesPayments (SalesOrderId, Amount, PaymentDate, Method)
                     VALUES ({pesananId}, {nominal}, '{tanggal}', 'Cash');");

    private void Jurnal(string tanggal, string status, params (int Akun, long Debit, long Kredit)[] baris)
    {
        _db.Exec($@"INSERT INTO JournalEntries (Description, TransactionDate, Status)
                     VALUES ('Uji', '{tanggal}', '{status}');");
        var id = Convert.ToInt32(_db.Scalar("SELECT last_insert_rowid();"));
        foreach (var (akun, debit, kredit) in baris)
            _db.Exec($@"INSERT INTO JournalLines (JournalEntryId, AccountId, DebitAmount, CreditAmount)
                         VALUES ({id}, {akun}, {debit}, {kredit});");
    }

    [Fact]
    public async Task Dashboard_KasPiutangDanArusHariIni_SamaDenganHitunganTangan()
    {
        var hariIni = BusinessTime.Today();
        var kemarin = hariIni.AddDays(-1);

        var lewat = Pesanan(Hari(kemarin.AddDays(-1)), 100_000, 20_000);
        var lunas = Pesanan(Hari(hariIni.AddDays(1)), 50_000, 0);
        Bayar(lunas, 50_000, Hari(hariIni));
        var sebagian = Pesanan(Hari(hariIni), 30_000, 0);
        Bayar(sebagian, 10_000, Hari(hariIni));
        Pesanan(Hari(hariIni), 90_000, 0, status: "Cancelled");

        Jurnal(Hari(hariIni), "Posted", (1, 150_000, 0), (5, 0, 150_000));
        Jurnal(Hari(hariIni), "Posted", (6, 40_000, 0), (1, 0, 40_000));
        Jurnal(Hari(kemarin), "Posted", (1, 10_000, 0), (5, 0, 10_000));

        var repo = new SalesOrderRepository(_factory);
        var jurnal = new JournalRepository(_factory);

        var ringkas = await repo.GetOutstandingSummaryAsync();
        ringkas.Jumlah.Should().Be(2, "pesanan lunas dan batal bukan piutang");
        ringkas.Sisa.Should().Be(100_000);

        var tenggat = await repo.GetOutstandingBreakdownAsync(hariIni);
        tenggat.Overdue.Should().Be(1);
        tenggat.Upcoming.Should().Be(1);

        var lewatList = await repo.GetOutstandingOverdueAsync(hariIni, 10);
        lewatList.Should().ContainSingle().Which.Id.Should().Be(lewat);
        var belumList = await repo.GetOutstandingUpcomingAsync(hariIni, 10);
        belumList.Should().ContainSingle().Which.Id.Should().Be(sebagian);

        var arus = await jurnal.GetCashFlowAsync(hariIni, "1-001");
        arus.Masuk.Should().Be(150_000, "arus kas hari ini saja");
        arus.Keluar.Should().Be(40_000);

        (await jurnal.GetAccountBalanceAsync("1-001")).Should().Be(120_000,
            "saldo kas menjumlah seluruh riwayat posted");
    }

    [Fact]
    public async Task KotakTugas_JumlahPerJenisTetapTepat_SaatDaftarDibatasi()
    {
        var hariIni = BusinessTime.Today();
        for (var i = 0; i < 75; i++) Pesanan(Hari(hariIni.AddDays(-1)), 10_000, 0);
        for (var i = 0; i < 70; i++) Pesanan(Hari(hariIni.AddDays(1)), 10_000, 0);

        var inbox = new TaskInboxService(
            new SalesOrderRepository(_factory), new ProductionRepository(_factory),
            new PurchaseOrderRepository(_factory), new IngredientRepository(_factory),
            new BatchRepository(_factory));

        var kotak = await inbox.GetAsync(UserRole.Owner);

        kotak.Jumlah.Should().Be(145, "angka yang dilaporkan adalah keadaan basis data, bukan panjang daftar layar");
        kotak.PerJenis["piutang"].Should().Be(75);
        kotak.PerJenis["pengambilan"].Should().Be(70);
        kotak.Item.Should().HaveCount(60, "baris yang dikirim ke layar tetap dibatasi");
    }

    [Fact]
    public async Task Analitik_MemakaiJurnalDanPesananPadaRentangYangSama()
    {
        var hariIni = BusinessTime.Today();
        var kemarin = hariIni.AddDays(-1);

        _db.Exec("INSERT INTO ProductTemplates (Name, SellingPrice, IsActive) VALUES ('Roti A', 10000, 1);");
        _db.Exec("INSERT INTO ProductTemplates (Name, SellingPrice, IsActive) VALUES ('Roti Nonaktif', 10000, 0);");

        Pesanan(Hari(hariIni), 100_000, 0, createdAt: hariIni, produk: 1, qty: 2m);
        Pesanan(Hari(hariIni), 50_000, 0, status: "Cancelled", createdAt: hariIni, produk: 1);
        Pesanan(Hari(kemarin), 30_000, 0, createdAt: kemarin, produk: 2, qty: 3m);
        Pesanan(Hari(hariIni), 99_000, 0, createdAt: hariIni.AddDays(1), produk: 1, qty: 9m);
        Pesanan(Hari(hariIni), 77_000, 0, createdAt: hariIni.AddDays(-20), produk: 1);

        Jurnal(Hari(hariIni), "Posted", (1, 100_000, 0), (5, 0, 100_000));
        Jurnal(Hari(hariIni), "Posted", (6, 25_000, 0), (1, 0, 25_000));
        Jurnal(Hari(kemarin), "Posted", (1, 30_000, 0), (5, 0, 30_000));
        Jurnal(Hari(hariIni.AddDays(1)), "Posted", (1, 777, 0), (5, 0, 777));

        var analitik = new AnalyticsService(
            new JournalRepository(_factory), new SalesOrderRepository(_factory));
        var hasil = await analitik.GetSalesTrendAsync(7);

        hasil.TotalPendapatan.Should().Be(130_000, "jurnal tanggal besok tidak boleh ikut");
        hasil.TotalBeban.Should().Be(25_000);
        hasil.LabaKotor.Should().Be(105_000);
        hasil.TotalNilaiPesanan.Should().Be(130_000);
        hasil.JumlahPesanan.Should().Be(2);
        hasil.JumlahPesananBatal.Should().Be(1);
        hasil.UnitTerjual.Should().Be(5m);
        hasil.RataRataNilaiPesanan.Should().Be(65_000);

        hasil.Harian.Should().HaveCount(2);
        hasil.Harian[0].Tanggal.Should().Be(kemarin);
        hasil.Harian[1].Tanggal.Should().Be(hariIni);
        hasil.Harian[1].Pendapatan.Should().Be(100_000);
        hasil.Harian[1].JumlahPesanan.Should().Be(1);

        hasil.Produk.Should().HaveCount(2);
        hasil.Produk[0].Nama.Should().Be("Roti A");
        hasil.Produk[0].Nilai.Should().Be(100_000);
        hasil.Produk[1].Nama.Should().Be("Pesanan tanpa produk terdaftar",
            "produk nonaktif tidak lagi punya nama yang boleh dipakai");
        hasil.Produk[1].Nilai.Should().Be(30_000);

        hasil.Beban.Should().ContainSingle();
        hasil.Beban[0].Kode.Should().Be("5-001");
        hasil.Beban[0].Nominal.Should().Be(25_000);
    }

    [Fact]
    public async Task PenjualanDiniHari_DihitungPadaTanggalToko_BukanTanggalUtc()
    {
        // Regresi: rekap harian dulu memakai potongan tanggal UTC dari CreatedAt, sehingga
        // penjualan pukul 01:30 WIB (masih tanggal UTC kemarin) hilang dari laporan hari ini
        // dan muncul pada laporan kemarin.
        var hariIni = BusinessTime.Today();
        var awalHari = BusinessTime.AwalHariUtc(hariIni);
        var diniHari = awalHari.AddMinutes(90);        // 01:30 waktu toko
        var akhirKemarin = awalHari.AddMinutes(-30);   // 23:30 waktu toko kemarin

        Pesanan(Hari(hariIni), 50_000, 0, createdAtUtc: diniHari);
        Pesanan(Hari(hariIni.AddDays(-1)), 70_000, 0, createdAtUtc: akhirKemarin);

        var repo = new SalesOrderRepository(_factory);
        var harian = await repo.GetDailySalesAsync(hariIni, hariIni.AddDays(1));

        harian.Should().ContainSingle("hanya transaksi 01:30 yang jatuh pada tanggal toko hari ini");
        harian[0].Tanggal.Should().Be(hariIni);
        harian[0].JumlahPesanan.Should().Be(1);
        harian[0].Nilai.Should().Be(50_000);

        var analitik = new AnalyticsService(new JournalRepository(_factory), repo);
        var laporan = await analitik.GetDailyReportAsync(hariIni);
        laporan.JumlahPesanan.Should().Be(1, "laporan harian MCP memakai jalur yang sama");
        laporan.NilaiPesanan.Should().Be(50_000);

        var kemarin = await repo.GetDailySalesAsync(hariIni.AddDays(-1), hariIni);
        kemarin.Should().ContainSingle();
        kemarin[0].Tanggal.Should().Be(hariIni.AddDays(-1));
        kemarin[0].Nilai.Should().Be(70_000, "transaksi 23:30 tetap milik tanggal kemarin");
    }

    [Fact]
    public async Task PembelianTerbuka_HanyaStatusMenungguDanBerurutTanggalTerima()
    {
        var hariIni = BusinessTime.Today();
        _db.Exec("INSERT INTO Suppliers (Name, IsActive, CreatedAt) VALUES ('Pemasok Uji', 1, datetime('now'));");
        _db.Exec($@"
            INSERT INTO PurchaseOrders (PoNumber, SupplierId, Status, OrderDate, ExpectedDate, CreatedAt) VALUES
            ('PO-1', 1, 'Draft',           '{Hari(hariIni)}', '{Hari(hariIni.AddDays(-3))}', datetime('now')),
            ('PO-2', 1, 'Sent',            '{Hari(hariIni)}', '{Hari(hariIni.AddDays(5))}',  datetime('now')),
            ('PO-3', 1, 'PartialReceived', '{Hari(hariIni)}', NULL,                          datetime('now')),
            ('PO-4', 1, 'Received',        '{Hari(hariIni)}', '{Hari(hariIni.AddDays(1))}',  datetime('now')),
            ('PO-5', 1, 'Cancelled',       '{Hari(hariIni)}', '{Hari(hariIni.AddDays(1))}',  datetime('now'));");

        var repo = new PurchaseOrderRepository(_factory);

        var tenggat = await repo.GetOpenBreakdownAsync(hariIni);
        tenggat.Overdue.Should().Be(1);
        tenggat.Upcoming.Should().Be(2);

        var terbuka = await repo.GetOpenAsync(10);
        terbuka.Select(p => p.PoNumber).Should().Equal("PO-1", "PO-2", "PO-3");
        terbuka[0].SupplierName.Should().Be("Pemasok Uji");
    }

    [Fact]
    public async Task Jurnal_RentangDimuatPerHalaman_TanpaAdaEntriTerlewatAtauGanda()
    {
        var awal = new DateTime(2031, 3, 1);
        // Lima entri posted di rentang, satu tanggal di luar rentang, dan satu dokumen yang
        // dibatalkan utuh: entri aslinya berstatus Reversed dan entri pembaliknya Posted.
        for (var i = 0; i < 5; i++)
            Jurnal(Hari(awal.AddDays(i % 2)), "Posted", (1, 10_000 + i, 0), (5, 0, 10_000 + i));
        Jurnal(Hari(awal.AddDays(10)), "Posted", (1, 99_000, 0), (5, 0, 99_000));
        Jurnal(Hari(awal.AddDays(1)), "Reversed", (1, 88_000, 0), (5, 0, 88_000));
        Jurnal(Hari(awal.AddDays(1)), "Posted", (1, 0, 88_000), (5, 88_000, 0));

        var repo = new JournalRepository(_factory);

        var halaman1 = await repo.GetByDateRangePageAsync(awal, awal.AddDays(2), null, null, 2);
        halaman1.Should().HaveCount(2);
        halaman1[0].Id.Should().BeGreaterThan(halaman1[1].Id, "urun terbaru lebih dulu");

        var terakhir = halaman1[^1];
        var halaman2 = await repo.GetByDateRangePageAsync(
            awal, awal.AddDays(2), terakhir.TransactionDate, terakhir.Id, 2);
        var halaman3 = await repo.GetByDateRangePageAsync(
            awal, awal.AddDays(2), halaman2[^1].TransactionDate, halaman2[^1].Id, 2);

        var semua = halaman1.Concat(halaman2).Concat(halaman3).ToList();
        semua.Should().HaveCount(6, "entri di luar rentang tidak ikut, dan yang dibatalkan tampil sebagai pembaliknya");
        semua.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        semua.Should().OnlyContain(e => e.Status == JournalStatus.Posted);

        var ringkas = await repo.GetRangeSummaryAsync(awal, awal.AddDays(2), null);
        ringkas.Entries.Should().Be(7, "lima entri posted ditambah pasangan asli+pembalik yang sama-sama tercatat");
        ringkas.Legs.Should().Be(14);
        ringkas.Accounts.Should().HaveCount(2);
        var kas = ringkas.Accounts.Single(a => a.Code == "1-001");
        kas.Debit.Should().Be(138_010, "10.000+10.001+10.002+10.003+10.004 ditambah entri asli yang dibatalkan");
        kas.Credit.Should().Be(88_000, "pembalik mengkredit kas sebesar nilai dokumen yang dibatalkan");
        (kas.Debit - kas.Credit).Should().Be(50_010,
            "pasangan asli+pembalik harus saling meniadakan; kalau hanya pembaliknya dihitung, "
            + "rentang ini minus 88.000 dan laporan ikut salah");
        kas.Legs.Should().Be(7);
    }

    [Fact]
    public async Task Jurnal_RingkasanMengikutiFilterSumber_DanSaldoAsOfMemotongTanggal()
    {
        var awal = new DateTime(2031, 4, 1);
        _db.Exec($@"INSERT INTO JournalEntries (Description, TransactionDate, Status, SourceDocumentType)
                    VALUES ('Penjualan', '{Hari(awal)}', 'Posted', 'SalesOrder');");
        var idJual = Convert.ToInt32(_db.Scalar("SELECT last_insert_rowid();"));
        // Angka ditulis tanpa pemisah digit: nilai ini disisipkan ke teks SQL, bukan dikirim
        // sebagai parameter, sehingga "70_000" akan sampai ke SQLite apa adanya.
        _db.Exec($@"INSERT INTO JournalLines (JournalEntryId, AccountId, DebitAmount, CreditAmount)
                    VALUES ({idJual}, 1, 70000, 0), ({idJual}, 5, 0, 70000);");
        Jurnal(Hari(awal), "Posted", (1, 0, 20_000), (6, 20_000, 0));      // tanpa sumber
        Jurnal(Hari(awal.AddDays(3)), "Posted", (1, 5_000, 0), (5, 0, 5_000));

        var repo = new JournalRepository(_factory);

        var semua = await repo.GetRangeSummaryAsync(awal, awal.AddDays(5), null);
        semua.Entries.Should().Be(3);
        semua.Sources.Should().Equal("SalesOrder");

        var penjualan = await repo.GetRangeSummaryAsync(awal, awal.AddDays(5), "SalesOrder");
        penjualan.Entries.Should().Be(1);
        penjualan.Accounts.Single(a => a.Code == "1-001").Debit.Should().Be(70_000);

        var tanpaSumber = await repo.GetRangeSummaryAsync(awal, awal.AddDays(5), "none");
        tanpaSumber.Entries.Should().Be(2, "dua entri dibuat lewat helper tanpa jenis dokumen sumber");
        tanpaSumber.Accounts.Single(a => a.Code == "6" || a.Code == "5-001").Debit.Should().Be(20_000);

        var saldoAwalHariItu = await repo.GetBalancesAsync(awal);
        saldoAwalHariItu.Single(a => a.Code == "1-001").Balance.Should().Be(50_000,
            "70.000 masuk - 20.000 keluar; entri tanggal berikutnya belum dihitung");
        var saldoAkhir = await repo.GetBalancesAsync(awal.AddDays(3));
        saldoAkhir.Single(a => a.Code == "1-001").Balance.Should().Be(55_000);
    }

    [Fact]
    public async Task Pelanggan_HalamanDanPencarianMenjawabJumlahYangSamaDenganIsi()
    {
        // Nomor dirakit di C# lalu disisipkan: format sisipan seperti {i:04} adalah format
        // kustom (bukan padding empat digit) sehingga nilainya tidak seperti yang dibaca.
        for (var i = 1; i <= 7; i++)
        {
            var nama = $"Pelanggan {i:D2}";
            var nomor = $"0812{i:D4}";
            _db.Exec($@"INSERT INTO Customers (Name, Phone, IsActive, CreatedAt)
                        VALUES ('{nama}', '{nomor}', 1, datetime('now'));");
        }
        _db.Exec(@"INSERT INTO Customers (Name, Phone, IsActive, CreatedAt)
                   VALUES ('Pelanggan Nonaktif', '0899', 0, datetime('now'));");

        var repo = new CustomerRepository(_factory);

        (await repo.CountActiveAsync(null)).Should().Be(7);
        var halaman = await repo.GetPageAsync(null, 3, 0);
        halaman.Should().HaveCount(3);
        halaman.Select(c => c.Name).Should().Equal("Pelanggan 01", "Pelanggan 02", "Pelanggan 03");

        var lanjutan = await repo.GetPageAsync(null, 3, 3);
        lanjutan.Select(c => c.Name).Should().Equal("Pelanggan 04", "Pelanggan 05", "Pelanggan 06");

        var cari = await repo.GetPageAsync("03", 10, 0);
        cari.Should().ContainSingle().Which.Name.Should().Be("Pelanggan 03");
        (await repo.CountActiveAsync("03")).Should().Be(1);
        (await repo.CountActiveAsync("08120003")).Should().Be(1, "nomor telepon ikut dicari");
    }

    [Fact]
    public async Task Pembelian_HalamanDenganBaris_DanHitunganTerbuka()
    {
        _db.Exec("INSERT INTO Suppliers (Name, IsActive, CreatedAt) VALUES ('Pemasok Uji', 1, datetime('now'));");
        _db.Exec("INSERT INTO Ingredients (Name, Unit, MinStockAlert, IsActive) VALUES ('Tepung', 'kg', 0, 1);");
        for (var i = 1; i <= 4; i++)
        {
            _db.Exec($@"INSERT INTO PurchaseOrders (PoNumber, SupplierId, Status, OrderDate, CreatedAt)
                        VALUES ('PO-{i}', 1, '{(i <= 2 ? "Sent" : "Received")}', '2031-05-0{i}', datetime('now'));");
            var id = Convert.ToInt32(_db.Scalar("SELECT last_insert_rowid();"));
            _db.Exec($@"INSERT INTO PurchaseOrderLines (PurchaseOrderId, IngredientId, OrderedQty, ReceivedQty,
                                                        UnitCostMinorUnits, Unit)
                        VALUES ({id}, 1, 5, 0, 1000, 'kg');");
        }

        var repo = new PurchaseOrderRepository(_factory);

        (await repo.CountAsync(null)).Should().Be(4);
        (await repo.CountAsync("open")).Should().Be(2, "hanya Draft/Terkirim/Sebagian yang menjadi utang");

        var halaman = await repo.GetPageAsync(null, 2, 0);
        halaman.Should().HaveCount(2);
        halaman.Should().OnlyContain(po => po.Lines.Count == 1, "baris ikut dimuat untuk nilai PO");
        halaman.Should().OnlyContain(po => po.SupplierName == "Pemasok Uji");

        var terbuka = await repo.GetPageAsync("open", 10, 0);
        terbuka.Select(po => po.PoNumber).Should().BeEquivalentTo("PO-1", "PO-2");
    }
}
