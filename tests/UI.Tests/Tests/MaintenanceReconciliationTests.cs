using FluentAssertions;
using Microsoft.Data.Sqlite;
using Seruni22Bakery.Host;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace BakeryDesktop.UI.Tests;

/// <summary>
/// <c>--rekonsiliasi-stok-bahan</c> harus dikenali sebagai perintah pemeliharaan: kalau saklar ini
/// tidak diklaim, host web tetap menyala dan pemilik mengira ia sudah menambal data padahal yang
/// berjalan adalah aplikasi penuh. Mode baca saja tidak boleh menulis apa pun, karena angka yang
/// akan dibukukan masuk ke neraca (Debet 1-003 / Kredit 3-001 Modal).
///
/// Jalur tulis (--terapkan) dibuktikan di MasterStockJournalRepairTests pada basis data sementara;
/// di sini ia sengaja tidak dijalankan karena gerbang "instance lain berjalan" bergantung pada
/// apakah aplikasi milik pemilik sedang hidup di mesin tes.
/// </summary>
public class MaintenanceReconciliationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _dataRoot;

    public MaintenanceReconciliationTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"seruni-rec-{Guid.NewGuid():N}");
        _dbPath = Path.Combine(_dataRoot, "uji.db");
        Directory.CreateDirectory(_dataRoot);
        new DatabaseMigrator($"Data Source={_dbPath};").Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dataRoot, recursive: true); }
        catch { /* direktori sementara Windows dibersihkan sendiri */ }
    }

    private void Exec(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Jejak kode lama: stok layar Bahan Baku tertulis di ledger, tanpa jurnal.</summary>
    private void StokWarisan(long qty)
    {
        Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
             "VALUES ('TepungUjiKli', 'kg', 300000, 0, 0, 1);");
        var id = Scalar("SELECT Id FROM Ingredients WHERE Name = 'TepungUjiKli';");
        Exec($"INSERT INTO StockMovements (IngredientId, MovementType, Quantity, SourceType, SourceId, Notes) " +
             $"VALUES ({id}, 'Restock', {qty}, 'IngredientMaster', NULL, 'stok awal warisan');");
        Exec("INSERT OR REPLACE INTO LocationStock (LocationId, IngredientId, Quantity, UpdatedAt) " +
             $"SELECT Id, {id}, {qty}, datetime('now') FROM StorageLocations " +
             "WHERE Type = 'Warehouse' ORDER BY Id LIMIT 1;");
    }

    [Fact]
    public void SaklarDikenali_DanModeBacaTidakMenulisSatuBarisPun()
    {
        StokWarisan(10);
        var jurnalSebelum = Scalar("SELECT COUNT(*) FROM JournalEntries;");

        var diklaim = MaintenanceCommands.TryRun(
            new[] { "--rekonsiliasi-stok-bahan" }, _dbPath, _dataRoot);

        diklaim.Should().BeTrue("host web tidak boleh menyala pada perintah pemeliharaan");
        Scalar("SELECT COUNT(*) FROM JournalEntries;").Should().Be(jurnalSebelum,
            "tanpa --terapkan perintah ini hanya menampilkan angka untuk diperiksa pemilik");
        Scalar("SELECT COUNT(*) FROM JournalEntries WHERE SourceDocumentType = 'IngredientMaster';")
            .Should().Be(0);
        Directory.Exists(Path.Combine(_dataRoot, "backups")).Should().BeFalse(
            "cadangan pra-rekonsiliasi hanya dibuat saat akan menulis");
    }

    [Fact]
    public void TanpaSaklarPemeliharaan_TryRunMenyerahkanKembaliKeHostWeb()
    {
        MaintenanceCommands.TryRun(new[] { "--urls", "http://127.0.0.1:5299" }, _dbPath, _dataRoot)
            .Should().BeFalse("start normal tidak boleh tertahan oleh pemeriksaan ini");
    }

    [Fact]
    public void BasisDataSehat_SaklarTetapDiklaimDanTidakAdaYangDitulis()
    {
        var diklaim = MaintenanceCommands.TryRun(
            new[] { "--rekonsiliasi-stok-bahan", "--terapkan" }, _dbPath, _dataRoot);

        diklaim.Should().BeTrue();
        Scalar("SELECT COUNT(*) FROM JournalEntries;").Should().Be(0,
            "stok yang sudah berjurnal tidak boleh ditambal ulang");
    }
}
