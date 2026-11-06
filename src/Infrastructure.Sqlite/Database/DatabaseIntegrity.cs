using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Seruni22Bakery.Infrastructure.Sqlite.Database;

/// <summary>
/// Satu pemeriksaan mandiri: apa yang diuji, apakah lolos, dan angka/isi sebenarnya yang
/// ditemukan. Detail dipakai langsung oleh operator, jadi tidak boleh berbunyi "OK" saja
/// saat ada yang rusak.
/// </summary>
public sealed record IntegrityCheck(string Name, bool Passed, string Detail);

/// <summary>
/// Pemeriksaan kondisi basis data yang bisa dilihat pengguna tanpa membuka berkas. Semua
/// pertanyaan diajukan ke SQLite itu sendiri, bukan ke perkiraan di aplikasi, supaya
/// "basis data sehat" berarti ada bukti dari mesin penyimpannya.
/// </summary>
public sealed class DatabaseIntegrity
{
    private readonly DbConnectionFactory _factory;

    public DatabaseIntegrity(DbConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<IntegrityCheck> Check()
    {
        using var conn = _factory.CreateConnection();
        return
        [
            CekKunciAsingAktif(conn),
            CekIntegrityCheck(conn),
            CekForeignKeysMelanggar(conn),
            CekVersiSkema(conn),
            CekJurnalSeimbang(conn),
            CekRujukanDokumenYatim(conn),
            CekNomorDokumenUnik(conn),
            CekPenghitungDokumen(conn),
            CekStokNegatif(conn),
            CekLapisanBiayaMenutupiStok(conn),
            CekNilaiPersediaanJadi(conn),
            CekNilaiLapisanBahanSesuaiAkun(conn),
            CekJurnalStokLayarBahan(conn),
            CekStokMasterNonaktif(conn),
            CekHutangPemasokTidakNegatif(conn),
            CekDokumenPemasokBerjurnal(conn),
            CekPeriodeTertutupMengunci(conn),
            CekSatuanResepTerdaftar(conn),
        ];
    }

    /// <summary>
    /// PRAGMA foreign_keys adalah pengaturan per koneksi, bukan properti berkas. Kalau hanya
    /// koneksi saat startup yang menyalakannya, koneksi berikutnya bisa menulis baris yatim
    /// tanpa galat apa pun — jadi keadaan ini ikut dilaporkan ke pengguna.
    /// </summary>
    private static IntegrityCheck CekKunciAsingAktif(SqliteConnection conn)
    {
        var aktif = Skalar(conn, "PRAGMA foreign_keys;") == "1";
        var timeout = Skalar(conn, "PRAGMA busy_timeout;");
        return baru("foreign_keys aktif di koneksi ini", aktif,
            $"PRAGMA foreign_keys = {(aktif ? "ON" : "OFF")}, busy_timeout = {timeout} ms. " +
            "Kunci asing hanya berlaku pada koneksi yang menyalakannya.");
    }

    private static IntegrityCheck CekIntegrityCheck(SqliteConnection conn)
    {
        var baris = Baris(conn, "PRAGMA integrity_check;");
        var lolos = baris.Count == 1 && baris[0] == "ok";
        return baru("PRAGMA integrity_check", lolos,
            lolos ? "struktur halaman dan indeks konsisten" : Ringkas(baris, "kerusakan: "));
    }

    private static IntegrityCheck CekForeignKeysMelanggar(SqliteConnection conn)
    {
        var baris = Baris(conn, "PRAGMA foreign_key_check;");
        return baru("PRAGMA foreign_key_check", baris.Count == 0,
            baris.Count == 0 ? "tidak ada baris yang menunjuk induk yang hilang"
                             : $"{baris.Count} baris melanggar: {Rangkai(baris)}");
    }

    private static IntegrityCheck CekVersiSkema(SqliteConnection conn)
    {
        var terkini = Skalar(conn, "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;");
        var tersedia = VersiTersedia();
        var lolos = int.TryParse(terkini, out var sudah) && sudah == tersedia;
        return baru("versi skema", lolos,
            $"terpasang {terkini}, skema terbaru yang ikut terkemas {tersedia}. " +
            (lolos ? "semua migrasi sudah dijalankan."
                   : "migrasi belum jalan penuh — restart aplikasi atau jalankan Migration.Tool."));
    }

    /// <summary>
    /// Entri posted yang debetnya tidak sama dengan kredit membuat laporan salah tanpa pernah
    /// terlihat galat. Baris tanpa satu pun journal line juga dihitung: nominalnya nol di
    /// laporan tetapi tetap muncul sebagai transaksi.
    /// </summary>
    private static IntegrityCheck CekJurnalSeimbang(SqliteConnection conn)
    {
        var rusak = Baris(conn, @"
            SELECT je.Id || '#',
                   CASE WHEN COUNT(jl.Id) = 0 THEN 'tanpa baris'
                        ELSE 'debet ' || SUM(COALESCE(jl.DebitAmount, 0)) ||
                             ' vs kredit ' || SUM(COALESCE(jl.CreditAmount, 0)) END
            FROM JournalEntries je
            LEFT JOIN JournalLines jl ON jl.JournalEntryId = je.Id
            WHERE je.Status = 'Posted'
            GROUP BY je.Id
            HAVING COUNT(jl.Id) = 0 OR SUM(COALESCE(jl.DebitAmount, 0)) <> SUM(COALESCE(jl.CreditAmount, 0));");
        var total = Skalar(conn,
            "SELECT COUNT(*) FROM JournalEntries WHERE Status = 'Posted';");
        return baru("jurnal posted seimbang", rusak.Count == 0,
            rusak.Count == 0
                ? $"{total} entri posted, semuanya seimbang dan punya baris."
                : $"{rusak.Count} dari {total} entri bermasalah: {Rangkai(rusak)}");
    }

    /// <summary>
    /// SourceDocumentId/Type tidak punya kunci asing karena jenisnya bebas, jadi rujukan yang
    /// yang menunjuk dokumen yang sudah hilang hanya bisa ditemukan dengan menanyakan kedua tabel.
    /// </summary>
    private static IntegrityCheck CekRujukanDokumenYatim(SqliteConnection conn)
    {
        var yatim = Baris(conn, @"
            SELECT je.Id || ' (' || je.SourceDocumentType || '#' || je.SourceDocumentId || ')'
            FROM JournalEntries je
            WHERE je.SourceDocumentId IS NOT NULL AND (
                  (je.SourceDocumentType = 'SalesOrder' AND NOT EXISTS (SELECT 1 FROM SalesOrders s WHERE s.Id = je.SourceDocumentId))
               OR (je.SourceDocumentType = 'WorkOrder'  AND NOT EXISTS (SELECT 1 FROM WorkOrders w WHERE w.Id = je.SourceDocumentId))
               OR (je.SourceDocumentType = 'SupplierReturn'  AND NOT EXISTS (SELECT 1 FROM SupplierReturns r WHERE r.Id = je.SourceDocumentId))
               OR (je.SourceDocumentType = 'SupplierPayment' AND NOT EXISTS (SELECT 1 FROM SupplierPayments p WHERE p.Id = je.SourceDocumentId)));");
        return baru("rujukan dokumen jurnal utuh", yatim.Count == 0,
            yatim.Count == 0 ? "setiap entri ber-id dokumen sumber menunjuk dokumen yang masih ada"
                             : $"{yatim.Count} entri menggantung: {Rangkai(yatim)}");
    }

    private static IntegrityCheck CekNomorDokumenUnik(SqliteConnection conn)
    {
        var dobel = Baris(conn, @"
            SELECT t || ' ' || n || ' x' || c FROM (
                SELECT 'ORD' AS t, OrderNumber AS n, COUNT(*) AS c FROM SalesOrders GROUP BY OrderNumber HAVING COUNT(*) > 1
                UNION ALL
                SELECT 'SPK', OrderNumber, COUNT(*) FROM WorkOrders GROUP BY OrderNumber HAVING COUNT(*) > 1
                UNION ALL
                SELECT 'PO', PoNumber, COUNT(*) FROM PurchaseOrders GROUP BY PoNumber HAVING COUNT(*) > 1
                UNION ALL
                SELECT 'RTB', ReturnNumber, COUNT(*) FROM SupplierReturns GROUP BY ReturnNumber HAVING COUNT(*) > 1
                UNION ALL
                SELECT 'PPB', PaymentNumber, COUNT(*) FROM SupplierPayments GROUP BY PaymentNumber HAVING COUNT(*) > 1);");
        return baru("nomor dokumen tidak ada yang kembar", dobel.Count == 0,
            dobel.Count == 0 ? "setiap nomor dokumen hanya muncul sekali"
                             : $"{dobel.Count} nomor kembar: {Rangkai(dobel)}");
    }

    /// <summary>
    /// Penghitung naik setiap kali nomor dikocok; kalau jumlah dokumen sehari melampaui
    /// LastValue, ada nomor yang dipakai dua kali (biasanya sisa data sebelum penomoran
    /// deterministik) dan dokumen berikutnya akan ditolak.
    /// </summary>
    private static IntegrityCheck CekPenghitungDokumen(SqliteConnection conn)
    {
        var bocor = Baris(conn, @"
            WITH docs AS (
                SELECT 'Order'         AS kind, substr(OrderNumber, instr(OrderNumber, '-') + 1, 8) AS day FROM SalesOrders
                UNION ALL SELECT 'WorkOrder',      substr(OrderNumber, instr(OrderNumber, '-') + 1, 8) FROM WorkOrders
                UNION ALL SELECT 'PurchaseOrder',  substr(PoNumber,     instr(PoNumber, '-') + 1, 8)     FROM PurchaseOrders
                UNION ALL SELECT 'SupplierReturn', substr(ReturnNumber, instr(ReturnNumber, '-') + 1, 8) FROM SupplierReturns
                UNION ALL SELECT 'SupplierPayment',substr(PaymentNumber,instr(PaymentNumber, '-') + 1, 8) FROM SupplierPayments),
            jumlah AS (
                SELECT kind, day, COUNT(*) AS n FROM docs
                WHERE day GLOB '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
                GROUP BY kind, day)
            SELECT ds.Kind || '/' || ds.DayKey || ': penghitung ' || ds.LastValue || ', dokumen ' || COALESCE(j.n, 0)
            FROM DocumentSequences ds
            LEFT JOIN jumlah j ON j.kind = ds.Kind AND j.day = replace(ds.DayKey, '-', '')
            WHERE COALESCE(j.n, 0) > ds.LastValue;");
        var total = Skalar(conn, "SELECT COUNT(*) FROM DocumentSequences;");
        return baru("penghitung dokumen menutupi isinya", bocor.Count == 0,
            bocor.Count == 0 ? $"{total} penghitung hari, tidak ada yang tertinggal di belakang."
                             : $"{bocor.Count} penghitung lebih kecil dari jumlah dokumennya: {Rangkai(bocor)}");
    }

    private static IntegrityCheck CekStokNegatif(SqliteConnection conn)
    {
        var negatif = Baris(conn, @"
            SELECT i.Name || ' @ ' || l.Name || ' = ' || qs.Quantity
            FROM (SELECT LocationId, IngredientId, Quantity FROM LocationStock WHERE Quantity < 0) qs
            JOIN Ingredients i ON i.Id = qs.IngredientId
            JOIN StorageLocations l ON l.Id = qs.LocationId;");
        return baru("tidak ada stok negatif", negatif.Count == 0,
            negatif.Count == 0
                ? Skalar(conn, "SELECT COUNT(*) FROM LocationStock;") + " baris stok semuanya nol atau positif"
                : $"{negatif.Count} baris negatif: {Rangkai(negatif)}");
    }

    /// <summary>
    /// Jumlah fisik dan nilai lapisan harus bergerak bersama. Stok di rak tanpa lapisan berarti
    /// tidak ada satu pun harga yang bisa dipakai membukukan HPP atau transfer, dan lapisan yang
    /// lebih besar dari fisik berarti ada barang yang tercatat tapi tidak ada. Kedua keadaan
    /// lahir dari jalur tulis yang melewati StockLedger/FinishedGoodsLedger.
    /// </summary>
    private static IntegrityCheck CekLapisanBiayaMenutupiStok(SqliteConnection conn)
    {
        var bocor = Baris(conn, @"
            SELECT 'Bahan ' || i.Name || ' di ' || sl.Name || ': fisik ' || printf('%g', ls.Quantity) ||
                   ', lapisan ' || printf('%g', COALESCE(l.qty, 0))
              FROM LocationStock ls
              JOIN Ingredients i ON i.Id = ls.IngredientId
              JOIN StorageLocations sl ON sl.Id = ls.LocationId
              LEFT JOIN (SELECT ItemId, LocationId, SUM(Quantity) AS qty FROM StockCostLayers
                          WHERE ItemKind = 'Ingredient' GROUP BY ItemId, LocationId) l
                     ON l.ItemId = ls.IngredientId AND l.LocationId = ls.LocationId
             WHERE ABS(ls.Quantity - COALESCE(l.qty, 0)) > 0.0000001
            UNION ALL
            SELECT 'Produk ' || pt.Name || ' di ' || sl.Name || ': fisik ' || printf('%g', fgs.Quantity) ||
                   ', lapisan ' || printf('%g', COALESCE(l.qty, 0))
              FROM FinishedGoodsStock fgs
              JOIN ProductTemplates pt ON pt.Id = fgs.ProductTemplateId
              JOIN StorageLocations sl ON sl.Id = fgs.LocationId
              LEFT JOIN (SELECT ItemId, LocationId, SUM(Quantity) AS qty FROM StockCostLayers
                          WHERE ItemKind = 'FinishedGoods' GROUP BY ItemId, LocationId) l
                     ON l.ItemId = fgs.ProductTemplateId AND l.LocationId = fgs.LocationId
             WHERE ABS(fgs.Quantity - COALESCE(l.qty, 0)) > 0.0000001
            UNION ALL
            SELECT 'Lapisan yatim ' || l.ItemKind || ' #' || l.ItemId || ' di ' || sl.Name ||
                   ': lapisan ' || printf('%g', l.qty) || ', stok fisiknya tidak punya baris'
              FROM (SELECT ItemKind, ItemId, LocationId, SUM(Quantity) AS qty FROM StockCostLayers
                     GROUP BY ItemKind, ItemId, LocationId) l
              JOIN StorageLocations sl ON sl.Id = l.LocationId
             WHERE NOT EXISTS (
                   SELECT 1 FROM LocationStock ls
                    WHERE l.ItemKind = 'Ingredient'
                      AND ls.IngredientId = l.ItemId AND ls.LocationId = l.LocationId)
               AND NOT EXISTS (
                   SELECT 1 FROM FinishedGoodsStock fgs
                    WHERE l.ItemKind = 'FinishedGoods'
                      AND fgs.ProductTemplateId = l.ItemId AND fgs.LocationId = l.LocationId)
            ORDER BY 1;");
        return baru("lapisan biaya menutupi stok fisik", bocor.Count == 0,
            bocor.Count == 0
                ? Skalar(conn, "SELECT COUNT(*) FROM StockCostLayers;") +
                  " lapisan biaya, jumlahnya sama dengan stok di semua lokasi."
                : $"{bocor.Count} lokasi+barang punya stok dan lapisan yang tidak sama: {Rangkai(bocor)}. " +
                  "Semua perubahan stok harus lewat layar (Formulir Mutasi Bahan, Pemeriksaan Fisik, " +
                  "Penerimaan Barang, Produksi, Kasir); suntingan langsung membuat harga barang tidak " +
                  "bisa dibukukan.");
    }

    /// <summary>
    /// Nilai stok produk jadi yang dipakai menu Inventaris harus sama dengan saldo akun 1-006.
    /// Sejak lapisan biaya ada, pembandingnya adalah total nilai lapisan — bukan jumlah dikali
    /// biaya rata-rata, yang bergeser beberapa sen tiap mutasi dan tidak pernah bisa menjelaskan
    /// harga batch yang benar-benar terjual.
    /// </summary>
    private static IntegrityCheck CekNilaiPersediaanJadi(SqliteConnection conn)
    {
        var saldo = Angka(conn, @"
            SELECT COALESCE(SUM(jl.DebitAmount - jl.CreditAmount), 0)
              FROM JournalLines jl
              JOIN Accounts a ON a.Id = jl.AccountId
              JOIN JournalEntries je ON je.Id = jl.JournalEntryId
             WHERE a.Code = '1-006' AND je.Status IN ('Posted','Reversed');");
        var nilaiStok = Angka(conn, @"
            SELECT COALESCE(SUM(CAST(ROUND(Quantity * UnitCostMinorUnits) AS INTEGER)), 0)
              FROM StockCostLayers WHERE ItemKind = 'FinishedGoods';");

        // Biaya per unit disimpan sebagai bilangan bulat, jadi setiap mutasi bisa
        // menyisakan pembulatan di bawah satu sen. Selisih sebesar itu bukan kerusakan.
        const long toleransi = 100;
        var selisih = saldo - nilaiStok;
        var lolos = Math.Abs(selisih) <= toleransi;

        return baru("nilai stok produk jadi sama dengan saldo 1-006", lolos,
            lolos
                ? $"saldo 1-006 = {saldo}, nilai lapisan stok = {nilaiStok}, selisih {selisih} (toleransi {toleransi} sen)."
                : $"saldo 1-006 = {saldo}, nilai lapisan stok = {nilaiStok}, selisih {selisih} sen melebihi toleransi " +
                  $"{toleransi}. Produksi sebelum perbaikan kapitalisasi mencatat tenaga kerja/overhead " +
                  "sebagai beban tanpa menambah nilai persediaan; periksa jurnal WorkOrder lama.");
    }

    /// <summary>
    /// Setiap sen yang tersimpan di lapisan bahan harus punya pasangan di akun persediaan untuk
    /// lokasi itu (1-003 gudang, 1-004 dapur), dan sebaliknya. Bahan yang ditumpuk di lokasi
    /// etalase tidak punya akun persediaan bahan, jadi ikut dilaporkan di sini.
    /// </summary>
    private static IntegrityCheck CekNilaiLapisanBahanSesuaiAkun(SqliteConnection conn)
    {
        var laporan = new List<string>();
        foreach (var (kode, jenis) in new[] { ("1-003", "Warehouse"), ("1-004", "Kitchen") })
        {
            var saldo = Angka(conn, $@"
                SELECT COALESCE(SUM(jl.DebitAmount - jl.CreditAmount), 0)
                  FROM JournalLines jl
                  JOIN Accounts a ON a.Id = jl.AccountId
                  JOIN JournalEntries je ON je.Id = jl.JournalEntryId
                 WHERE a.Code = '{kode}' AND je.Status IN ('Posted','Reversed');");
            var nilai = Angka(conn, $@"
                SELECT COALESCE(SUM(CAST(ROUND(l.Quantity * l.UnitCostMinorUnits) AS INTEGER)), 0)
                  FROM StockCostLayers l
                  JOIN StorageLocations sl ON sl.Id = l.LocationId
                 WHERE l.ItemKind = 'Ingredient' AND sl.Type = '{jenis}';");
            var selisih = saldo - nilai;
            if (selisih != 0)
                laporan.Add($"akun {kode} bersaldo {saldo} sedangkan lapisan {jenis} bernilai {nilai} " +
                            $"(selisih {selisih} sen)");
        }

        var asing = Baris(conn, @"
            SELECT sl.Name || ' = ' ||
                   CAST(COALESCE(SUM(CAST(ROUND(l.Quantity * l.UnitCostMinorUnits) AS INTEGER)), 0) AS TEXT) || ' sen'
              FROM StockCostLayers l
              JOIN StorageLocations sl ON sl.Id = l.LocationId
             WHERE l.ItemKind = 'Ingredient' AND sl.Type NOT IN ('Warehouse','Kitchen')
             GROUP BY sl.Name;");
        laporan.AddRange(asing.Select(a => $"bahan disimpan di lokasi non-persediaan: {a}"));

        return baru("nilai lapisan bahan sama dengan saldo akun persediaan", laporan.Count == 0,
            laporan.Count == 0
                ? "1-003 dan 1-004 persis sama dengan nilai lapisan gudang dan dapur."
                : $"{string.Join("; ", laporan)}. Saldo neraca tidak lagi menjelaskan barang di rak. " +
                  "Stok layar Bahan Baku yang belum berjurnal ditambal dengan --rekonsiliasi-stok-bahan; " +
                  "selisih fisik dibukukan lewat Pemeriksaan Fisik (Stok Opname) supaya lawannya menjadi " +
                  "Beban Selisih Persediaan.");
    }

    /// <summary>
    /// Stok yang diketik di layar Bahan Baku baru membukukan jurnalnya sejak perbaikan ini.
    /// Bahan yang dibuat sebelumnya punya mutasi bernilai tanpa satu pun entri, sehingga saldo
    /// akun persediaan lebih kecil daripada barang di rak — dan ketidakcocokan itu tidak bisa
    /// terlihat dari pemeriksaan lain karena jurnal yang ada semuanya tetap seimbang.
    /// </summary>
    private static IntegrityCheck CekJurnalStokLayarBahan(SqliteConnection conn)
    {
        var belum = Baris(conn, @"
            SELECT i.Name || ' = ' || CAST(s.qty AS REAL) || ' ' || i.Unit ||
                   ', nilai ' || CAST(s.nilai AS INTEGER) || ' sen'
              FROM (SELECT m.IngredientId, SUM(m.Quantity) AS qty,
                           SUM(m.Quantity) * i.PricePerUnit AS nilai
                      FROM StockMovements m
                      JOIN Ingredients i ON i.Id = m.IngredientId
                     WHERE m.SourceType = 'IngredientMaster'
                     GROUP BY m.IngredientId, i.PricePerUnit) s
              JOIN Ingredients i ON i.Id = s.IngredientId
             WHERE s.nilai <> 0
               AND NOT EXISTS (SELECT 1 FROM JournalEntries je
                                WHERE je.SourceDocumentType = 'IngredientMaster'
                                  AND je.SourceDocumentId = s.IngredientId);");
        var total = Skalar(conn,
            "SELECT COUNT(DISTINCT IngredientId) FROM StockMovements WHERE SourceType = 'IngredientMaster';");
        return baru("stok layar Bahan Baku punya jurnal", belum.Count == 0,
            belum.Count == 0
                ? $"{total} bahan pernah diubah dari layar Bahan Baku, semuanya sudah dibukukan."
                : $"{belum.Count} dari {total} bahan menumpuk stok tanpa satu pun jurnal: {Rangkai(belum)}. " +
                  "Saldo akun persediaan kurang sebesar nilai itu. Stok yang diketik dari layar " +
                  "Bahan Baku kini dibukukan (stok awal melawan Modal, koreksi melawan Beban " +
                  "Selisih Persediaan); data lama dibukukan sekali dengan perintah " +
                  "--rekonsiliasi-stok-bahan atau lewat opname.");
    }

    /// <summary>
    /// Menonaktifkan master tidak menghapus stoknya. Selama sisa stok itu ada, nilainya tetap
    /// menjadi aset di akun persediaan, tetapi masternya sudah hilang dari daftar bahan dan
    /// katalog produk — sehingga angka di layar, di laporan, dan di neraca tidak lagi mudah
    /// dibandingkan. Tombol nonaktif kini menolak master yang masih berstok; pemeriksaan ini
    /// menangkap sisa keadaan itu (misalnya data sebelum pagar terpasang).
    /// </summary>
    private static IntegrityCheck CekStokMasterNonaktif(SqliteConnection conn)
    {
        var menggantung = Baris(conn, @"
            SELECT 'Bahan ' || i.Name || ' di ' || sl.Name || ' = ' || printf('%g', ls.Quantity) || ' ' || i.Unit
              FROM LocationStock ls
              JOIN Ingredients i ON i.Id = ls.IngredientId
              JOIN StorageLocations sl ON sl.Id = ls.LocationId
             WHERE i.IsActive = 0 AND ls.Quantity <> 0
            UNION ALL
            SELECT 'Produk ' || pt.Name || ' di ' || sl.Name || ' = ' || printf('%g', fgs.Quantity) || ' unit'
              FROM FinishedGoodsStock fgs
              JOIN ProductTemplates pt ON pt.Id = fgs.ProductTemplateId
              JOIN StorageLocations sl ON sl.Id = fgs.LocationId
             WHERE pt.IsActive = 0 AND fgs.Quantity <> 0
            ORDER BY 1;");
        return baru("stok master nonaktif sudah habis", menggantung.Count == 0,
            menggantung.Count == 0
                ? "tidak ada stok yang menggantung di master nonaktif"
                : $"{menggantung.Count} baris stok masih dihitung di akun persediaan padahal " +
                  $"masternya sudah dinonaktifkan: {Rangkai(menggantung)}. Habiskan stoknya lewat " +
                  "Formulir Mutasi Bahan atau Pemeriksaan Fisik (Stok Opname) supaya selisihnya " +
                  "ikut membukukan jurnal; setelah stok nol, laporan dan neraca menunjuk barang " +
                  "yang sama.");
    }

    /// <summary>
    /// Satuan baris resep harus satuan stok bahan atau satuan turunan yang terdaftar. Baris yang
    /// menyebut satuan asing tidak bisa dihitung, dan kalau dihitung dengan faktor 1 HPP-nya
    /// salah seratus kali lipat (250 g dibaca 250 kg). Pemicu menahan penulisan baru; pemeriksaan
    /// ini menangkap data lama atau suntingan langsung ke basis data.
    /// </summary>
    private static IntegrityCheck CekSatuanResepTerdaftar(SqliteConnection conn)
    {
        var asing = Baris(conn, @"
            SELECT pt.Name || ' / ' || i.Name || ' memakai ' || trim(rl.Unit)
              FROM RecipeLines rl
              JOIN ProductTemplates pt ON pt.Id = rl.ProductTemplateId
              JOIN Ingredients i ON i.Id = rl.IngredientId
             WHERE rl.Unit IS NOT NULL AND trim(rl.Unit) <> ''
               AND lower(trim(rl.Unit)) <> lower(trim(i.Unit))
               AND NOT EXISTS (SELECT 1 FROM IngredientUnits iu
                                WHERE iu.IngredientId = rl.IngredientId
                                  AND lower(trim(iu.Unit)) = lower(trim(rl.Unit)));");
        var total = Skalar(conn,
            "SELECT COUNT(*) FROM RecipeLines WHERE Unit IS NOT NULL AND trim(Unit) <> '';");
        return baru("satuan baris resep terdaftar pada bahannya", asing.Count == 0,
            asing.Count == 0
                ? $"{total} baris resep memakai satuan; semuanya cocok dengan satuan stok atau satuan turunannya."
                : $"{asing.Count} baris memakai satuan yang tidak terdaftar: {Rangkai(asing)}. " +
                  "Tambahkan satuan itu lewat modal master Bahan Baku (Resep & BOM › Bahan Baku), " +
                  "atau ubah baris resepnya ke satuan yang tersedia.");
    }

    /// <summary>
    /// Hutang lahir per struk penerimaan, jadi turunnya juga harus bisa dijelaskan per struk.
    /// Satu struk yang beban retur plus pembayarannya melampaui nilai terimanya berarti 2-002
    /// pernah dikurang lebih dalam daripada yang pernah dikredit — saldo akun berubah menjadi
    /// piutang padahal labelnya utang. Pemeriksaan ini hanya menilai dokumen yang dibuat jalur
    /// retur/pembayaran, jadi basis data lama tanpa tabel baru tetap dilaporkan sehat.
    /// </summary>
    private static IntegrityCheck CekHutangPemasokTidakNegatif(SqliteConnection conn)
    {
        var bocor = Baris(conn, @"
            SELECT 'GR#' || g.GoodsReceiptId || ': terima ' || g.nilai || ', retur ' ||
                   COALESCE(r.v, 0) || ', bayar ' || COALESCE(p.v, 0)
              FROM (SELECT GoodsReceiptId,
                           SUM(CAST(ROUND(ReceivedQty * UnitCostMinorUnits) AS INTEGER)) AS nilai
                      FROM GoodsReceiptLines GROUP BY GoodsReceiptId) g
              LEFT JOIN (SELECT gl.GoodsReceiptId, SUM(l.ValueMinorUnits) AS v
                           FROM SupplierReturnLines l
                           JOIN GoodsReceiptLines gl ON gl.Id = l.GoodsReceiptLineId
                          GROUP BY gl.GoodsReceiptId) r ON r.GoodsReceiptId = g.GoodsReceiptId
              LEFT JOIN (SELECT GoodsReceiptId, SUM(Amount) AS v
                           FROM SupplierPayments GROUP BY GoodsReceiptId) p ON p.GoodsReceiptId = g.GoodsReceiptId
             WHERE COALESCE(r.v, 0) + COALESCE(p.v, 0) > g.nilai;");
        var saldo = Skalar(conn, @"
            SELECT COALESCE(SUM(jl.DebitAmount - jl.CreditAmount), 0)
              FROM JournalLines jl
              JOIN Accounts a ON a.Id = jl.AccountId
              JOIN JournalEntries je ON je.Id = jl.JournalEntryId
             WHERE a.Code = '2-002' AND je.Status IN ('Posted','Reversed');");
        var struk = Skalar(conn, "SELECT COUNT(DISTINCT GoodsReceiptId) FROM GoodsReceiptLines;");
        return baru("hutang pemasok per struk tidak pernah negatif", bocor.Count == 0,
            bocor.Count == 0
                ? $"{struk} struk penerimaan; retur plus pembayaran setiap struk tidak ada yang " +
                  $"melampaui nilai terimanya. Saldo 2-002 = {saldo} sen (kredit positif = masih utang)."
                : $"{bocor.Count} struk dibebani lebih besar daripada nilai barang yang diterima: " +
                  $"{Rangkai(bocor)}. Saldo 2-002 saat ini {saldo} sen. Retur atau pembayaran ditulis " +
                  "melewati jalur resmi (layar Retur Pemasok / Pembayaran Pemasok), bukan disisipkan " +
                  "langsung ke tabel SupplierReturnLines atau SupplierPayments.");
    }

    /// <summary>
    /// Retur dan pembayaran pemasok mengubah stok, hutang, dan kas sekaligus. Tanpa tautan jurnal,
    /// pergerakannya hanya ada di tabel dokumen sehingga neraca tidak berubah dan tidak ada satu pun
    /// baris buku besar yang bisa ditunjuk saat angka hutang dipertanyakan.
    /// </summary>
    private static IntegrityCheck CekDokumenPemasokBerjurnal(SqliteConnection conn)
    {
        var yatim = Baris(conn, @"
            SELECT 'RTB ' || ReturnNumber || ' tanpa jurnal'
              FROM SupplierReturns WHERE JournalEntryId IS NULL
            UNION ALL
            SELECT 'PPB ' || PaymentNumber || ' tanpa jurnal'
              FROM SupplierPayments WHERE JournalEntryId IS NULL
            UNION ALL
            SELECT 'RTB ' || r.ReturnNumber || ' menunjuk jurnal #' || r.JournalEntryId || ' yang hilang'
              FROM SupplierReturns r
             WHERE r.JournalEntryId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM JournalEntries je WHERE je.Id = r.JournalEntryId)
            UNION ALL
            SELECT 'PPB ' || p.PaymentNumber || ' menunjuk jurnal #' || p.JournalEntryId || ' yang hilang'
              FROM SupplierPayments p
             WHERE p.JournalEntryId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM JournalEntries je WHERE je.Id = p.JournalEntryId);");
        var total = Skalar(conn, @"
            SELECT (SELECT COUNT(*) FROM SupplierReturns) + (SELECT COUNT(*) FROM SupplierPayments);");
        return baru("retur dan pembayaran pemasok punya jurnal", yatim.Count == 0,
            yatim.Count == 0
                ? $"{total} dokumen retur/pembayaran pemasok, semuanya bertaut jurnal."
                : $"{yatim.Count} dari {total} dokumen tidak bisa ditelusuri ke buku besar: {Rangkai(yatim)}. " +
                  "Catat ulang dokumennya lewat layar; jangan menambal JournalEntryId dengan tangan.");
    }

    /// <summary>
    /// Tutup periode hanya berarti kalau gerbang tulisnya masih ada dan benar-benar menahan. Dua
    /// hal diuji sekaligus: pemicunya belum hilang (misalnya karena penurunan versi manual), dan
    /// tidak ada satu baris jurnal pun yang tercatat SETELAH periodenya ditutup. Jurnal yang sudah
    /// ada di dalam bulan tertutup sebelum penutupan adalah keadaan normal dan tidak dilaporkan
    /// di sini — yang dilarang adalah mengisinya lagi setelah kunci dipasang.
    /// </summary>
    private static IntegrityCheck CekPeriodeTertutupMengunci(SqliteConnection conn)
    {
        var pemicu = Skalar(conn, @"
            SELECT COUNT(*) FROM sqlite_master
             WHERE type = 'trigger' AND name IN (
                 'trg_journalentries_periode_insert', 'trg_journalentries_periode_update');");

        // Kedua cap waktu dinormalisasi ke "yyyy-MM-dd HH:mm:ss" dulu: kolom lama ada yang ditulis
        // datetime('now') (spasi, tanpa penanda zona) dan ada yang ToString("o") (T + 7 desimal),
        // jadi membandingkan teks mentah-nya akan selalu menyebut yang berformat ISO lebih baru.
        var bocor = Baris(conn, @"
            SELECT 'jurnal #' || je.Id || ' bertanggal ' || je.TransactionDate ||
                   ' ditulis ' || substr(replace(je.CreatedAt, 'T', ' '), 1, 19) ||
                   ' ke periode ' || fp.Name || ' yang ditutup ' ||
                   substr(replace(fp.ClosedAt, 'T', ' '), 1, 19)
              FROM JournalEntries je
              JOIN FiscalPeriods fp
                ON fp.Status IN ('Closed', 'Locked')
               AND fp.StartDate <= je.TransactionDate
               AND fp.EndDate >= je.TransactionDate
             WHERE fp.ClosedAt IS NOT NULL
               AND substr(replace(je.CreatedAt, 'T', ' '), 1, 19)
                   > substr(replace(fp.ClosedAt, 'T', ' '), 1, 19)
             ORDER BY je.Id DESC LIMIT 8;");

        var tertutup = Skalar(conn,
            "SELECT COUNT(*) FROM FiscalPeriods WHERE Status IN ('Closed','Locked');");
        var isi = Skalar(conn, @"
            SELECT COUNT(*) FROM JournalEntries je
              JOIN FiscalPeriods fp
                ON fp.Status IN ('Closed','Locked')
               AND fp.StartDate <= je.TransactionDate
               AND fp.EndDate >= je.TransactionDate;");

        var lolos = pemicu == "2" && bocor.Count == 0;
        return baru("periode tertutup mengunci penulis jurnal", lolos, lolos
            ? $"{tertutup} periode ditutup berisi {isi} kaki jurnal; pemicu pagar INSERT dan UPDATE " +
              "aktif, dan tidak ada jurnal yang ditulis setelah penutupan."
            : (pemicu != "2"
                ? $"pemicu pagar periode hanya {pemicu} dari 2 yang diharapkan — tutup periode saat ini " +
                  "tidak mengikat, jurnal mundur masih bisa ditulis. Bangun ulang skema (migrasi 022) " +
                  "sebelum memakai fitur ini."
                : $"{bocor.Count} jurnal masuk ke periode yang sudah lebih dulu ditutup: {Rangkai(bocor)}. " +
                  "Pagar ditulis lewat jalur yang memutar aplikasi; periksa dari mana angka ini datang.") +
              $" ({tertutup} periode tertutup, {isi} jurnal di dalamnya)");
    }

    private static IntegrityCheck baru(string name, bool passed, string detail)
        => new(name, passed, detail);

    private static int VersiTersedia()
    {
        var tertinggi = 0;
        foreach (var res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            if (!res.Contains("Migrations") || !res.EndsWith(".sql")) continue;
            var parts = res.Split('.');
            var angka = new string(parts[^2].TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(angka, out var v) && v > tertinggi) tertinggi = v;
        }
        return tertinggi;
    }

    private static string Skalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var nilai = cmd.ExecuteScalar();
        return nilai is null or DBNull ? "0" : Convert.ToString(nilai, System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static long Angka(SqliteConnection conn, string sql) =>
        long.Parse(Skalar(conn, sql), System.Globalization.CultureInfo.InvariantCulture);

    private static List<string> Baris(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var hasil = new List<string>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var sb = new StringBuilder();
            for (var i = 0; i < rdr.FieldCount; i++)
            {
                // Pustaka membaca kolom INTEGER sebagai long, jadi GetString bisa melempar
                // galat cast untuk PRAGMA yang mengembalikan baris/angka.
                if (i > 0) sb.Append(' ');
                sb.Append(rdr.IsDBNull(i) ? "-" : Convert.ToString(rdr.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
            }
            hasil.Add(sb.ToString());
        }
        return hasil;
    }

    private static string Rangkai(List<string> baris)
        => string.Join("; ", baris.Take(5)) + (baris.Count > 5 ? $" (+{baris.Count - 5} lainnya)" : "");

    private static string Ringkas(List<string> baris, string awalan)
        => awalan + string.Join("; ", baris.Take(5)) + (baris.Count > 5 ? $" (+{baris.Count - 5} lainnya)" : "");
}
