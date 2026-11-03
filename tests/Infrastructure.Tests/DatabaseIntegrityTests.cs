using FluentAssertions;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Periksa-diri hanya berguna kalau angkanya jujur: hijau pada basis data yang benar-benar sehat
/// dan merah pada kerusakan yang sengaja dibuat. Tanpa sisi merah, layar "Basis Data Sehat" bisa
/// tampil selamanya padahal jurnalnya sudah miring.
/// </summary>
public class DatabaseIntegrityTests
{
    private static IReadOnlyList<IntegrityCheck> Periksa(TempDatabase db) =>
        new DatabaseIntegrity(new DbConnectionFactory(db.ConnectionString)).Check();

    private static IntegrityCheck Ambil(IReadOnlyList<IntegrityCheck> hasil, string nama) =>
        hasil.Single(c => c.Name == nama);

    /// <summary>SPK butuh produk; seed migrasi tidak menyediakannya.</summary>
    private static void TambahProduk(TempDatabase db) =>
        db.Exec("INSERT INTO ProductTemplates (Id, Name, SellingPrice, IsActive) VALUES (1, 'Nastar', 1500000, 1);");

    [Fact]
    public void BasisDataHasilMigrasi_SemuaPemeriksaanLolos()
    {
        using var db = new TempDatabase();

        var hasil = Periksa(db);

        hasil.Should().NotBeEmpty();
        var gagal = hasil.Where(c => !c.Passed).Select(c => $"{c.Name}: {c.Detail}").ToArray();
        gagal.Should().BeEmpty("basis data baru dari migrasi tidak boleh dilaporkan rusak");
    }

