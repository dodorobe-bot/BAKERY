using FluentAssertions;
using Microsoft.Data.Sqlite;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>DB sekali-pakai berisi seluruh migrasi yang sudah diterapkan, dibersihkan setelah dipakai.</summary>
internal sealed class TempDatabase : IDisposable
{
    public string FilePath { get; }
    public string ConnectionString => $"Data Source={FilePath};";

    public TempDatabase()
    {
        FilePath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"bakedb-{Guid.NewGuid():N}.db");
        new DatabaseMigrator(ConnectionString).Migrate();
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    public void Exec(string sql)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public object? Scalar(string sql)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public int Count(string table, string where = "1=1") =>
        Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM {table} WHERE {where};"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { FilePath, FilePath + "-wal", FilePath + "-shm" })
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* file sementara */ }
        }
    }
}

public class DatabaseMigratorTests
{
    [Fact]
    public void SplitStatements_SemicolonInsideCommentOrString_KeepsStatementWhole()
    {
        const string sql = "-- komentar berisi; titik-koma tidak boleh memecah\n" +
                           "INSERT INTO T VALUES ('a;b');\nSELECT 1;";

        var statements = DatabaseMigrator.SplitStatements(sql).ToList();

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("'a;b'");
    }

    [Fact]
    public void SplitStatements_MigrationFileFromDisk_HasNoFragmentStartingWithProse()
    {
        // Regresi startup: potongan komentar "kolomnya diisi ..." sempat dijalankan sebagai SQL
        var sql = "-- Ledger dicatat sebelum lokasi mutasi diketahui, kolomnya diisi\n" +
                  "ALTER TABLE StockMovements ADD COLUMN LocationId INTEGER;";

        DatabaseMigrator.SplitStatements(sql).Should().HaveCount(1);
    }

    [Fact]
    public void SplitStatements_TriggerBody_KeepsBeginEndWhole()
    {
        const string sql =
            "CREATE TRIGGER IF NOT EXISTS trg_x BEFORE INSERT ON T\n" +
            "BEGIN\n" +
            "    SELECT RAISE(ABORT, 'nominal harus lebih dari nol') WHERE NEW.Amount <= 0;\n" +
            "END;\n" +
            "SELECT 1;";

        var statements = DatabaseMigrator.SplitStatements(sql).ToList();

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("BEGIN").And.Contain("END")
            .And.NotContain("SELECT 1");
    }

    [Fact]
    public void SplitStatements_TriggerBodyWithCommentWordBegin_StaysWhole()
    {
        // Kata BEGIN/END di dalam komentar atau pesan RAISE tidak boleh dihitung sebagai penutup blok.
        const string sql =
            "-- BEGIN catatan END\n" +
            "CREATE TRIGGER trg_y BEFORE INSERT ON T\n" +
            "BEGIN\n" +
            "    SELECT RAISE(ABORT, 'BEGIN END bukan blok') WHERE NEW.A < 0;\n" +
            "END;\n";

        DatabaseMigrator.SplitStatements(sql).Should().HaveCount(1);
    }

    [Fact]
    public void SplitStatements_TriggerBodyWithCaseEnd_StaysWhole()
    {
        // Regresi migrasi 013: END milik CASE pernah dianggap penutup badan trigger,
        // sehingga CREATE TRIGGER terpotong dan migrasi gagal "incomplete input".
        const string sql =
            "CREATE TRIGGER trg_z BEFORE INSERT ON T\n" +
            "BEGIN\n" +
            "    SELECT RAISE(ABORT, 'keputusan salah')\n" +
            "        WHERE NEW.Decision != (CASE WHEN NEW.Failed = 0 THEN 'Passed' ELSE 'Failed' END);\n" +
            "END;\n" +
            "SELECT 1;";

        var statements = DatabaseMigrator.SplitStatements(sql).ToList();

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("CASE").And.Contain("Passed")
            .And.NotContain("SELECT 1");
    }

