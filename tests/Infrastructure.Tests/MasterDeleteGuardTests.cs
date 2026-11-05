using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// "Hapus" di aplikasi ini bersifat lembut: baris master dibiarkan ada dengan IsActive = 0 dan
/// stoknya tidak ikut hilang. Selama stok itu masih ada, nilainya tetap menjadi aset di jurnal
/// persediaan sementara masternya sudah lenyap dari daftar bahan dan katalog produk — tiga angka
/// yang tidak lagi sama antara layar Inventaris, laporan stok, dan neraca. Pagar di repository
/// menjaga keadaan itu tidak tercipta lagi; pemeriksaan basis data menangkap sisa yang sudah ada
/// sebelum pagar terpasang.
/// </summary>
public class MasterDeleteGuardTests : IDisposable
{
    private const int Gudang = 1;
    private const int Etalase = 3;
    private const int Pengguna = 1;

    private readonly TempDatabase _db = new();
    private readonly DbConnectionFactory _factory;
    private readonly IngredientRepository _ingredients;
    private readonly ProductTemplateRepository _products;
    private readonly LocationStockRepository _stock;
    private readonly DatabaseIntegrity _integrity;

    public MasterDeleteGuardTests()
    {
        _factory = new DbConnectionFactory(_db.ConnectionString);
        _ingredients = new IngredientRepository(_factory);
        _products = new ProductTemplateRepository(_factory);
        _stock = new LocationStockRepository(_factory);
        _integrity = new DatabaseIntegrity(_factory);
    }

