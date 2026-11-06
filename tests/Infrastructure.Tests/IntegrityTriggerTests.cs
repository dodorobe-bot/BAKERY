using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Pemicu integritas v008 diuji pada level database: API boleh berubah, tapi nilai
/// uang/kuantitas yang mustahil tidak boleh masuk ke tabel.
/// </summary>
public class IntegrityTriggerTests
{
    private static int InsertIngredient(TempDatabase db, string name = "Tepung", long price = 1200000) =>
        Convert.ToInt32(ScalarWithInsert(db,
            $"INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
            $"VALUES ('{name}', 'kg', {price}, 0, 0, 1);"));

    private static int InsertOrder(TempDatabase db, long total, long dp = 0) =>
        Convert.ToInt32(ScalarWithInsert(db,
            "INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, DownPayment, Status) " +
            $"VALUES ('ORD-T{Guid.NewGuid():N}', 'Ratna', 'Arisan', '2030-01-10', {total}, {dp}, 'Pending');"));

    private static object? ScalarWithInsert(TempDatabase db, string sql)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        // last_insert_rowid() harus dibaca pada koneksi yang sama dengan INSERT-nya.
        cmd.CommandText = sql + "\nSELECT last_insert_rowid();";
        return cmd.ExecuteScalar();
    }

    private static void ShouldBeRejected(TempDatabase db, string sql)
    {
        var act = () => db.Exec(sql);
        act.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Pesanan_Bernol_Nol_Ditolak()
    {
        using var db = new TempDatabase();
        ShouldBeRejected(db, "INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, DownPayment) " +
                "VALUES ('ORD-ZERO', 'Ratna', 'Arisan', '2030-01-10', 0, 0);");
    }

    [Fact]
    public void Uang_Muka_Melebihi_Nilai_Pesanan_Ditolak()
    {
        using var db = new TempDatabase();
        ShouldBeRejected(db, "INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, DownPayment) " +
                "VALUES ('ORD-DP', 'Ratna', 'Arisan', '2030-01-10', 1000000, 1500000);");
    }

    [Fact]
    public void Pembayaran_Melebihi_Sisa_Bayar_Ditolak_Tepat_Sisa_Diterima()
    {
        using var db = new TempDatabase();
        var orderId = InsertOrder(db, total: 1000000, dp: 300000);

        ShouldBeRejected(db, $"INSERT INTO SalesPayments (SalesOrderId, Amount, PaymentDate, Method) " +
                $"VALUES ({orderId}, 800000, '2030-01-10', 'Cash');");

        var exact = () => db.Exec(
            $"INSERT INTO SalesPayments (SalesOrderId, Amount, PaymentDate, Method) " +
            $"VALUES ({orderId}, 700000, '2030-01-10', 'Cash');");
        exact.Should().NotThrow();

        ShouldBeRejected(db, $"INSERT INTO SalesPayments (SalesOrderId, Amount, PaymentDate, Method) " +
                $"VALUES ({orderId}, 1, '2030-01-10', 'Cash');");
    }

    [Fact]
    public void Pembayaran_Bernominal_Nol_Ditolak()
    {
        using var db = new TempDatabase();
        var orderId = InsertOrder(db, total: 1000000);
        ShouldBeRejected(db, $"INSERT INTO SalesPayments (SalesOrderId, Amount, PaymentDate, Method) " +
                $"VALUES ({orderId}, 0, '2030-01-10', 'Cash');");
    }

    [Fact]
    public void Bahan_Dengan_Harga_Negatif_Ditolak()
    {
        using var db = new TempDatabase();
        ShouldBeRejected(db, "INSERT INTO Ingredients (Name, Unit, PricePerUnit, CurrentStock, MinStockAlert, IsActive) " +
                "VALUES ('Mentega', 'kg', -1, 0, 0, 1);");
        var ingredientId = InsertIngredient(db);
        ShouldBeRejected(db, $"UPDATE Ingredients SET MinStockAlert = -5 WHERE Id = {ingredientId};");
    }

    [Fact]
    public void Resep_Dengan_Jumlah_Nol_Ditolak()
    {
        using var db = new TempDatabase();
        var ingredientId = InsertIngredient(db);
        var productId = Convert.ToInt32(ScalarWithInsert(db,
            "INSERT INTO ProductTemplates (Name, SellingPrice, IsActive) VALUES ('Roti', 2500000, 1);"));
        ShouldBeRejected(db, $"INSERT INTO RecipeLines (ProductTemplateId, IngredientId, Quantity) VALUES ({productId}, {ingredientId}, 0);");
    }

    [Fact]
    public void Penerimaan_Barang_Melebihi_Pesanan_Ditolak()
    {
        using var db = new TempDatabase();
        var ingredientId = InsertIngredient(db);
        var supplierId = Convert.ToInt32(ScalarWithInsert(db,
            "INSERT INTO Suppliers (Name) VALUES ('Boga');"));
        var poId = Convert.ToInt32(ScalarWithInsert(db,
            $"INSERT INTO PurchaseOrders (PoNumber, SupplierId, Status) VALUES ('PO-{Guid.NewGuid():N}', {supplierId}, 'Sent');"));
        var lineId = Convert.ToInt32(ScalarWithInsert(db,
            $"INSERT INTO PurchaseOrderLines (PurchaseOrderId, IngredientId, OrderedQty, ReceivedQty, UnitCostMinorUnits, Unit) " +
            $"VALUES ({poId}, {ingredientId}, 10, 0, 1200000, 'kg');"));

        ShouldBeRejected(db, $"UPDATE PurchaseOrderLines SET ReceivedQty = 12.5 WHERE Id = {lineId};");

        var withinOrder = () => db.Exec($"UPDATE PurchaseOrderLines SET ReceivedQty = 10 WHERE Id = {lineId};");
        withinOrder.Should().NotThrow();
    }

    [Fact]
    public void Baris_Penerimaan_Barang_Kosong_Ditolak()
    {
        using var db = new TempDatabase();
        var ingredientId = InsertIngredient(db);
        var supplierId = Convert.ToInt32(ScalarWithInsert(db, "INSERT INTO Suppliers (Name) VALUES ('Boga');"));
        var poId = Convert.ToInt32(ScalarWithInsert(db,
            $"INSERT INTO PurchaseOrders (PoNumber, SupplierId, Status) VALUES ('PO-{Guid.NewGuid():N}', {supplierId}, 'Sent');"));
        var lineId = Convert.ToInt32(ScalarWithInsert(db,
            $"INSERT INTO PurchaseOrderLines (PurchaseOrderId, IngredientId, OrderedQty, ReceivedQty, UnitCostMinorUnits, Unit) " +
            $"VALUES ({poId}, {ingredientId}, 10, 0, 1200000, 'kg');"));
        var receiptId = Convert.ToInt32(ScalarWithInsert(db,
            $"INSERT INTO GoodsReceipts (PurchaseOrderId, ReceivedDate) VALUES ({poId}, '2030-01-10');"));

        ShouldBeRejected(db, $"INSERT INTO GoodsReceiptLines (GoodsReceiptId, PurchaseOrderLineId, IngredientId, ReceivedQty, UnitCostMinorUnits, LocationId) " +
                $"VALUES ({receiptId}, {lineId}, {ingredientId}, 0, 1200000, 1);");
    }

    [Fact]
    public void Spk_Dengan_Target_Nol_Ditolak()
    {
        using var db = new TempDatabase();
        var productId = Convert.ToInt32(ScalarWithInsert(db,
            "INSERT INTO ProductTemplates (Name, SellingPrice, IsActive) VALUES ('Roti', 2500000, 1);"));
        ShouldBeRejected(db, $"INSERT INTO WorkOrders (OrderNumber, ProductTemplateId, TargetQuantity, LaborCost, OverheadCost) " +
                $"VALUES ('SPK-ZERO', {productId}, 0, 0, 0);");
    }

    private static int InsertUnit(TempDatabase db, int ingredientId, string unit, decimal factor) =>
        Convert.ToInt32(ScalarWithInsert(db,
            "INSERT INTO IngredientUnits (IngredientId, Unit, FactorToBase) " +
            $"VALUES ({ingredientId}, '{unit}', {factor.ToString(System.Globalization.CultureInfo.InvariantCulture)});"));

    private static int InsertProduct(TempDatabase db, string name = "Roti") =>
        Convert.ToInt32(ScalarWithInsert(db,
            $"INSERT INTO ProductTemplates (Name, SellingPrice, IsActive) VALUES ('{name}', 2500000, 1);"));

    [Fact]
    public void Satuan_Tambahan_Tidak_Sah_Ditolak()
    {
        using var db = new TempDatabase();
        var ingredientId = InsertIngredient(db);

        // Faktor nol/negatif membuat konversi resep menjadi nol atau terbalik.
        ShouldBeRejected(db, $"INSERT INTO IngredientUnits (IngredientId, Unit, FactorToBase) VALUES ({ingredientId}, 'g', 0);");
        ShouldBeRejected(db, $"INSERT INTO IngredientUnits (IngredientId, Unit, FactorToBase) VALUES ({ingredientId}, 'g', -1);");
        // Satuan yang sama dengan satuan stok membuat faktor mana yang dipakai tidak jelas.
        ShouldBeRejected(db, $"INSERT INTO IngredientUnits (IngredientId, Unit, FactorToBase) VALUES ({ingredientId}, 'KG', 1000);");

        InsertUnit(db, ingredientId, "g", 0.001m);
        // "g" dan "G" adalah satuan yang sama.
        ShouldBeRejected(db, $"INSERT INTO IngredientUnits (IngredientId, Unit, FactorToBase) VALUES ({ingredientId}, 'G', 0.001);");
    }

    [Fact]
    public void Baris_Resep_Bersatuan_Tak_Terdaftar_Ditolak()
    {
        using var db = new TempDatabase();
        var ingredientId = InsertIngredient(db);
        var productId = InsertProduct(db);

        ShouldBeRejected(db, "INSERT INTO RecipeLines (ProductTemplateId, IngredientId, Quantity, Unit) " +
                $"VALUES ({productId}, {ingredientId}, 250, 'g');");

        InsertUnit(db, ingredientId, "g", 0.001m);
        var valid = () => db.Exec(
            "INSERT INTO RecipeLines (ProductTemplateId, IngredientId, Quantity, Unit) " +
            $"VALUES ({productId}, {ingredientId}, 250, 'g');");
        valid.Should().NotThrow();

        // Satuan stok sendiri selalu sah walau tidak punya baris turunan.
        var baseUnit = () => db.Exec(
            "INSERT INTO RecipeLines (ProductTemplateId, IngredientId, Quantity, Unit) " +
            $"VALUES ({productId}, {ingredientId}, 1, 'kg');");
        baseUnit.Should().NotThrow();
    }

    [Fact]
    public void Satuan_Yang_Masih_Dipakai_Resep_Tidak_Boleh_Dihapus_Atau_Diganti()
    {
        using var db = new TempDatabase();
        var ingredientId = InsertIngredient(db);
        var productId = InsertProduct(db);
        var unitId = InsertUnit(db, ingredientId, "g", 0.001m);
        var lineId = Convert.ToInt32(ScalarWithInsert(db,
            "INSERT INTO RecipeLines (ProductTemplateId, IngredientId, Quantity, Unit) " +
            $"VALUES ({productId}, {ingredientId}, 250, 'g');"));

        ShouldBeRejected(db, $"DELETE FROM IngredientUnits WHERE Id = {unitId};");
        // Satuan stok "kg" belum disebut baris resep (yang dipakai "g"), jadi masih boleh diganti.
        var gantiTanpaPemakai = () => db.Exec($"UPDATE Ingredients SET Unit = 'ons' WHERE Id = {ingredientId};");
        gantiTanpaPemakai.Should().NotThrow();

        // Setelah baris resep berpindah ke satuan stok baru, satuan itu terkunci.
        var pindah = () => db.Exec($"UPDATE RecipeLines SET Unit = 'ons' WHERE Id = {lineId};");
        pindah.Should().NotThrow();
        ShouldBeRejected(db, $"UPDATE Ingredients SET Unit = 'gram' WHERE Id = {ingredientId};");

        var hapusBaris = () => db.Exec($"DELETE FROM RecipeLines WHERE Id = {lineId};");
        hapusBaris.Should().NotThrow();
        var hapusSatuan = () => db.Exec($"DELETE FROM IngredientUnits WHERE Id = {unitId};");
        hapusSatuan.Should().NotThrow();
    }
}
