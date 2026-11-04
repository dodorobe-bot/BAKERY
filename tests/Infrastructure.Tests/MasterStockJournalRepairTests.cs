using FluentAssertions;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Stok yang diketik dari layar Bahan Baku sebelum jalur itu ikut membukukan jurnal masih ada di
/// ledger hari ini, tetapi neracanya tidak. Perintah pemeliharaan --rekonsiliasi-stok-bahan menutup
/// celah itu; tes ini memastikan yang ditutup memang celahnya — tidak lebih, tidak dobel.
///
/// Bentuk "warisan" ditiru dengan menulis StockMovements tanpa JournalEntries, persis seperti yang
/// ditinggalkan kode lama.
/// </summary>
public class MasterStockJournalRepairTests
{
    private const int Gudang = 1;
    private const int Pengguna = 1;

    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;
    private readonly LocationStockRepository _stock;

    public MasterStockJournalRepairTests()
    {
        _factory = new DbConnectionFactory(_db.ConnectionString);
        _stock = new LocationStockRepository(_factory);
    }

    private int Bahan(string nama, long hargaPerKg)
    {
        _db.Exec($"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 $"VALUES ('{nama}', 'kg', {hargaPerKg}, 0, 0, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT id FROM Ingredients WHERE Name = '{nama}';"));
    }

    /// <summary>Meniru jejak kode lama: mutasi ada, jurnal tidak.</summary>
    private void Warisan(int ingredientId, decimal qty)
    {
        _db.Exec($"INSERT INTO StockMovements (IngredientId, MovementType, Quantity, SourceType, SourceId, Notes) " +
                 $"VALUES ({ingredientId}, 'Restock', {qty}, 'IngredientMaster', NULL, 'stok awal warisan');");
        _db.Exec($"INSERT OR REPLACE INTO LocationStock (LocationId, IngredientId, Quantity, UpdatedAt) " +
                 $"VALUES ({Gudang}, {ingredientId}, {qty}, datetime('now'));");
    }

    private (long Debit, long Kredit) Baris(string kode)
    {
        var debit = _db.Scalar(
            $"SELECT COALESCE(SUM(jl.DebitAmount), 0) FROM JournalLines jl " +
            $"JOIN JournalEntries je ON je.Id = jl.JournalEntryId " +
            $"JOIN Accounts a ON a.Id = jl.AccountId " +
            $"WHERE a.Code = '{kode}' AND je.SourceDocumentType = 'IngredientMaster';");
        var kredit = _db.Scalar(
            $"SELECT COALESCE(SUM(jl.CreditAmount), 0) FROM JournalLines jl " +
            $"JOIN JournalEntries je ON je.Id = jl.JournalEntryId " +
            $"JOIN Accounts a ON a.Id = jl.AccountId " +
            $"WHERE a.Code = '{kode}' AND je.SourceDocumentType = 'IngredientMaster';");
        return (Convert.ToInt64(debit), Convert.ToInt64(kredit));
    }

    [Fact]
    public async Task CariGap_MenemukanStokWarisanBesertaNilainya()
    {
        var bahan = Bahan("TepungWarisan", 300_000);
        Warisan(bahan, 10m);

        var gap = await MasterStockJournalRepair.CariGapAsync(_factory);

        gap.Should().HaveCount(1);
        gap[0].IngredientId.Should().Be(bahan);
        gap[0].Quantity.Should().Be(10m);
        gap[0].AmountMinor.Should().Be(3_000_000, "10 kg kali Rp 3.000 per kg dalam unit minor");
    }

    [Fact]
    public async Task CariGap_TidakMenulisSatuBarisJurnalPun()
    {
        var bahan = Bahan("TepungKering", 300_000);
        Warisan(bahan, 4m);
        var sebelum = _db.Count("JournalEntries");

        await MasterStockJournalRepair.CariGapAsync(_factory);

        _db.Count("JournalEntries").Should().Be(sebelum, "mode baca tidak boleh menyentuh buku besar");
    }

    [Fact]
    public async Task Terapkan_MenulisStokAwalMelawanModalLaluPagarnyaHijau()
    {
        var bahan = Bahan("MentegaWarisan", 400_000);
        Warisan(bahan, 5m);

        var ditulis = await MasterStockJournalRepair.TerapkanAsync(_factory, Pengguna);

        ditulis.Should().Be(1);
        Baris("1-003").Should().Be((2_000_000L, 0L), "Dr Persediaan Bahan Baku sebesar nilai stok");
        Baris("3-001").Kredit.Should().Be(2_000_000, "Cr Modal — stok lama dianggap penyertaan pemilik");
        Baris("5-006").Should().Be((0L, 0L), "penambalan warisan bukan selisih opname");

        (await MasterStockJournalRepair.CariGapAsync(_factory)).Should().BeEmpty();
        new DatabaseIntegrity(_factory).Check()
            .Single(c => c.Name == "stok layar Bahan Baku punya jurnal").Passed
            .Should().BeTrue();
    }

    [Fact]
    public async Task Terapkan_DijalankanDuaKali_TidakMendobelJurnal()
    {
        var bahan = Bahan("GulaWarisan", 150_000);
        Warisan(bahan, 8m);

        await MasterStockJournalRepair.TerapkanAsync(_factory, Pengguna);
        var kedua = await MasterStockJournalRepair.TerapkanAsync(_factory, Pengguna);

        kedua.Should().Be(0);
        _db.Count("JournalEntries", "SourceDocumentType = 'IngredientMaster'").Should().Be(1);
    }

    [Fact]
    public async Task Terapkan_MelewatiBahanYangSudahDibukukanOlehJalurBaru()
    {
        var bahan = Bahan("SusuBaru", 250_000);
        await _stock.ApplyIngredientMasterStockAsync(Gudang, bahan, 6m, stokAwal: true, Pengguna, "Stok awal");
        var jurnalSebelum = _db.Count("JournalEntries", "SourceDocumentType = 'IngredientMaster'");

        var ditulis = await MasterStockJournalRepair.TerapkanAsync(_factory, Pengguna);

        ditulis.Should().Be(0, "stok yang sudah dibukukan saat diketik tidak boleh ditambal lagi");
        _db.Count("JournalEntries", "SourceDocumentType = 'IngredientMaster'").Should().Be(jurnalSebelum);
    }

    [Fact]
    public async Task Terapkan_MenolakSebelumMenulisBilaLokasiGudangTidakAda()
    {
        var bahan = Bahan("RagiWarisan", 2_000_000);
        Warisan(bahan, 1m);
        _db.Exec("DELETE FROM LocationStock;");
        _db.Exec("DELETE FROM StorageLocations WHERE Type = 'Warehouse';");

        var act = async () => await MasterStockJournalRepair.TerapkanAsync(_factory, Pengguna);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Warehouse*");
        _db.Count("JournalEntries", "SourceDocumentType = 'IngredientMaster'").Should().Be(0,
            "penolakan terjadi sebelum satu baris pun ditulis");
    }
}
