using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Satu-satunya tempat SQL yang mengubah stok fisik dan mencatat mutasinya.
/// Semua jalur tulis (pembelian, produksi, penjualan, transfer, penyesuaian) lewat sini
/// supaya LocationStock dan StockMovements tidak pernah bergerak sendiri-sendiri.
/// </summary>
internal static class StockLedger
{
    public static async Task ApplyAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int locationId, int ingredientId, decimal delta, MovementType type,
        string? sourceType, int? sourceId, int? userId, string? notes)
    {
        if (delta == 0) return;

        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = @"
            SELECT COALESCE(SUM(Quantity), 0) FROM LocationStock
            WHERE LocationId = @locId AND IngredientId = @ingId;";
        check.Parameters.AddWithValue("@locId", locationId);
        check.Parameters.AddWithValue("@ingId", ingredientId);
        decimal onHand = Convert.ToDecimal(await check.ExecuteScalarAsync());

        if (onHand + delta < 0)
            throw new InvalidOperationException(
                await BuildInsufficientStockMessageAsync(conn, tx, locationId, ingredientId, onHand, delta));

        // Bukan upsert biasa: SQLite menilai CHECK pada nilai yang akan disisipkan sebelum
        // resolusi konflik, sehingga kaki negatif (pengurangan) akan selalu ditolak.
        using var upsert = conn.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = @"
            UPDATE LocationStock
               SET Quantity = Quantity + @delta, UpdatedAt = datetime('now')
             WHERE LocationId = @locId AND IngredientId = @ingId;
            INSERT INTO LocationStock (LocationId, IngredientId, Quantity, UpdatedAt)
            SELECT @locId, @ingId, @delta, datetime('now')
             WHERE NOT EXISTS (
                SELECT 1 FROM LocationStock WHERE LocationId = @locId AND IngredientId = @ingId);";
        upsert.Parameters.AddWithValue("@locId", locationId);
        upsert.Parameters.AddWithValue("@ingId", ingredientId);
        upsert.Parameters.AddWithValue("@delta", delta);
        await upsert.ExecuteNonQueryAsync();

        using var ledger = conn.CreateCommand();
        ledger.Transaction = tx;
        ledger.CommandText = @"
            INSERT INTO StockMovements (
                IngredientId, MovementType, Quantity, LocationId, SourceType, SourceId, Notes, CreatedAt, CreatedByUserId
            ) VALUES (@ingId, @mt, @delta, @locId, @st, @si, @notes, datetime('now'), @userId);";
        ledger.Parameters.AddWithValue("@ingId", ingredientId);
        ledger.Parameters.AddWithValue("@mt", type.ToString());
        ledger.Parameters.AddWithValue("@delta", delta);
        ledger.Parameters.AddWithValue("@locId", locationId);
        ledger.Parameters.AddWithValue("@st", sourceType ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@si", sourceId ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@notes", notes ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
        await ledger.ExecuteNonQueryAsync();
    }

    // Pesan gagal dibaca pengguna layar, jadi pakai nama lokasi/bahan, bukan ID.
    private static async Task<string> BuildInsufficientStockMessageAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int locationId, int ingredientId, decimal onHand, decimal delta)
    {
        using var nameCmd = conn.CreateCommand();
        nameCmd.Transaction = tx;
        nameCmd.CommandText = @"
            SELECT sl.Name, i.Name, i.Unit
            FROM StorageLocations sl, Ingredients i
            WHERE sl.Id = @locId AND i.Id = @ingId;";
        nameCmd.Parameters.AddWithValue("@locId", locationId);
        nameCmd.Parameters.AddWithValue("@ingId", ingredientId);
        using var reader = await nameCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return $"Stok tidak mencukupi: tersedia {onHand}, perubahan diminta {delta}.";
        return $"Stok {reader.GetString(1)} di {reader.GetString(0)} tidak cukup: " +
               $"tersedia {onHand} {reader.GetString(2)}, dibutuhkan {-delta} {reader.GetString(2)}.";
    }

    public static async Task<int> ResolveLocationIdByTypeAsync(
        SqliteConnection conn, SqliteTransaction? tx, string type)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id FROM StorageLocations WHERE Type = @type AND IsActive = 1 ORDER BY Id LIMIT 1;";
        cmd.Parameters.AddWithValue("@type", type);
        var found = await cmd.ExecuteScalarAsync();
        if (found is null or DBNull)
            throw new InvalidOperationException($"Tidak ada lokasi aktif bertipe '{type}' di master lokasi.");
        return Convert.ToInt32(found);
    }
}
