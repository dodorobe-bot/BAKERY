using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class LocationStockRepository : ILocationStockRepository
{
    private readonly DbConnectionFactory _factory;

    public LocationStockRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<StorageLocation>> GetLocationsAsync()
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Code, Name, Type, IsActive FROM StorageLocations WHERE IsActive = 1 ORDER BY Id;";

        var list = new List<StorageLocation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new StorageLocation
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Type = reader.GetString(3),
                IsActive = reader.GetInt32(4) == 1
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<LocationStock>> GetStockByLocationAsync(int locationId)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ls.Id, ls.LocationId, ls.IngredientId, ls.Quantity,
                   sl.Name AS LocationName, i.Name AS IngredientName, i.Unit, i.MinStockAlert
            FROM LocationStock ls
            JOIN StorageLocations sl ON ls.LocationId = sl.Id
            JOIN Ingredients i ON ls.IngredientId = i.Id
            WHERE ls.LocationId = @locId
            ORDER BY i.Name;";
        cmd.Parameters.AddWithValue("@locId", locationId);

        var list = new List<LocationStock>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new LocationStock
            {
                Id = reader.GetInt32(0),
                LocationId = reader.GetInt32(1),
                IngredientId = reader.GetInt32(2),
                Quantity = reader.GetDecimal(3),
                LocationName = reader.GetString(4),
                IngredientName = reader.GetString(5),
                Unit = reader.GetString(6),
                MinStockAlert = reader.GetDecimal(7)
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<StockMovementView>> GetMovementsAsync(int? locationId, int limit = 50)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT m.Id, m.IngredientId, i.Name, i.Unit,
                   CASE WHEN m.MovementType = 'Adjustment' AND m.SourceType = 'LocationTransfer'
                        THEN 'Transfer' ELSE m.MovementType END,
                   m.Quantity,
                   m.LocationId, sl.Name, m.SourceType, m.SourceId, m.Notes, m.CreatedAt
            FROM StockMovements m
            JOIN Ingredients i ON i.Id = m.IngredientId
            LEFT JOIN StorageLocations sl ON sl.Id = m.LocationId
            WHERE (@locId IS NULL OR m.LocationId = @locId)
            ORDER BY m.CreatedAt DESC, m.Id DESC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@locId", (object?)locationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<StockMovementView>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new StockMovementView(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                DateTime.SpecifyKind(DateTime.Parse(reader.GetString(11)), DateTimeKind.Utc)));
        }
        return list;
    }

    public async Task<decimal> GetTotalStockAsync(int ingredientId)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(Quantity), 0) FROM LocationStock WHERE IngredientId = @ingId;";
        cmd.Parameters.AddWithValue("@ingId", ingredientId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    public async Task<int> GetLocationIdByTypeAsync(string type)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        return await StockLedger.ResolveLocationIdByTypeAsync(conn, null, type);
    }

    public async Task ApplyAsync(IReadOnlyList<StockChange> changes)
    {
        if (changes.Count == 0) return;
        await WriteAsync(async (conn, tx) =>
        {
            foreach (var c in changes)
                await StockLedger.ApplyAsync(conn, tx, c.LocationId, c.IngredientId, c.Delta, c.Type,
                    c.SourceType, c.SourceId, c.UserId, c.Notes);
        });
    }

    public async Task TransferStockAsync(
        int fromLocationId,
        int toLocationId,
        int ingredientId,
        decimal quantity,
        int? userId,
        string? notes)
    {
        if (quantity <= 0) return;
        if (fromLocationId == toLocationId)
            throw new InvalidOperationException("Lokasi asal dan tujuan transfer harus berbeda.");

        // Jenis mutasi tersimpan 'Adjustment' karena CHECK di tabel StockMovements belum mengenal
        // 'Transfer' (mengubahnya butuh migrasi). Pemisahnya SourceType 'LocationTransfer', dan
        // GetMovementsAsync menerjemahkannya menjadi 'Transfer' supaya layar tidak menyebut
        // perpindahan barang sebagai koreksi manual.
        await WriteAsync(async (conn, tx) =>
        {
            await StockLedger.ApplyAsync(conn, tx, fromLocationId, ingredientId, -quantity, MovementType.Adjustment,
                "LocationTransfer", fromLocationId, userId, notes);
            await StockLedger.ApplyAsync(conn, tx, toLocationId, ingredientId, quantity, MovementType.Adjustment,
                "LocationTransfer", fromLocationId, userId, notes);
        });
    }

    public async Task AdjustStockAsync(int locationId, int ingredientId, decimal newQuantity, int? userId, string? reason)
    {
        if (newQuantity < 0)
            throw new InvalidOperationException("Stok hasil penyesuaian tidak boleh negatif.");

        await WriteAsync(async (conn, tx) =>
        {
            using var cur = conn.CreateCommand();
            cur.Transaction = tx;
            cur.CommandText = @"
                SELECT COALESCE(SUM(Quantity), 0) FROM LocationStock
                WHERE LocationId = @locId AND IngredientId = @ingId;";
            cur.Parameters.AddWithValue("@locId", locationId);
            cur.Parameters.AddWithValue("@ingId", ingredientId);
            decimal current = Convert.ToDecimal(await cur.ExecuteScalarAsync());

            await StockLedger.ApplyAsync(conn, tx, locationId, ingredientId, newQuantity - current,
                MovementType.Adjustment, "Adjustment", null, userId, reason);
        });
    }

    public async Task<IReadOnlyList<FinishedGoodsStockItem>> GetFinishedGoodsStockAsync(int? locationId = null)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT fgs.LocationId, sl.Name, fgs.ProductTemplateId, pt.Name, fgs.Quantity, fgs.UpdatedAt
            FROM FinishedGoodsStock fgs
            JOIN StorageLocations sl ON fgs.LocationId = sl.Id
            JOIN ProductTemplates pt ON fgs.ProductTemplateId = pt.Id
            WHERE (@locId IS NULL OR fgs.LocationId = @locId)
            ORDER BY pt.Name, sl.Name;";
        cmd.Parameters.AddWithValue("@locId", locationId ?? (object)DBNull.Value);

        var list = new List<FinishedGoodsStockItem>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new FinishedGoodsStockItem
            {
                LocationId = reader.GetInt32(0),
                LocationName = reader.GetString(1),
                ProductTemplateId = reader.GetInt32(2),
                ProductName = reader.GetString(3),
                Quantity = reader.GetDecimal(4),
                UpdatedAt = DateTime.SpecifyKind(DateTime.Parse(reader.GetString(5)), DateTimeKind.Utc)
            });
        }
        return list;
    }

    public async Task<decimal> GetFinishedGoodsTotalAsync(int productTemplateId)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(Quantity), 0) FROM FinishedGoodsStock WHERE ProductTemplateId = @prodId;";
        cmd.Parameters.AddWithValue("@prodId", productTemplateId);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }

    // Satu koneksi + satu transaksi untuk setiap operasi tulis agar stok dan ledger tidak pernah separuh jalan.
    private Task WriteAsync(Func<SqliteConnection, SqliteTransaction, Task> action)
        => WriteTransaction.RunAsync(_factory, action);
}
