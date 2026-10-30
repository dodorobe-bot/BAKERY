using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Migrasi 014 menambah antrean cetak dan barcode produk. Yang diuji bukan hanya "tabelnya ada",
/// tetapi janji yang dipakai layar: barcode ganda ditolak walau beda huruf besar-kecil, produk
/// tanpa barcode tetap sah, dan rollback benar-benar mencabut semuanya.
/// </summary>
public class Migration014Tests
{
    private static void TambahProduk(TempDatabase db, int id, string nama, string? barcode)
    {
        var nilai = barcode is null ? "NULL" : $"'{barcode}'";
        db.Exec($"INSERT INTO ProductTemplates (Id, Name, SellingPrice, IsActive, Barcode) " +
                $"VALUES ({id}, '{nama}', 1000000, 1, {nilai});");
    }

    [Fact]
    public void Migrasi014_MenyediakanTabelAntreanDanKolomBarcode()
    {
        using var db = new TempDatabase();

        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PrintJobs';"))
            .Should().Be(1);
        db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ProductTemplates') WHERE name='Barcode';")
            .Should().Be(1L);
    }

    [Fact]
    public void Migrasi014_MengisiPengaturanPerangkatBawaan()
    {
        using var db = new TempDatabase();

        db.Scalar("SELECT Value FROM AppSettings WHERE Key='device.paper.width_mm';").Should().Be("80");
        db.Scalar("SELECT Value FROM AppSettings WHERE Key='device.printer.mode';").Should().Be("windows-driver");
        db.Scalar("SELECT Value FROM AppSettings WHERE Key='device.printer.auto_print';").Should().Be("0");
        db.Scalar("SELECT Value FROM AppSettings WHERE Key='device.scanner.suffix';").Should().Be("enter");
        db.Scalar("SELECT Value FROM AppSettings WHERE Key='device.printer.name';").Should().Be(string.Empty);
    }

    [Fact]
    public void BarcodeGanda_DitolakBasisData()
    {
        using var db = new TempDatabase();
        TambahProduk(db, 1, "Nastar", "8991234567890");

        var tindakan = () => TambahProduk(db, 2, "Bolu", "8991234567890");

        tindakan.Should().Throw<SqliteException>()
            .Which.SqliteErrorCode.Should().Be(19, "indeks unik parsial menolak barcode yang sama");
        db.Count("ProductTemplates").Should().Be(1);
    }

    [Fact]
    public void BarcodeGandaBedaHurufBesarKecil_Ditolak()
    {
        using var db = new TempDatabase();
        TambahProduk(db, 1, "Nastar", "abc-01");

        var tindakan = () => TambahProduk(db, 2, "Bolu", "ABC-01");

        tindakan.Should().Throw<SqliteException>("barcode adalah kode fisik; beda huruf bukan produk berbeda");
    }

    [Fact]
    public void ProdukTanpaBarcode_BolehBanyak()
    {
        using var db = new TempDatabase();
        TambahProduk(db, 1, "Nastar", null);
        TambahProduk(db, 2, "Bolu", null);
        TambahProduk(db, 3, "Kue Kering", "");

        db.Count("ProductTemplates").Should().Be(3);
    }

    [Fact]
    public void PrintJobs_MenolakStatusDanLebarDiLuarDaftar()
    {
        using var db = new TempDatabase();

        var statusSalah = () => db.Exec(
            "INSERT INTO PrintJobs (Kind, PaperWidthMm, Status, Payload, CreatedAt) " +
            "VALUES ('Receipt', 80, 'Selesai', 'x', '2026-10-28T00:00:00Z');");
        var lebarSalah = () => db.Exec(
            "INSERT INTO PrintJobs (Kind, PaperWidthMm, Status, Payload, CreatedAt) " +
            "VALUES ('Receipt', 57, 'Pending', 'x', '2026-10-28T00:00:00Z');");

        statusSalah.Should().Throw<SqliteException>();
        lebarSalah.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Rollback014_MencabutAntreanBarcodeDanPengaturan()
    {
        using var db = new TempDatabase();
        TambahProduk(db, 1, "Nastar", "8991234567890");

        MigrationRollback.Run(db, "014_PosDevicesAndBarcode.down.sql");

        Convert.ToInt32(db.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PrintJobs';"))
            .Should().Be(0);
        db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ProductTemplates') WHERE name='Barcode';")
            .Should().Be(0L);
        db.Scalar("SELECT COUNT(*) FROM AppSettings WHERE Key LIKE 'device.%';").Should().Be(0L);
        db.Scalar("SELECT COUNT(*) FROM SchemaVersion WHERE Version = 14;").Should().Be(0L);
        db.Scalar("SELECT Name FROM ProductTemplates WHERE Id = 1;").Should().Be("Nastar",
            "rollback tidak boleh menghapus produk, hanya kolom barcode");
    }
}
