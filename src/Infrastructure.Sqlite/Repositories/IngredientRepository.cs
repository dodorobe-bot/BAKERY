using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class IngredientRepository : IIngredientRepository
{
    private readonly DbConnectionFactory _factory;
    public IngredientRepository(DbConnectionFactory factory) => _factory = factory;

    // CurrentStock adalah turunan dari LocationStock; kolom Ingredients.CurrentStock tidak lagi ditulisi.
    private const string StockExpr =
        "(SELECT COALESCE(SUM(ls.Quantity), 0) FROM LocationStock ls WHERE ls.IngredientId = i.Id)";

    public async Task<Ingredient?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT i.*, {StockExpr} AS CurrentStock FROM Ingredients i WHERE i.Id = @id AND i.IsActive = 1;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? Map(r) : null;
    }

    public async Task<IReadOnlyList<Ingredient>> GetAllActiveAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT i.*, {StockExpr} AS CurrentStock FROM Ingredients i WHERE i.IsActive = 1 ORDER BY i.Name;";
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Ingredient>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyList<Ingredient>> GetAllCountedAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT i.*, {StockExpr} AS CurrentStock FROM Ingredients i
             WHERE i.IsActive = 1 OR {StockExpr} <> 0
             ORDER BY i.IsActive DESC, i.Name;";
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Ingredient>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyList<Ingredient>> GetBelowMinStockAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT i.*, {StockExpr} AS CurrentStock FROM Ingredients i
            WHERE i.IsActive = 1 AND {StockExpr} <= i.MinStockAlert
            ORDER BY i.Name;";
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Ingredient>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public async Task<bool> IsNameTakenAsync(string name, int? exceptId = null)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Bahan nonaktif tidak dihitung: namanya boleh dipakai lagi oleh bahan pengganti.
        cmd.CommandText = @"
            SELECT 1 FROM Ingredients
            WHERE IsActive = 1
              AND lower(trim(Name)) = lower(trim(@nama))
              AND (@kecuali IS NULL OR Id <> @kecuali)
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@nama", name);
        cmd.Parameters.AddWithValue("@kecuali", exceptId ?? (object)DBNull.Value);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    /// <summary>
    /// Sisipkan bahan hanya bila namanya belum dipakai. Pemeriksaan dan penyisipan berada di satu
    /// transaksi tulis (BEGIN IMMEDIATE), jadi dua permintaan bersamaan tidak bisa sama-sama lolos
    /// pemeriksaan. Mengembalikan null berarti nama sudah dipakai.
    /// </summary>
    public async Task<int?> TryCreateAsync(Ingredient i)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction(deferred: false);

        var cek = conn.CreateCommand();
        cek.Transaction = tx;
        cek.CommandText = @"
            SELECT 1 FROM Ingredients
            WHERE IsActive = 1 AND lower(trim(Name)) = lower(trim(@nama))
            LIMIT 1;";
        cek.Parameters.AddWithValue("@nama", i.Name);
        if (await cek.ExecuteScalarAsync() is not null)
        {
            tx.Rollback();
            return null;
        }

        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO Ingredients (Name, Brand, Unit, PricePerUnit, MinStockAlert, IsActive, UpdatedAt)
            VALUES (@n, @b, @u, @p, @ms, 1, @ua);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@n", i.Name);
        cmd.Parameters.AddWithValue("@b", i.Brand ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@u", i.Unit);
        cmd.Parameters.AddWithValue("@p", i.PricePerUnit);
        cmd.Parameters.AddWithValue("@ms", i.MinStockAlert);
        cmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("o"));
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        tx.Commit();
        return id;
    }

    public async Task UpdateAsync(Ingredient i)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Ingredients SET Name=@n, Brand=@b, Unit=@u, PricePerUnit=@p,
                MinStockAlert=@ms, IsActive=@active, UpdatedAt=@ua WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@n", i.Name);
        cmd.Parameters.AddWithValue("@b", i.Brand ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@u", i.Unit);
        cmd.Parameters.AddWithValue("@p", i.PricePerUnit);
        cmd.Parameters.AddWithValue("@ms", i.MinStockAlert);
        cmd.Parameters.AddWithValue("@active", i.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@id", i.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        // Menonaktifkan bahan tidak menghapus stoknya. Selama stoknya masih ada, nilainya tetap
        // dihitung di jurnal persediaan dan di layar Inventaris, tetapi bahan itu hilang dari
        // daftar bahan — jadi laporan dan neraca tidak lagi menunjuk barang yang sama. Karena
        // itu bahan hanya boleh dinonaktifkan setelah raknya kosong.
        var sisa = new List<string>();
        string namaBahan = $"bahan #{id}";
        using (var cek = conn.CreateCommand())
        {
            cek.CommandText = @"
                SELECT sl.Name, ls.Quantity, i.Name, i.Unit
                  FROM LocationStock ls
                  JOIN StorageLocations sl ON sl.Id = ls.LocationId
                  JOIN Ingredients i ON i.Id = ls.IngredientId
                 WHERE ls.IngredientId = @id AND ls.Quantity <> 0
                 ORDER BY sl.Name;";
            cek.Parameters.AddWithValue("@id", id);
            using var reader = await cek.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sisa.Add($"{reader.GetString(0)} {reader.GetDecimal(1):0.##} {reader.GetString(3)}");
                namaBahan = reader.GetString(2);
            }
        }
        if (sisa.Count > 0)
            throw new InvalidOperationException(
                $"Bahan \"{namaBahan}\" masih punya stok: {string.Join(", ", sisa)}. " +
                "Habiskan lebih dulu lewat Formulir Mutasi Bahan atau Pemeriksaan Fisik (Stok Opname) " +
                "supaya setiap selisih ikut membukukan jurnalnya, lalu nonaktifkan.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Ingredients SET IsActive = 0 WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Ingredient Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        LegacyId = r.IsDBNull(r.GetOrdinal("LegacyId")) ? null : r.GetInt32(r.GetOrdinal("LegacyId")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Brand = r.IsDBNull(r.GetOrdinal("Brand")) ? null : r.GetString(r.GetOrdinal("Brand")),
        Unit = r.GetString(r.GetOrdinal("Unit")),
        PricePerUnit = r.GetInt64(r.GetOrdinal("PricePerUnit")),
        CurrentStock = r.GetDecimal(r.GetOrdinal("CurrentStock")),
        MinStockAlert = r.GetDecimal(r.GetOrdinal("MinStockAlert")),
        IsActive = r.GetInt32(r.GetOrdinal("IsActive")) == 1,
        UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("UpdatedAt")))
    };
}