    [Fact]
    public void Migrate_EmptyDatabase_ReachesVersionTertinggiDenganStokProdukJadiLotDanSesi()
    {
        using var db = new TempDatabase();

        Convert.ToInt32(db.Scalar("SELECT MAX(Version) FROM SchemaVersion;"))
            .Should().Be(DatabaseMigrator.LatestEmbeddedVersion());

        // Migrasi 016: indeks untuk dashboard, analitik, kotak tugas, dan riwayat stok.
        // Migrasi 017 mengganti dua di antaranya dengan indeks penutup dan menambah indeks
        // urutan daftar pembelian; nama lama tidak boleh tersisa karena sudah tidak dipakai.
        Convert.ToInt32(db.Scalar(@"
            SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN (
                'idx_journallines_account_amounts', 'idx_journalentries_status_date',
                'idx_salesorders_status_pickup',
                'idx_purchaseorders_status_expected', 'idx_locationstock_ingredient',
                'idx_stockmovements_created');")).Should().Be(6);
        Convert.ToInt32(db.Scalar(@"
            SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN (
                'idx_salesorders_created_agg', 'idx_purchaseorders_created',
                'idx_salespayments_order_amount', 'idx_salesorders_open');")).Should().Be(4);
        var indeksTerbuka = (string)db.Scalar(
            "SELECT sql FROM sqlite_master WHERE name='idx_salesorders_open';")!;
        indeksTerbuka.Should().Contain("WHERE Status NOT IN ('Paid','Cancelled')",
            "syarat indeks parsial harus sama persis dengan syarat kueri piutang supaya dipakai");
        Convert.ToInt32(db.Scalar(@"
            SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN (
                'idx_salesorders_created', 'idx_salespayments_salesorder');"))
            .Should().Be(0, "indeks sempit yang diganti migrasi 017 tidak boleh tetap ada");
        var indeksBayar = (string)db.Scalar(
            "SELECT sql FROM sqlite_master WHERE name='idx_salespayments_order_amount';")!;
        indeksBayar.Should().Contain("Amount", "ringkasan piutang menjumlahkan Amount; tanpa kolom itu indeks tidak menutup");
        Convert.ToInt32(db.Scalar(@"
            SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_journallines_entryid';"))
            .Should().Be(1, "indeks baris jurnal per entri dipertahankan: pola baca jurnal bergantung padanya");
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='sqlite_stat1';"))
            .Should().Be(1, "ANALYZE wajib jalan supaya perencana query memakai indeks baru");

        // Migrasi 014: antrean cetak dan barcode produk.
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PrintJobs';"))
            .Should().Be(1);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ProductTemplates') WHERE name='Barcode';"))
            .Should().Be(1);

        // Migrasi 015: kotak notifikasi internal yang dibaca layar Pemberitahuan.
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Notifications';"))
            .Should().Be(1);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ux_notifications_dedupe';"))
            .Should().Be(1);

        // Kunci idempotensi harus benar-benar menolak kunci kembar, bukan hanya punya kolom.
        db.Exec("INSERT INTO IdempotencyKeys (Key, Route) VALUES ('kunci-sama-1', 'POST /api/orders');");
        var dobel = () => db.Exec("INSERT INTO IdempotencyKeys (Key, Route) VALUES ('kunci-sama-1', 'POST /api/orders');");
        dobel.Should().Throw<Microsoft.Data.Sqlite.SqliteException>();
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM IdempotencyKeys WHERE CompletedAt IS NULL;")).Should().Be(1);

        // Stok produk jadi dan jumlah pesanan harus benar-benar ada, bukan hanya tercatat di file migrasi.
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('SalesOrders') WHERE name='Quantity';"))
            .Should().Be(1);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('FinishedGoodsStock','FinishedGoodsMovements');"))
            .Should().Be(2);

        // Migrasi 012: nomor lot & kedaluwarsa menempel pada baris penerimaan dan lot bahan.
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('GoodsReceiptLines') WHERE name IN ('BatchNumber','ExpiryDate');"))
            .Should().Be(2);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ItemBatches') WHERE name='GoodsReceiptLineId';"))
            .Should().Be(1);

        // Migrasi 013: versi token untuk mencabut sesi, tempat hasil QC, dan dokumen retur.
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name='TokenVersion';"))
            .Should().Be(1);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('QualityChecks','SalesReturns');"))
            .Should().Be(2);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name LIKE 'trg_qualitychecks_%';"))
            .Should().Be(2);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='trg_salesreturns_insert';"))
            .Should().Be(1);
        var mutasi = (string)db.Scalar("SELECT sql FROM sqlite_master WHERE name='FinishedGoodsMovements';")!;
        mutasi.Should().Contain("'Return'");

        // Migrasi 018: akun harga pokok & selisih persediaan, biaya rata-rata produk jadi,
        // dan indeks penelusuran mutasi produk jadi per dokumen sumber.
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM Accounts WHERE Code IN ('5-005','5-006') AND IsActive = 1;"))
            .Should().Be(2);
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM pragma_table_info('FinishedGoodsStock') WHERE name='AverageCostMinorUnits';"))
            .Should().Be(1);
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_finishedgoods_movements_source';"))
            .Should().Be(1);

        // Migrasi 019: lapisan biaya FIFO untuk bahan dan produk jadi, plus nilai rupiah tiap
        // mutasi produk jadi. Tanpa tabel ini, harga pokok mengikuti harga master terakhir
        // (BUG-032) dan mutasi keluar tidak bisa dijelaskan nilai yang sebenarnya keluar.
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='StockCostLayers';"))
            .Should().Be(1);
        var lapisan = (string)db.Scalar("SELECT sql FROM sqlite_master WHERE name='StockCostLayers';")!;
        lapisan.Should().Contain("ItemKind IN ('Ingredient','FinishedGoods')")
            .And.Contain("Quantity >= 0").And.Contain("UnitCostMinorUnits >= 0");
        Convert.ToInt32(db.Scalar(@"
            SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN (
                'idx_stockcostlayers_fifo', 'idx_stockcostlayers_location');"))
            .Should().Be(2);
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM pragma_table_info('FinishedGoodsMovements') WHERE name='ValueMinorUnits';"))
            .Should().Be(1, "mutasi keluar produk jadi harus menyimpan nilai yang benar-benar keluar");
    }

    [Fact]
    public void Migrate_BasisDataVersiLama_SekaliJalanMenutupKekosongan()
    {
        // Simulasi basis data dari versi aplikasi sebelumnya: migrasi berikutnya harus bisa
        // menutup kekurangan tanpa menyentuh baris yang sudah ada.
        using var db = new TempDatabase();
        MigrationRollback.Run(db, "022_PeriodCloseGuard.down.sql");
        MigrationRollback.Run(db, "021_SupplierReturnsAndPayments.down.sql");
        MigrationRollback.Run(db, "020_DiscountTaxAndReceivables.down.sql");
        MigrationRollback.Run(db, "019_CostLayersForInventory.down.sql");
        MigrationRollback.Run(db, "018_CostOfGoodsAndInventoryJournals.down.sql");
        MigrationRollback.Run(db, "017_CoveringIndexesForLargeData.down.sql");
        MigrationRollback.Run(db, "016_QueryPerformanceIndexes.down.sql");
        MigrationRollback.Run(db, "015_InternalNotifications.down.sql");
        MigrationRollback.Run(db, "014_PosDevicesAndBarcode.down.sql");
        MigrationRollback.Run013Down(db);
        MigrationRollback.Run012Down(db);
        MigrationRollback.Run011Down(db);
        db.Exec("DELETE FROM SchemaVersion WHERE Version IN (11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22);");

        Convert.ToInt32(db.Scalar("SELECT MAX(Version) FROM SchemaVersion;")).Should().Be(10);

        new DatabaseMigrator(db.ConnectionString).Migrate();

        Convert.ToInt32(db.Scalar("SELECT MAX(Version) FROM SchemaVersion;"))
            .Should().Be(DatabaseMigrator.LatestEmbeddedVersion());
        db.Scalar("PRAGMA integrity_check;").Should().Be("ok");
        db.Scalar("PRAGMA foreign_key_check;").Should().BeNull();
    }

    [Fact]
    public void Rollback013Lalu012Lalu011_MenghapusSkemaDanKembaliNaikTanpaSisa()
    {
        using var db = new TempDatabase();

        MigrationRollback.Run(db, "022_PeriodCloseGuard.down.sql");
        MigrationRollback.Run(db, "021_SupplierReturnsAndPayments.down.sql");
        MigrationRollback.Run(db, "020_DiscountTaxAndReceivables.down.sql");
        MigrationRollback.Run(db, "019_CostLayersForInventory.down.sql");
        MigrationRollback.Run(db, "018_CostOfGoodsAndInventoryJournals.down.sql");
        MigrationRollback.Run(db, "017_CoveringIndexesForLargeData.down.sql");
        MigrationRollback.Run(db, "016_QueryPerformanceIndexes.down.sql");
        MigrationRollback.Run(db, "015_InternalNotifications.down.sql");
        MigrationRollback.Run(db, "014_PosDevicesAndBarcode.down.sql");
        MigrationRollback.Run013Down(db);
        MigrationRollback.Run012Down(db);
        MigrationRollback.Run011Down(db);

        Convert.ToInt32(db.Scalar(@"
            SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN (
                'idx_salesorders_status_pickup', 'idx_salesorders_created',
                'idx_journallines_account_amounts', 'idx_locationstock_ingredient');"))
            .Should().Be(0, "rollback 016 harus mencabut indeks performa yang ditambahkannya");
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN ('idx_salespayments_order_amount','idx_salesorders_open');"))
            .Should().Be(0);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_salespayments_salesorder';"))
            .Should().Be(1, "rollback 017 harus mengembalikan indeks pembayaran yang digantikannya");
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('QualityChecks','SalesReturns');"))
            .Should().Be(0);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PrintJobs';"))
            .Should().Be(0);
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('SupplierReturns','SupplierReturnLines','SupplierPayments');"))
            .Should().Be(0, "rollback 021 harus mencabut dokumen retur dan pembayaran pemasok");
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name LIKE 'trg_supplier%';"))
            .Should().Be(0, "pemicu pagaran hutang pemasok ikut dicabut bersama tabelnya");
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_goodsreceiptlines_receipt';"))
            .Should().Be(0, "021 menambah indeks di tabel lama, jadi indeks itu harus dicabut manual");
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Notifications';"))
            .Should().Be(0, "rollback 015 harus mencabut kotak notifikasi beserta indeksnya");
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ProductTemplates') WHERE name='Barcode';"))
            .Should().Be(0);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name='TokenVersion';"))
            .Should().Be(0);
        var mutasi = (string)db.Scalar("SELECT sql FROM sqlite_master WHERE name='FinishedGoodsMovements';")!;
        mutasi.Should().NotContain("'Return'");
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE 'FinishedGoods%';"))
            .Should().Be(0);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('SalesOrders') WHERE name='Quantity';"))
            .Should().Be(0);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name LIKE 'trg_salesorders_quantity%';"))
            .Should().Be(0);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM pragma_table_info('GoodsReceiptLines') WHERE name IN ('BatchNumber','ExpiryDate');"))
            .Should().Be(0);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_auditlog_entity';"))
            .Should().Be(0);
        db.Scalar("PRAGMA integrity_check;").Should().Be("ok");

        // Naik lagi ke versi terbaru harus bisa: rollback tidak meninggalkan keadaan yang macet.
        new DatabaseMigrator(db.ConnectionString).Migrate();
        Convert.ToInt32(db.Scalar("SELECT MAX(Version) FROM SchemaVersion;"))
            .Should().Be(DatabaseMigrator.LatestEmbeddedVersion());
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM pragma_table_info('FinishedGoodsMovements') WHERE name='ValueMinorUnits';"))
            .Should().Be(1, "019 harus jalan ulang setelah rollback, bukan dilewati karena nomornya tersisa");
    }

    [Fact]
    public void SalesOrders_JumlahNol_DitolakPemicuBukanHanyaValidasiAplikasi()
    {
        using var db = new TempDatabase();

        var insertNol = () => db.Exec(
            "INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, Quantity) " +
            "VALUES ('ORD-TEST-1', 'Uji', 'Uji', '2030-01-01', 10000, 0);");
        insertNol.Should().Throw<SqliteException>()
            .WithMessage("*Jumlah pesanan harus lebih dari nol*");

        db.Exec("INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, Quantity) " +
                "VALUES ('ORD-TEST-2', 'Uji', 'Uji', '2030-01-01', 10000, 3);");
        var ubahNol = () => db.Exec("UPDATE SalesOrders SET Quantity = 0 WHERE OrderNumber = 'ORD-TEST-2';");
        ubahNol.Should().Throw<SqliteException>();

        // Baris lama tanpa jumlah tetap sah dan bernilai satu butir, bukan nol.
        Convert.ToDouble(db.Scalar("SELECT Quantity FROM SalesOrders WHERE OrderNumber = 'ORD-TEST-2';"))
            .Should().Be(3);
    }

    [Fact]
    public void FinishedGoodsStock_TidakBolehNegatifDanUnikPerLokasiProduk()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                "VALUES ('Roti Tawar Uji', 'Roti', 12000, 1);");
        var produk = Convert.ToInt32(db.Scalar("SELECT Id FROM ProductTemplates WHERE Name = 'Roti Tawar Uji';"));

        var negatif = () => db.Exec(
            $"INSERT INTO FinishedGoodsStock (LocationId, ProductTemplateId, Quantity) VALUES (3, {produk}, -1);");
        negatif.Should().Throw<SqliteException>();

        db.Exec($"INSERT INTO FinishedGoodsStock (LocationId, ProductTemplateId, Quantity) VALUES (3, {produk}, 10);");
        var dobelLokasi = () => db.Exec(
            $"INSERT INTO FinishedGoodsStock (LocationId, ProductTemplateId, Quantity) VALUES (3, {produk}, 5);");
        dobelLokasi.Should().Throw<SqliteException>();
    }


    [Fact]
    public void Migrate_BentukBangunanDasar_TerpasangDariKosong()
    {
        using var db = new TempDatabase();

        var ddl = (string)db.Scalar("SELECT sql FROM sqlite_master WHERE name='LocationStock';")!;
        ddl.Should().Contain("CHECK (Quantity >= 0)");
        ddl.Should().Contain("UpdatedAt");
        db.Scalar("SELECT COUNT(*) FROM pragma_table_info('StockMovements') WHERE name='LocationId';")
            .Should().Be(Convert.ToInt32(1));
        db.Scalar("PRAGMA integrity_check;").Should().Be("ok");

        // Pemicu integritas v008 harus benar-benar terpasang, bukan hanya ada di file.
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='trigger';"))
            .Should().BeGreaterThan(10);
        Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND sql LIKE '%SalesPayments%';"))
            .Should().Be(1);

        // Penghitung nomor dokumen: kunci (jenis, hari) supaya dua hari berbeda tidak salaman.
        var nomor = (string)db.Scalar("SELECT sql FROM sqlite_master WHERE name='DocumentSequences';")!;
        nomor.Should().Contain("PRIMARY KEY (Kind, DayKey)");
    }

    [Fact]
    public void Migrate_VersiSkemaTidakTerbaca_BerhentiTanpaMengubahBasisData()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bakery-migrator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "rusak.db");
        var connString = $"Data Source={path};";
        var cadanganDir = System.IO.Path.Combine(dir, "backups");

        try
        {
            // Tabel ada dengan tipe yang salah: bukan "belum ada tabel", jadi bukan skema 0.
            using (var conn = new SqliteConnection(connString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE SchemaVersion (Version TEXT PRIMARY KEY, AppliedAt TEXT NOT NULL);" +
                    "CREATE TABLE Users (Id INTEGER PRIMARY KEY, Username TEXT);" +
                    "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES ('rusak', 'x');" +
                    "INSERT INTO Users (Username) VALUES ('pemilik-lama');";
                cmd.ExecuteNonQuery();
            }

            var aksi = () => new DatabaseMigrator(connString, cadanganDir).Migrate();

            aksi.Should().Throw<InvalidOperationException>().WithMessage("*tidak bisa dibaca*");
            Directory.Exists(cadanganDir).Should().BeFalse(
                "start harus berhenti sebelum migrasi/cadangan menyentuh data yang versinya tidak jelas");

            using (var cek = new SqliteConnection(connString))
            {
                cek.Open();
                using var baca = cek.CreateCommand();
                baca.CommandText = "SELECT Username FROM Users;";
                Convert.ToString(baca.ExecuteScalar()).Should().Be("pemilik-lama");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { /* berkas sementara */ }
        }
    }

    [Fact]
    public void Migrate_AdaTabelPenggunaTanpaBarisVersi_SalinanPengamanDibuatLebihDulu()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bakery-migrator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "lama.db");
        var connString = $"Data Source={path};";
        var cadanganDir = System.IO.Path.Combine(dir, "backups");

        try
        {
            using (var conn = new SqliteConnection(connString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE SchemaVersion (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);" +
                    "CREATE TABLE Users (Id INTEGER PRIMARY KEY, Username TEXT, PasswordHash TEXT NOT NULL DEFAULT '');" +
                    "INSERT INTO Users (Username, PasswordHash) VALUES ('pemilik-lama', 'x');";
                cmd.ExecuteNonQuery();
            }

            // Migrasi dari nol bisa berhenti di tabel yang sudah ada; yang diuji adalah bahwa
            // data pengguna tidak disentuh sebelum salinan pengaman benar-benar terbit.
            try { new DatabaseMigrator(connString, cadanganDir).Migrate(); }
            catch (Exception) { /* hasil akhir migrasi bukan inti uji ini */ }

            var cadangan = Directory.GetFiles(cadanganDir,
                $"pre-upgrade-v0-to-v{DatabaseMigrator.LatestEmbeddedVersion()}-*.db");
            cadangan.Should().HaveCount(1, "tabel pengguna tanpa baris versi tetap harus dicadangkan");
            DatabaseBackupService.ReadSchemaVersion(cadangan[0]).Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { /* berkas sementara */ }
        }
    }

    [Fact]
    public void Migrate_RerunOnSameDatabase_IsIdempotent()
    {
        var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bakedb-{Guid.NewGuid():N}.db");
        var connString = $"Data Source={filePath};";
        try
        {
            new DatabaseMigrator(connString).Migrate();
            var act = () => new DatabaseMigrator(connString).Migrate();

            act.Should().NotThrow();
        }
        finally
        {
            // Pool SQLite menutup koneksinya di latar belakang; menunggu berkas benar-benar
            // bebas mencegah tes gagal karena pegangan yang belum lepas, bukan karena produk.
            SqliteConnection.ClearAllPools();
            DatabaseBackupService.TungguBerkasBebas(filePath);
            if (File.Exists(filePath)) File.Delete(filePath);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(filePath + suffix)) DatabaseBackupService.HapusSidecar(filePath + suffix);
        }
    }

    [Fact]
    public void LocationStock_CheckConstraint_RejectsNegativeQuantityAtDatabaseLevel()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                "VALUES ('Gula', 'kg', 15000, 0, 0, 1);");
        var ingredientId = Convert.ToInt32(db.Scalar("SELECT last_insert_rowid();"));

        var act = () => db.Exec(
            $"INSERT INTO LocationStock (LocationId, IngredientId, Quantity) VALUES (1, {ingredientId}, -1);");

        act.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Migrasi022_PagarPeriodeTerpasangDiBasisData()
    {
        using var db = new TempDatabase();

        Convert.ToInt32(db.Scalar(@"
            SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name IN (
                'trg_journalentries_periode_insert', 'trg_journalentries_periode_update',
                'trg_fiscalperiods_format');"))
            .Should().Be(3, "pagar periode harus hidup di basis data, bukan hanya di layanan");
        Convert.ToInt32(db.Scalar(@"
            SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_fiscalperiods_startdate';"))
            .Should().Be(1, "satu rentang tanggal hanya boleh punya satu baris periode");

        // Pemicu pembalik jurnal bekerja dengan UPDATE Status; kalau pemicu tanggal ikut memagari
        // semua kolom, menutup bulan lama otomatis membuat seluruh pembatalan di bulan itu macet.
        var updateHanyaStatus = (string)db.Scalar(
            "SELECT sql FROM sqlite_master WHERE name='trg_journalentries_periode_update';")!;
        updateHanyaStatus.Should().Contain("BEFORE UPDATE OF TransactionDate");
    }

    [Fact]
    public void JurnalKePeriodeTertutup_DitolakPemicuTanpaMelewatAplikasi()
    {
        using var db = new TempDatabase();

        // Entri Januari dibuat sebelum bulan itu ditutup: pemicu hanya menahan penulisan baru,
        // riwayat yang sudah ada harus tetap ada dan bisa dibalik.
        db.Exec("INSERT INTO JournalEntries (Description, TransactionDate, Status) " +
                "VALUES ('Entri Januari lama', '2026-01-05', 'Posted');");
        db.Exec("INSERT INTO FiscalPeriods (Name, StartDate, EndDate, Status, ClosedAt) " +
                "VALUES ('Januari 2026', '2026-01-01', '2026-01-31', 'Closed', '2026-02-03 07:11:02');");

        var tulisDiBulanTutup = () => db.Exec(
            "INSERT INTO JournalEntries (Description, TransactionDate, Status) " +
            "VALUES ('Sisipan mentah ke bulan tertutup', '2026-01-15', 'Posted');");
        tulisDiBulanTutup.Should().Throw<SqliteException>()
            .WithMessage("*Periode buku ini sudah ditutup*");

        // Entri dari bulan yang masih terbuka boleh ada; memindah tanggalnya ke bulan tertutup
        // adalah cara paling diam-diam untuk mengubah laba rugi yang sudah final.
        db.Exec("INSERT INTO JournalEntries (Description, TransactionDate, Status) " +
                "VALUES ('Entri Februari', '2026-02-10', 'Posted');");
        var geserTanggal = () => db.Exec(
            "UPDATE JournalEntries SET TransactionDate = '2026-01-20' WHERE TransactionDate = '2026-02-10';");
        geserTanggal.Should().Throw<SqliteException>()
            .WithMessage("*tidak boleh dipindah ke periode buku yang sudah ditutup*");

        // Pembalik entri lama mengubah Status dan menambah entri baru bertanggal hari ini,
        // jadi kolom lain selain tanggal harus tetap boleh ditulis di baris bulan tertutup.
        db.Exec("UPDATE JournalEntries SET Status = 'Reversed' WHERE TransactionDate = '2026-01-05';");
        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM JournalEntries WHERE TransactionDate = '2026-01-05' AND Status = 'Reversed';"))
            .Should().Be(1, "menutup periode mengunci gerbang tulis, bukan menghapus riwayat yang ada");
        db.Scalar("PRAGMA integrity_check;").Should().Be("ok");
    }

    [Fact]
    public void PeriodeBuku_FormatTanggalSalahRentangTerbalikAtauDuplikat_DitolakPemicu()
    {
        using var db = new TempDatabase();

        var formatSalah = () => db.Exec(
            "INSERT INTO FiscalPeriods (Name, StartDate, EndDate) VALUES ('Buruk', '2026/01/01', '2026-01-31');");
        formatSalah.Should().Throw<SqliteException>().WithMessage("*awal periode harus berformat yyyy-MM-dd*");

        var rentangTerbalik = () => db.Exec(
            "INSERT INTO FiscalPeriods (Name, StartDate, EndDate) VALUES ('Terbalik', '2026-03-31', '2026-03-01');");
        rentangTerbalik.Should().Throw<SqliteException>().WithMessage("*Rentang periode buku tidak sah*");

        db.Exec("INSERT INTO FiscalPeriods (Name, StartDate, EndDate) VALUES ('Maret 2026', '2026-03-01', '2026-03-31');");
        var dobel = () => db.Exec(
            "INSERT INTO FiscalPeriods (Name, StartDate, EndDate) VALUES ('Maret kembar', '2026-03-01', '2026-03-31');");
        dobel.Should().Throw<SqliteException>();
    }
}
