using FluentAssertions;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

public class StockLedgerTests
{
    private const int Warehouse = 1;
    private const int Kitchen = 2;

    private readonly TempDatabase _db = new();
    private readonly LocationStockRepository _stock;
    private readonly IngredientRepository _ingredients;

    public StockLedgerTests()
    {
        var factory = new DbConnectionFactory(_db.ConnectionString);
        _stock = new LocationStockRepository(factory);
        _ingredients = new IngredientRepository(factory);
    }

    private int NewIngredient(string name = "Tepung", decimal staleCache = 0)
    {
        _db.Exec($"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                 $"VALUES ('{name}', 'kg', 10000, {staleCache}, 0, 1);");
        return Convert.ToInt32(_db.Scalar("SELECT last_insert_rowid();"));
    }

    private static StockChange Restock(int location, int ingredient, decimal qty) =>
        new(location, ingredient, qty, MovementType.Restock, "Test", null, 1, null);

    [Fact]
    public async Task ApplyAsync_Restock_UpdatesBalanceAndWritesOneLedgerLeg()
    {
        var ing = NewIngredient();

        await _stock.ApplyAsync(new[] { Restock(Warehouse, ing, 10m) });

        var totalAfterRestock = await _stock.GetTotalStockAsync(ing);
        totalAfterRestock.Should().Be(10m);
        _db.Count("StockMovements", $"IngredientId = {ing}").Should().Be(1);
        _db.Count("StockMovements", $"Quantity = 10 AND LocationId = {Warehouse}").Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_Overdraft_ThrowsWithoutLeavingAnyTrace()
    {
        var ing = NewIngredient();
        await _stock.ApplyAsync(new[] { Restock(Warehouse, ing, 5m) });
        var legsBefore = _db.Count("StockMovements", $"IngredientId = {ing}");

        var act = () => _stock.ApplyAsync(new[]
        {
            new StockChange(Warehouse, ing, -6m, MovementType.Deduction, "Test", null, 1, null)
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Tepung").And.Contain("Gudang");
        var totalAfterFailedOverdraft = await _stock.GetTotalStockAsync(ing);
        totalAfterFailedOverdraft.Should().Be(5m);
        _db.Count("StockMovements", $"IngredientId = {ing}").Should().Be(legsBefore);
    }

    [Fact]
    public async Task ApplyAsync_LegAfterFailedLeg_RollsBackWholeBatch()
    {
        var flour = NewIngredient("Tepung A");
        var sugar = NewIngredient("Gula A");
        await _stock.ApplyAsync(new[] { Restock(Kitchen, flour, 4m), Restock(Kitchen, sugar, 1m) });

        var act = () => _stock.ApplyAsync(new[]
        {
            new StockChange(Kitchen, flour, -1m, MovementType.Deduction, "Test", null, 1, null),
            new StockChange(Kitchen, sugar, -99m, MovementType.Deduction, "Test", null, 1, null)
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        // Regresi: konsumsi pesanan pernah memakan bahan pertama lalu gagal di bahan kedua
        var flourTotal = await _stock.GetTotalStockAsync(flour);
        var sugarTotal = await _stock.GetTotalStockAsync(sugar);
        flourTotal.Should().Be(4m);
        sugarTotal.Should().Be(1m);
    }

    [Fact]
    public async Task TransferStockAsync_WritesTwoSignedLegsInOneTransaction()
    {
        var ing = NewIngredient();
        await _stock.ApplyAsync(new[] { Restock(Warehouse, ing, 10m) });

        await _stock.TransferStockAsync(Warehouse, Kitchen, ing, 4m, 1, "pindah dapur");

        var legs = _db.Scalar(
            $"SELECT COUNT(*) FROM StockMovements WHERE IngredientId = {ing} AND LocationId IS NOT NULL;");
        Convert.ToInt32(legs).Should().Be(3);
        _db.Count("StockMovements", $"IngredientId = {ing} AND Quantity = -4 AND LocationId = {Warehouse}")
            .Should().Be(1);
        _db.Count("StockMovements", $"IngredientId = {ing} AND Quantity = 4 AND LocationId = {Kitchen}")
            .Should().Be(1);
    }

    [Fact]
    public async Task GetMovementsAsync_MenampilkanTransferUntukMutasiGudangKeDapur()
    {
        var ing = NewIngredient();
        await _stock.ApplyAsync(new[] { Restock(Warehouse, ing, 10m) });

        await _stock.TransferStockAsync(Warehouse, Kitchen, ing, 4m, 1, "pindah dapur");

        var gerak = (await _stock.GetMovementsAsync(null, 10))
            .Where(m => m.IngredientId == ing && Math.Abs(m.Quantity) == 4m)
            .ToList();
        gerak.Should().HaveCount(2, "mutasi menulis satu kaki keluar dan satu kaki masuk");
        gerak.Should().OnlyContain(m => m.MovementType == "Transfer",
            "perpindahan barang tidak boleh terbaca sebagai koreksi manual");
        gerak.Should().OnlyContain(m => m.SourceType == "LocationTransfer");
    }

    [Fact]
    public async Task TransferStockAsync_SameLocation_Throws()
    {
        var ing = NewIngredient();

        var act = () => _stock.TransferStockAsync(Warehouse, Warehouse, ing, 1m, 1, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AdjustStockAsync_NegativeTarget_Throws()
    {
        var ing = NewIngredient();

        var act = () => _stock.AdjustStockAsync(Warehouse, ing, -1m, 1, "opname");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetLocationIdByTypeAsync_UnknownType_ThrowsInsteadOfMagicId()
    {
        var act = () => _stock.GetLocationIdByTypeAsync("Laboratorium");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Laboratorium");
    }

    [Fact]
    public async Task Ingredient_CurrentStock_IsDerivedFromLocationStockNotCacheColumn()
    {
        var ing = NewIngredient(staleCache: 999);
        await _stock.ApplyAsync(new[] { Restock(Warehouse, ing, 4m), Restock(Kitchen, ing, 2m) });

        var read = await _ingredients.GetByIdAsync(ing);

        read!.CurrentStock.Should().Be(6m);
        _db.Count("Ingredients", $"Id = {ing} AND CurrentStock = 999").Should().Be(1);
    }
}
