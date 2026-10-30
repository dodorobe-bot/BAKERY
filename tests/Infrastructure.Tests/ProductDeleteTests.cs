using FluentAssertions;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Menonaktifkan produk harus benar-benar tertulis. Dulu endpoint-nya mengirim 200 sementara
/// barisnya tidak berubah, jadi produk yang sudah "dihapus" tetap muncul di daftar keesokan hari.
/// </summary>
public class ProductDeleteTests
{
    private static IProductTemplateRepository Repo(TempDatabase db) =>
        new ProductTemplateRepository(new DbConnectionFactory(db.ConnectionString));

    private static ProductTemplate Produk(string nama) => new()
    {
        Name = nama,
        Category = "Kue",
        SellingPrice = 1_500_000,
        IsActive = true,
    };

    [Fact]
    public async Task Delete_MenuNonaktifkanProdukDanProdukHilangDariDaftarAktif()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        var id = await repo.CreateAsync(Produk("Nastar Lebaran"), new List<RecipeLine>());

        await repo.DeleteAsync(id);

        (await repo.GetAllActiveAsync()).Select(p => p.Id).Should().NotContain(id);
        // Barisnya tetap ada sebagai rujukan pesanan lama; hanya flag-nya yang berubah.
        Convert.ToInt32(db.Scalar(
            $"SELECT IsActive FROM ProductTemplates WHERE Id = {id};")).Should().Be(0);
    }

    [Fact]
    public async Task Update_TidakMengubahStatusAktifYangSudahDisimpan()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        var id = await repo.CreateAsync(Produk("Bolak-Balik"), new List<RecipeLine>());
        await repo.DeleteAsync(id);

        // Ini jalur yang berbahaya: form yang masih terbuka di layar mengirim objek lengkap
        // setelah produk dinonaktifkan di tab lain. Update tidak boleh menyalakan IsActive kembali.
        var basi = await repo.GetAllActiveAsync();
        basi.Should().NotContain(p => p.Id == id, "produk nonaktif sudah hilang dari daftar");

        var dariForm = Produk("Bolak-Balik (diedit setelah dinonaktifkan)");
        dariForm.Id = id;
        await repo.UpdateAsync(dariForm, new List<RecipeLine>());

        Convert.ToInt32(db.Scalar($"SELECT IsActive FROM ProductTemplates WHERE Id = {id};"))
            .Should().Be(0, "menyimpan perubahan nama tidak boleh menghidupkan kembali produk yang sudah dinonaktifkan");
        (await repo.GetByIdWithRecipesAsync(id)).Should().BeNull(
            "jalur baca tetap menolak produk nonaktif sehingga layar tidak menampilkan tombol edit untuknya");
    }

    [Fact]
    public async Task Update_MenggantiBarisResepTanpaMenyisakanDuplikat()
    {
        using var db = new TempDatabase();
        db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                "VALUES ('Tepung', 'kg', 1200000, 0, 0, 1), ('Gula', 'kg', 1500000, 0, 0, 1);");
        var tepung = Convert.ToInt32(db.Scalar("SELECT Id FROM Ingredients WHERE Name='Tepung';"));
        var gula = Convert.ToInt32(db.Scalar("SELECT Id FROM Ingredients WHERE Name='Gula';"));

        var repo = Repo(db);
        var id = await repo.CreateAsync(Produk("Roti Manis"), new[]
        {
            new RecipeLine { IngredientId = tepung, Quantity = 0.5m },
        });

        await repo.UpdateAsync((await repo.GetByIdWithRecipesAsync(id))!, new[]
        {
            new RecipeLine { IngredientId = tepung, Quantity = 0.7m },
            new RecipeLine { IngredientId = gula, Quantity = 0.1m },
        });

        var setelah = await repo.GetByIdWithRecipesAsync(id);
        setelah!.RecipeLines.Should().HaveCount(2, "revisi resep mengganti komposisi, bukan menambah salinannya");
        setelah.RecipeLines.Single(l => l.IngredientId == tepung).Quantity.Should().Be(0.7m);
        Convert.ToInt32(db.Scalar($"SELECT COUNT(*) FROM RecipeLines WHERE ProductTemplateId = {id};"))
            .Should().Be(2);
    }
}
