using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Satu-satunya tempat SQL yang mengubah stok produk jadi dan mencatat mutasinya, meniru
/// StockLedger untuk bahan mentah. Dipisahkan karena produk jadi tidak punya Id bahan dan
/// tidak boleh tertulis di baris LocationStock yang sama.
/// </summary>
internal static class FinishedGoodsLedger
{
    public static async Task ApplyAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int locationId, int productTemplateId, decimal delta, FinishedGoodsMovementType type,
        string? sourceType, int? sourceId, int? userId, string? notes, long unitCostMinorUnits = 0)
    {
        if (delta == 0) return;

        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = @"
            SELECT COALESCE(SUM(Quantity), 0) FROM FinishedGoodsStock
            WHERE LocationId = @locId AND ProductTemplateId = @prodId;";
        check.Parameters.AddWithValue("@locId", locationId);
        check.Parameters.AddWithValue("@prodId", productTemplateId);
        decimal onHand = Convert.ToDecimal(await check.ExecuteScalarAsync());

        if (onHand + delta < 0)
            throw new InvalidOperationException(await BuildInsufficientStockMessageAsync(
                conn, tx, locationId, productTemplateId, onHand, delta));

        // Sama seperti StockLedger: UPDATE dulu, sisip hanya bila barisnya belum ada, karena
        // SQLite menilai CHECK atas nilai yang akan disisipkan sebelum resolusi konflik.
        using var upsert = conn.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = @"
            UPDATE FinishedGoodsStock
               SET Quantity = Quantity + @delta, UpdatedAt = datetime('now')
             WHERE LocationId = @locId AND ProductTemplateId = @prodId;
            INSERT INTO FinishedGoodsStock (LocationId, ProductTemplateId, Quantity, UpdatedAt)
            SELECT @locId, @prodId, @delta, datetime('now')
             WHERE NOT EXISTS (
                SELECT 1 FROM FinishedGoodsStock WHERE LocationId = @locId AND ProductTemplateId = @prodId);";
        upsert.Parameters.AddWithValue("@locId", locationId);
        upsert.Parameters.AddWithValue("@prodId", productTemplateId);
        upsert.Parameters.AddWithValue("@delta", delta);
        await upsert.ExecuteNonQueryAsync();

        using var ledger = conn.CreateCommand();
        ledger.Transaction = tx;
        ledger.CommandText = @"
            INSERT INTO FinishedGoodsMovements (
                ProductTemplateId, LocationId, MovementType, Quantity, UnitCostMinorUnits,
                SourceType, SourceId, Notes, CreatedAt, CreatedByUserId
            ) VALUES (@prodId, @locId, @mt, @delta, @unitCost, @st, @si, @notes, datetime('now'), @userId);";
        ledger.Parameters.AddWithValue("@prodId", productTemplateId);
        ledger.Parameters.AddWithValue("@locId", locationId);
        ledger.Parameters.AddWithValue("@mt", type.ToString());
        ledger.Parameters.AddWithValue("@delta", delta);
        ledger.Parameters.AddWithValue("@unitCost", unitCostMinorUnits);
        ledger.Parameters.AddWithValue("@st", sourceType ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@si", sourceId ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@notes", notes ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
        await ledger.ExecuteNonQueryAsync();
    }

    private static async Task<string> BuildInsufficientStockMessageAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int locationId, int productTemplateId, decimal onHand, decimal delta)
    {
        using var nameCmd = conn.CreateCommand();
        nameCmd.Transaction = tx;
        nameCmd.CommandText = @"
            SELECT sl.Name, pt.Name
            FROM StorageLocations sl, ProductTemplates pt
            WHERE sl.Id = @locId AND pt.Id = @prodId;";
        nameCmd.Parameters.AddWithValue("@locId", locationId);
        nameCmd.Parameters.AddWithValue("@prodId", productTemplateId);
        using var reader = await nameCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return $"Stok produk jadi tidak mencukupi: tersedia {onHand}, perubahan diminta {delta}.";
        return $"Stok produk {reader.GetString(1)} di {reader.GetString(0)} tidak cukup: " +
               $"tersedia {onHand}, dibutuhkan {-delta}.";
    }
}
