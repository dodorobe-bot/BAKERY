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
        Ingredient? bahan;
        using (var r = await cmd.ExecuteReaderAsync())
            bahan = r.Read() ? Map(r) : null;
        if (bahan is not null) await LampirkanSatuanAsync(conn, [bahan]);
        return bahan;
    }

    public async Task<IReadOnlyList<Ingredient>> GetAllActiveAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT i.*, {StockExpr} AS CurrentStock FROM Ingredients i WHERE i.IsActive = 1 ORDER BY i.Name;";
        var list = new List<Ingredient>();
        using (var r = await cmd.ExecuteReaderAsync())
            while (r.Read()) list.Add(Map(r));
        await LampirkanSatuanAsync(conn, list);
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
        var list = new List<Ingredient>();
        using (var r = await cmd.ExecuteReaderAsync())
            while (r.Read()) list.Add(Map(r));
        await LampirkanSatuanAsync(conn, list);
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
        var list = new List<Ingredient>();
        using (var r = await cmd.ExecuteReaderAsync())
            while (r.Read()) list.Add(Map(r));
        await LampirkanSatuanAsync(conn, list);
        return list;
    }

    // Tabel satuan turunan kecil (beberapa baris per bahan), jadi dibaca sekali untuk seluruh
    // daftar. Baris resep memakai faktor ini agar "250 g" tidak lagi dibaca 250 kg.
    private static async Task LampirkanSatuanAsync(SqliteConnection conn, IReadOnlyList<Ingredient> bahan)
    {
        if (bahan.Count == 0) return;
        var perId = bahan.ToDictionary(b => b.Id);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, IngredientId, Unit, FactorToBase FROM IngredientUnits ORDER BY IngredientId, Unit;";
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            if (!perId.TryGetValue(r.GetInt32(1), out var b)) continue;
            b.AdditionalUnits.Add(new IngredientUnit
            {
                Id = r.GetInt32(0),
                IngredientId = r.GetInt32(1),
                Unit = r.GetString(2),
                FactorToBase = r.GetDecimal(3)
            });
        }
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
        foreach (var satuan in i.AdditionalUnits)
        {
            var uCmd = conn.CreateCommand();
            uCmd.Transaction = tx;
            uCmd.CommandText = "INSERT INTO IngredientUnits (IngredientId, Unit, FactorToBase) VALUES (@iid, @u, @f);";
            uCmd.Parameters.AddWithValue("@iid", id);
            uCmd.Parameters.AddWithValue("@u", satuan.Unit.Trim());
            uCmd.Parameters.AddWithValue("@f", satuan.FactorToBase);
            await uCmd.ExecuteNonQueryAsync();
        }
        tx.Commit();
        return id;
    }

    public async Task UpdateAsync(Ingredient i)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction(deferred: false);

        // Satuan stok diganti lebih dulu supaya pemicu trg_ingredients_unit_update bisa menolak
        // perubahan yang membuat baris resep menyebut satuan yang tidak ada lagi.
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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

        // Selisihkan, jangan hapus-semua-lalu-sisipkan: menghapus satuan yang masih dipakai
        // baris resep ditolak pemicu, dan itu harus tetap berlaku walau faktornya tidak berubah.
        var lama = new List<IngredientUnit>();
        var baca = conn.CreateCommand();
        baca.Transaction = tx;
        baca.CommandText = "SELECT Id, Unit, FactorToBase FROM IngredientUnits WHERE IngredientId = @id ORDER BY Id;";
        baca.Parameters.AddWithValue("@id", i.Id);
        using (var r = await baca.ExecuteReaderAsync())
            while (await r.ReadAsync())
                lama.Add(new IngredientUnit
                {
                    Id = r.GetInt32(0),
                    IngredientId = i.Id,
                    Unit = r.GetString(1),
                    FactorToBase = r.GetDecimal(2)
                });

        var baru = i.AdditionalUnits.ToList();
        foreach (var satuan in lama)
        {
            var cocok = baru.FirstOrDefault(u =>
                string.Equals(u.Unit.Trim(), satuan.Unit.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cocok is null)
            {
                var hapus = conn.CreateCommand();
                hapus.Transaction = tx;
                hapus.CommandText = "DELETE FROM IngredientUnits WHERE Id = @id;";
                hapus.Parameters.AddWithValue("@id", satuan.Id);
                await hapus.ExecuteNonQueryAsync();
            }
            else if (cocok.FactorToBase != satuan.FactorToBase)
            {
                var ubah = conn.CreateCommand();
                ubah.Transaction = tx;
                ubah.CommandText = "UPDATE IngredientUnits SET FactorToBase = @f WHERE Id = @id;";
                ubah.Parameters.AddWithValue("@f", cocok.FactorToBase);
                ubah.Parameters.AddWithValue("@id", satuan.Id);
                await ubah.ExecuteNonQueryAsync();
            }
        }
        foreach (var satuan in baru)
        {
            if (lama.Any(l => string.Equals(l.Unit.Trim(), satuan.Unit.Trim(), StringComparison.OrdinalIgnoreCase)))
                continue;
            var sisip = conn.CreateCommand();
            sisip.Transaction = tx;
            sisip.CommandText = "INSERT INTO IngredientUnits (IngredientId, Unit, FactorToBase) VALUES (@iid, @u, @f);";
            sisip.Parameters.AddWithValue("@iid", i.Id);
            sisip.Parameters.AddWithValue("@u", satuan.Unit.Trim());
            sisip.Parameters.AddWithValue("@f", satuan.FactorToBase);
            await sisip.ExecuteNonQueryAsync();
        }
        tx.Commit();
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