    private int BahanBaru(string nama)
    {
        _db.Exec($"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 $"VALUES ('{nama}', 'kg', 100000, 0, 0, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM Ingredients WHERE Name = '{nama}';"));
    }

    private int ProdukBaru(string nama)
    {
        _db.Exec($"INSERT INTO ProductTemplates (Name, Category, SellingPrice, IsActive) " +
                 $"VALUES ('{nama}', 'Roti', 1500000, 1);");
        return Convert.ToInt32(_db.Scalar($"SELECT Id FROM ProductTemplates WHERE Name = '{nama}';"));
    }

    private Task IsiStokGudangAsync(int bahan, decimal jumlah) =>
        _stock.ApplyAsync(new[]
        {
            new StockChange(Gudang, bahan, jumlah, MovementType.Restock, "UjiPagar", null, Pengguna, null)
        });

    private void IsiStokProdukAsync(int produk, decimal jumlah) =>
        _db.Exec($"INSERT INTO FinishedGoodsStock (LocationId, ProductTemplateId, Quantity, AverageCostMinorUnits) " +
                 $"VALUES ({Etalase}, {produk}, {jumlah}, 250000);");

    private bool Aktif(int bahan) =>
        Convert.ToInt32(_db.Scalar($"SELECT IsActive FROM Ingredients WHERE Id = {bahan};")) == 1;

    private IntegrityCheck Pemeriksaan(string nama)
    {
        var cek = _integrity.Check().SingleOrDefault(c => c.Name == nama);
        cek.Should().NotBeNull($"pemeriksaan '{nama}' harus terdaftar di Periksa Basis Data");
        return cek!;
    }

    [Fact]
    public async Task BahanBerStok_DitolakDinonaktifkan()
    {
        var bahan = BahanBaru("Tepung Penjaga");
        await IsiStokGudangAsync(bahan, 5m);

        await _ingredients.Invoking(r => r.DeleteAsync(bahan)).Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Tepung Penjaga*Gudang Utama Bahan Baku*5*");

        Aktif(bahan).Should().BeTrue("penolakan tidak boleh mengubah master");
        var stok = await _stock.GetStockByLocationAsync(Gudang);
        stok.Single(s => s.IngredientId == bahan).Quantity.Should().Be(5m,
            "penolakan tidak boleh mengubah stok");
    }

    [Fact]
    public async Task BahanTanpaStok_BisaDinonaktifkan()
    {
        var bahan = BahanBaru("Gula Penjaga");
        await _ingredients.DeleteAsync(bahan);
        Aktif(bahan).Should().BeFalse();
    }

    [Fact]
    public async Task SetelahStokHabis_BahanBolehDinonaktifkan()
    {
        var bahan = BahanBaru("Mentega Penjaga");
        await IsiStokGudangAsync(bahan, 5m);
        await _ingredients.Invoking(r => r.DeleteAsync(bahan)).Should()
            .ThrowAsync<InvalidOperationException>();

        await _stock.AdjustStockAsync(Gudang, bahan, 0m, Pengguna, "Sisa dihabiskan");
        await _ingredients.DeleteAsync(bahan);

        Aktif(bahan).Should().BeFalse("setelah raknya kosong, bahan boleh dinonaktifkan");
    }

    [Fact]
    public async Task ProdukBerStokJadi_DitolakDinonaktifkan()
    {
        var produk = ProdukBaru("Roti Penjaga");
        IsiStokProdukAsync(produk, 4m);

        await _products.Invoking(r => r.DeleteAsync(produk)).Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Roti Penjaga*Etalase Toko & Kasir*4*");

        Convert.ToInt32(_db.Scalar($"SELECT IsActive FROM ProductTemplates WHERE Id = {produk};"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ProdukTanpaStokJadi_BisaDinonaktifkan()
    {
        var produk = ProdukBaru("Bolu Penjaga");
        await _products.DeleteAsync(produk);
        Convert.ToInt32(_db.Scalar($"SELECT IsActive FROM ProductTemplates WHERE Id = {produk};"))
            .Should().Be(0);
    }

    [Fact]
    public async Task LaporanStok_MemuatSisaStokMasterNonaktif()
    {
        // Keadaan lama: master sudah nonaktif dan stoknya masih ada (dibuat sebelum pagar).
        var bahan = BahanBaru("Tepung Warisan");
        await _ingredients.DeleteAsync(bahan);
        await IsiStokGudangAsync(bahan, 3m);
        var aktif = BahanBaru("Gula Aktif");
        await IsiStokGudangAsync(aktif, 2m);

        var dihitung = await _ingredients.GetAllCountedAsync();

        dihitung.Select(b => b.Name).Should().Contain(["Tepung Warisan", "Gula Aktif"],
            "laporan stok harus memuat semua barang yang masih dihitung di neraca, "
            + "bukan hanya yang masternya masih aktif");
        dihitung.Single(b => b.Name == "Tepung Warisan").IsActive.Should().BeFalse();
        (await _ingredients.GetAllActiveAsync()).Select(b => b.Name).Should().NotContain("Tepung Warisan");
    }

    [Fact]
    public async Task PeriksaBasisData_NamakanStokYangMenggantungDiMasterNonaktif()
    {
        Pemeriksaan("stok master nonaktif sudah habis").Passed.Should().BeTrue(
            "basis data baru belum punya stok yang menggantung");

        var bahan = BahanBaru("Tepung Gantung");
        await _ingredients.DeleteAsync(bahan);
        await IsiStokGudangAsync(bahan, 7m);
        var produk = ProdukBaru("Roti Gantung");
        await _products.DeleteAsync(produk);
        IsiStokProdukAsync(produk, 2m);

        var menggantung = Pemeriksaan("stok master nonaktif sudah habis");
        menggantung.Passed.Should().BeFalse(
            "stok di master nonaktif masih dihitung di akun persediaan, jadi keadaan ini wajib terlihat");
        menggantung.Detail.Should().Contain("Tepung Gantung").And.Contain("Roti Gantung");

        await _stock.AdjustStockAsync(Gudang, bahan, 0m, Pengguna, "Sisa dihabiskan");
        _db.Exec($"DELETE FROM FinishedGoodsStock WHERE ProductTemplateId = {produk};");
        Pemeriksaan("stok master nonaktif sudah habis").Passed.Should().BeTrue(
            "setelah stoknya habis, pemeriksaan kembali hijau");
    }

    [Fact]
    public async Task LaporanStokEkspor_MenyebutSisaStokMasterNonaktifDenganLabel()
    {
        // Keadaan lama: master sudah nonaktif, sisa stoknya masih dihitung di neraca.
        var warisan = BahanBaru("Tepung Laporan");
        await _ingredients.DeleteAsync(warisan);
        await IsiStokGudangAsync(warisan, 3m);
        await IsiStokGudangAsync(BahanBaru("Gula Laporan"), 2m);

        var ekspor = new ExportService(
            new SalesOrderRepository(_factory), new JournalRepository(_factory),
            _ingredients, new PurchaseOrderRepository(_factory), new SettingRepository(_factory));

        var (data, _, _) = await ekspor.ExportStockAsync("xlsx");
        using var buku = new ClosedXML.Excel.XLWorkbook(new MemoryStream(data));
        var isi = buku.Worksheets.SelectMany(ws => ws.CellsUsed()).Select(c => c.GetString()).ToList();

        isi.Should().Contain("Tepung Laporan",
            "stok yang masih menjadi aset harus ikut di laporan, sama seperti di layar Inventaris");
        isi.Should().Contain("NONAKTIF",
            "barisnya wajib menyebut bahwa masternya sudah dinonaktifkan, bukan menyamar jadi bahan normal");
        isi.Should().Contain("Gula Laporan");
    }

    public void Dispose() => _db.Dispose();
}
