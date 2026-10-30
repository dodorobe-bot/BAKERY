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
               OR (je.SourceDocumentType = 'WorkOrder'  AND NOT EXISTS (SELECT 1 FROM WorkOrders w WHERE w.Id = je.SourceDocumentId)));");
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
                SELECT 'PO', PoNumber, COUNT(*) FROM PurchaseOrders GROUP BY PoNumber HAVING COUNT(*) > 1);");
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
                UNION ALL SELECT 'PurchaseOrder',  substr(PoNumber,     instr(PoNumber, '-') + 1, 8)     FROM PurchaseOrders),
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
