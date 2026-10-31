using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>Hasil satu perubahan stok produk jadi, termasuk nilainya saat keluar/masuk.</summary>
internal record FinishedGoodsApplied(decimal Quantity, long UnitCostMinorUnits, long ValueMinorUnits);

/// <summary>
/// Satu-satunya tempat SQL yang mengubah stok produk jadi dan mencatat mutasinya, meniru
/// StockLedger untuk bahan mentah. Dipisahkan karena produk jadi tidak punya Id bahan dan
/// tidak boleh tertulis di baris LocationStock yang sama. Biaya rata-rata tertimbang
/// disimpan per lokasi + produk supaya penjualan bisa membukukan harga pokoknya.
/// </summary>
internal static class FinishedGoodsLedger
{
    public static async Task<FinishedGoodsApplied> ApplyAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int locationId, int productTemplateId, decimal delta, FinishedGoodsMovementType type,
        string? sourceType, int? sourceId, int? userId, string? notes, long unitCostMinorUnits = 0)
    {
        if (delta == 0)
            return new FinishedGoodsApplied(0, 0, 0);

        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = @"
            SELECT Quantity, AverageCostMinorUnits FROM FinishedGoodsStock
            WHERE LocationId = @locId AND ProductTemplateId = @prodId;";
        check.Parameters.AddWithValue("@locId", locationId);
        check.Parameters.AddWithValue("@prodId", productTemplateId);

        decimal onHand = 0;
        long averageCost = 0;
        using (var reader = await check.ExecuteReaderAsync())
            if (await reader.ReadAsync())
            {
                onHand = reader.GetDecimal(0);
                averageCost = reader.GetInt64(1);
            }

        if (onHand + delta < 0)
            throw new InvalidOperationException(await BuildInsufficientStockMessageAsync(
                conn, tx, locationId, productTemplateId, onHand, delta));

        decimal newQuantity = onHand + delta;
        long movementCost;
        long value;
        long newAverageCost;

        if (delta < 0)
        {
            // Barang keluar memakai biaya rata-rata yang sedang berlaku; rata-ratanya tidak
            // berubah karena sisa barang berasal dari lapisan biaya yang sama.
            movementCost = averageCost;
            value = Round(-delta * averageCost);
            newAverageCost = newQuantity <= 0 ? 0 : averageCost;
        }
        else
        {
            // Barang masuk boleh membawa biayanya sendiri (hasil produksi, retur, pembatalan).
            // Bila tidak, gunakan rata-rata yang ada supaya penyesuaian stok tidak menghapus nilai.
            movementCost = unitCostMinorUnits > 0 ? unitCostMinorUnits : averageCost;
            value = Round(delta * movementCost);
            newAverageCost = newQuantity <= 0
                ? 0
                : Round((onHand * averageCost + delta * movementCost) / newQuantity);
        }

        // Sama seperti StockLedger: UPDATE dulu, sisip hanya bila barisnya belum ada, karena
        // SQLite menilai CHECK atas nilai yang akan disisipkan sebelum resolusi konflik.
        using var upsert = conn.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = @"
            UPDATE FinishedGoodsStock
               SET Quantity = Quantity + @delta,
                   AverageCostMinorUnits = @avgCost,
                   UpdatedAt = datetime('now')
             WHERE LocationId = @locId AND ProductTemplateId = @prodId;
            INSERT INTO FinishedGoodsStock (LocationId, ProductTemplateId, Quantity, AverageCostMinorUnits, UpdatedAt)
            SELECT @locId, @prodId, @delta, @avgCost, datetime('now')
             WHERE NOT EXISTS (
                SELECT 1 FROM FinishedGoodsStock WHERE LocationId = @locId AND ProductTemplateId = @prodId);";
        upsert.Parameters.AddWithValue("@locId", locationId);
        upsert.Parameters.AddWithValue("@prodId", productTemplateId);
        upsert.Parameters.AddWithValue("@delta", delta);
        upsert.Parameters.AddWithValue("@avgCost", newAverageCost);
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
        ledger.Parameters.AddWithValue("@unitCost", movementCost);
        ledger.Parameters.AddWithValue("@st", sourceType ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@si", sourceId ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@notes", notes ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
        await ledger.ExecuteNonQueryAsync();

        return new FinishedGoodsApplied(delta, movementCost, value);
    }

    private static long Round(decimal value) =>
        (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);

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