    [Fact]
    public void JurnalTidakSeimbang_Tertangkap()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO JournalEntries (Description, TransactionDate, Status) " +
                "VALUES ('Sengaja miring', '2030-01-10', 'Posted');");
        var entryId = Convert.ToInt32(db.Scalar("SELECT last_insert_rowid();"));
        db.Exec($"INSERT INTO JournalLines (JournalEntryId, AccountId, DebitAmount, CreditAmount) " +
                $"VALUES ({entryId}, 1, 250000, 0);");

        var hasil = Periksa(db);

        var cek = Ambil(hasil, "jurnal posted seimbang");
        cek.Passed.Should().BeFalse();
        cek.Detail.Should().Contain(entryId.ToString()).And.Contain("250000");
    }

    [Fact]
    public void JurnalPostedTanpaBaris_Tertangkap()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO JournalEntries (Description, TransactionDate, Status) " +
                "VALUES ('Entri hantu', '2030-01-10', 'Posted');");

        Ambil(Periksa(db), "jurnal posted seimbang").Detail.Should().Contain("tanpa baris");
    }

    [Fact]
    public void JurnalMengarahKeDokumenYangHilang_Tertangkap()
    {
        using var db = new TempDatabase();
        // Kolom dokumen sumber sengaja tanpa kunci asing (jenisnya bebas), jadi kerusakan
        // jenis ini hanya bisa dilihat oleh pemeriksaan yang menanyakan tabel tujuannya.
        db.Exec("INSERT INTO JournalEntries (Description, TransactionDate, Status, SourceDocumentType, SourceDocumentId) " +
                "VALUES ('Piutang tanpa pesanan', '2030-01-10', 'Posted', 'SalesOrder', 424242);");

        var cek = Ambil(Periksa(db), "rujukan dokumen jurnal utuh");
        cek.Passed.Should().BeFalse();
        cek.Detail.Should().Contain("424242");
    }

    [Fact]
    public void NomorDokumenKembar_DitolakSkemaDanDilaporkanSehat()
    {
        using var db = new TempDatabase();
        TambahProduk(db);
        db.Exec("INSERT INTO WorkOrders (OrderNumber, ProductTemplateId, TargetQuantity, Status) " +
                "VALUES ('SPK-20300110-0001', 1, 5, 'Planned');");

        // Nomor kembar tidak bisa masuk (kolomnya UNIQUE), jadi pemeriksaan ini hijau di DB mana pun
        // yang masih punya indeksnya. Kalau suatu salinan kehilangan indeks, baris keduanya lolos dan
        // pemeriksaan ini yang menangkap.
        var act = () => db.Exec("INSERT INTO WorkOrders (OrderNumber, ProductTemplateId, TargetQuantity, Status) " +
                                "VALUES ('SPK-20300110-0001', 1, 5, 'Planned');");
        act.Should().Throw<Microsoft.Data.Sqlite.SqliteException>();

        var cek = Ambil(Periksa(db), "nomor dokumen tidak ada yang kembar");
        cek.Passed.Should().BeTrue();
        cek.Detail.Should().Contain("sekali");
    }

    [Fact]
    public void PenghitungDokumenTertinggalDariIsinya_Tertangkap()
    {
        using var db = new TempDatabase();
        TambahProduk(db);
        db.Exec("INSERT INTO DocumentSequences (Kind, DayKey, LastValue) VALUES ('WorkOrder', '2030-01-10', 1);");
        db.Exec("INSERT INTO WorkOrders (OrderNumber, ProductTemplateId, TargetQuantity, Status) " +
                "VALUES ('SPK-20300110-0001', 1, 5, 'Planned');");
        db.Exec("INSERT INTO WorkOrders (OrderNumber, ProductTemplateId, TargetQuantity, Status) " +
                "VALUES ('SPK-20300110-0002', 1, 5, 'Planned');");

        var cek = Ambil(Periksa(db), "penghitung dokumen menutupi isinya");
        cek.Passed.Should().BeFalse();
        cek.Detail.Should().Contain("penghitung 1").And.Contain("dokumen 2");
    }

    [Fact]
    public void MigrasiBelumSelesaiPenuh_Tertangkap()
    {
        using var db = new TempDatabase();
        var tertinggi = Convert.ToInt32(db.Scalar("SELECT MAX(Version) FROM SchemaVersion;"));
        // Tabel versi ditulis ulang seperti keadaan sebelum migrasi terakhir sempat jalan:
        // beginilah tampilannya basis data yang disalin dari aplikasi versi lama.
        db.Exec($"DELETE FROM SchemaVersion WHERE Version = {tertinggi};");

        var cek = Ambil(Periksa(db), "versi skema");
        cek.Passed.Should().BeFalse();
        cek.Detail.Should().Contain($"terpasang {tertinggi - 1}");
    }

    [Fact]
    public void BarisYatimTanpaKunciAsingAktif_Tertangkap()
    {
        using var db = new TempDatabase();
        // Kunci asing hanya ditegakkan pada koneksi yang menyalakan PRAGMA-nya. Koneksi tes
        // ini mematikannya untuk membuat baris rusak, seperti yang terjadi bila pabrikan
        // lupa menyalakan PRAGMA pada setiap koneksi.
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys=OFF; " +
                              "INSERT INTO LocationStock (LocationId, IngredientId, Quantity) VALUES (999, 999, 3);";
            cmd.ExecuteNonQuery();
        }

        var hasil = Periksa(db);
        Ambil(hasil, "PRAGMA foreign_key_check").Passed.Should().BeFalse();
        Ambil(hasil, "PRAGMA foreign_key_check").Detail.Should().Contain("LocationStock");
        // Connection yang dipakai pemeriksaan menyalakan PRAGMA-nya sendiri, jadi ini ikut terbukti.
        Ambil(hasil, "foreign_keys aktif di koneksi ini").Passed.Should().BeTrue();
    }

    [Fact]
    public void NilaiStokProdukJadiDanSaldoBukuBesarBerbeda_Tertangkap()
    {
        using var db = new TempDatabase();
        var persediaan = Convert.ToInt32(db.Scalar("SELECT Id FROM Accounts WHERE Code = '1-006';"));
        var kas = Convert.ToInt32(db.Scalar("SELECT Id FROM Accounts WHERE Code = '1-001';"));

        // Jurnal seimbang, jadi bukan pemeriksaan keseimbangan yang menangkapnya: hanya nilai
        // stok fisik (nol) yang tidak mendukung saldo persediaan produk jadi.
        db.Exec("INSERT INTO JournalEntries (Description, TransactionDate, Status) " +
                "VALUES ('Persediaan tanpa stok', '2030-01-10', 'Posted');");
        var entryId = Convert.ToInt32(db.Scalar("SELECT last_insert_rowid();"));
        db.Exec($"INSERT INTO JournalLines (JournalEntryId, AccountId, DebitAmount, CreditAmount) " +
                $"VALUES ({entryId}, {persediaan}, 500000, 0), ({entryId}, {kas}, 0, 500000);");

        var hasil = Periksa(db);
        Ambil(hasil, "jurnal posted seimbang").Passed.Should().BeTrue();

        var cek = Ambil(hasil, "nilai stok produk jadi sama dengan saldo 1-006");
        cek.Passed.Should().BeFalse();
        cek.Detail.Should().Contain("500000").And.Contain("selisih");
    }
}
