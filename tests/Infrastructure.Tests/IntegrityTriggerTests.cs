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
}
