using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class ProductTemplateRepository : IProductTemplateRepository
{
    private readonly DbConnectionFactory _factory;
    public ProductTemplateRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<ProductTemplate?> GetByIdWithRecipesAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pt.*, rl.Id as RlId, rl.IngredientId, rl.Quantity, rl.Unit as RlUnit,
                   i.Name as IngName, i.Unit as IngUnit, i.PricePerUnit
            FROM ProductTemplates pt
            LEFT JOIN RecipeLines rl ON pt.Id = rl.ProductTemplateId
            LEFT JOIN Ingredients i ON rl.IngredientId = i.Id
            WHERE pt.Id = @id AND pt.IsActive = 1;";
        cmd.Parameters.AddWithValue("@id", id);
        ProductTemplate? product = null;
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (r.Read())
            {
                if (product is null) product = MapProduct(r);
                if (!r.IsDBNull(r.GetOrdinal("RlId")))
                    product.RecipeLines.Add(MapLine(r, id));
            }
        }
        if (product is not null) await LampirkanSatuanAsync(conn, [product]);
        return product;
    }

    public async Task<IReadOnlyList<ProductTemplate>> GetAllActiveAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pt.*, rl.Id as RlId, rl.IngredientId, rl.Quantity, rl.Unit as RlUnit,
                   i.Name as IngName, i.Unit as IngUnit, i.PricePerUnit
            FROM ProductTemplates pt
            LEFT JOIN RecipeLines rl ON pt.Id = rl.ProductTemplateId
            LEFT JOIN Ingredients i ON rl.IngredientId = i.Id
            WHERE pt.IsActive = 1
            ORDER BY pt.Name;";
        var dict = new Dictionary<int, ProductTemplate>();
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (r.Read())
            {
                var productId = r.GetInt32(r.GetOrdinal("Id"));
                if (!dict.TryGetValue(productId, out var product))
                {
                    product = MapProduct(r);
                    dict[productId] = product;
                }
                if (!r.IsDBNull(r.GetOrdinal("RlId")))
                    product.RecipeLines.Add(MapLine(r, productId));
            }
        }
        await LampirkanSatuanAsync(conn, dict.Values);
        return dict.Values.ToList();
    }

    private static RecipeLine MapLine(SqliteDataReader r, int productId) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("RlId")),
        ProductTemplateId = productId,
        IngredientId = r.GetInt32(r.GetOrdinal("IngredientId")),
        Quantity = r.GetDecimal(r.GetOrdinal("Quantity")),
        Unit = r.IsDBNull(r.GetOrdinal("RlUnit")) ? null : r.GetString(r.GetOrdinal("RlUnit")),
        Ingredient = new Ingredient
        {
            Id = r.GetInt32(r.GetOrdinal("IngredientId")),
            Name = r.IsDBNull(r.GetOrdinal("IngName")) ? "" : r.GetString(r.GetOrdinal("IngName")),
            Unit = r.IsDBNull(r.GetOrdinal("IngUnit")) ? "" : r.GetString(r.GetOrdinal("IngUnit")),
            PricePerUnit = r.IsDBNull(r.GetOrdinal("PricePerUnit")) ? 0 : r.GetInt64(r.GetOrdinal("PricePerUnit"))
        }
    };

    // Resep yang memakai satuan turunan butuh faktornya untuk menghitung BaseQuantity/HPP, jadi
    // satuan dilampirkan ke salinan bahan di tiap baris (satu kueri untuk semua bahan).
    private static async Task LampirkanSatuanAsync(SqliteConnection conn, IEnumerable<ProductTemplate> produk)
    {
        var bahan = produk.SelectMany(p => p.RecipeLines)
            .Select(l => l.Ingredient)
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();
        if (bahan.Count == 0) return;

        var perId = new Dictionary<int, List<IngredientUnit>>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IngredientId, Id, Unit, FactorToBase FROM IngredientUnits ORDER BY IngredientId, Unit;";
        using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var iid = r.GetInt32(0);
                if (!perId.TryGetValue(iid, out var list)) perId[iid] = list = new List<IngredientUnit>();
                list.Add(new IngredientUnit
                {
                    Id = r.GetInt32(1),
                    IngredientId = iid,
                    Unit = r.GetString(2),
                    FactorToBase = r.GetDecimal(3)
                });
            }
        foreach (var b in bahan)
            if (perId.TryGetValue(b.Id, out var list)) b.AdditionalUnits = list;
    }

    public async Task<int> CreateAsync(ProductTemplate template, IEnumerable<RecipeLine> lines)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO ProductTemplates (Name, Category, Barcode, SellingPrice, IsActive)
                VALUES (@n, @c, @barcode, @sp, 1);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@n", template.Name);
            cmd.Parameters.AddWithValue("@c", template.Category ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(template.Barcode)
                ? (object)DBNull.Value
                : template.Barcode.Trim());
            cmd.Parameters.AddWithValue("@sp", template.SellingPrice);
            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            foreach (var line in lines)
                await SisipBarisAsync(conn, tx, id, line);
            tx.Commit();
            return id;
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task UpdateAsync(ProductTemplate template, IEnumerable<RecipeLine> lines)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE ProductTemplates SET Name=@n, Category=@c, Barcode=@barcode, SellingPrice=@sp WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@n", template.Name);
            cmd.Parameters.AddWithValue("@c", template.Category ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(template.Barcode)
                ? (object)DBNull.Value
                : template.Barcode.Trim());
            cmd.Parameters.AddWithValue("@sp", template.SellingPrice);
            cmd.Parameters.AddWithValue("@id", template.Id);
            await cmd.ExecuteNonQueryAsync();

            var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM RecipeLines WHERE ProductTemplateId = @id;";
            del.Parameters.AddWithValue("@id", template.Id);
            await del.ExecuteNonQueryAsync();

            foreach (var line in lines)
                await SisipBarisAsync(conn, tx, template.Id, line);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task DeleteAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        // Sama seperti bahan: menonaktifkan produk tidak menghapus stok produk jadinya. Saldo
        // akun 1-006 tetap memuat nilainya sementara produk itu hilang dari katalog, jadi
        // neraca dan daftar barang tidak lagi sama. Produk hanya boleh dinonaktifkan setelah
        // stok jadinya habis.
        var sisa = new List<string>();
        string namaProduk = $"produk #{id}";
        using (var cek = conn.CreateCommand())
        {
            cek.CommandText = @"
                SELECT sl.Name, fgs.Quantity, pt.Name
                  FROM FinishedGoodsStock fgs
                  JOIN StorageLocations sl ON sl.Id = fgs.LocationId
                  JOIN ProductTemplates pt ON pt.Id = fgs.ProductTemplateId
                 WHERE fgs.ProductTemplateId = @id AND fgs.Quantity <> 0
                 ORDER BY sl.Name;";
            cek.Parameters.AddWithValue("@id", id);
            using var reader = await cek.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sisa.Add($"{reader.GetString(0)} {reader.GetDecimal(1):0.##} unit");
                namaProduk = reader.GetString(2);
            }
        }
        if (sisa.Count > 0)
            throw new InvalidOperationException(
                $"Produk \"{namaProduk}\" masih punya stok produk jadi: {string.Join(", ", sisa)}. " +
                "Stok itu tetap dihitung di akun Persediaan Produk Jadi, jadi jual atau keluarkan " +
                "lebih dulu sebelum produk dinonaktifkan.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ProductTemplates SET IsActive = 0 WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SisipBarisAsync(
        SqliteConnection conn, SqliteTransaction tx, int productId, RecipeLine line)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO RecipeLines (ProductTemplateId, IngredientId, Quantity, Unit)
            VALUES (@pid, @iid, @q, @u);";
        cmd.Parameters.AddWithValue("@pid", productId);
        cmd.Parameters.AddWithValue("@iid", line.IngredientId);
        cmd.Parameters.AddWithValue("@q", line.Quantity);
        cmd.Parameters.AddWithValue("@u", string.IsNullOrWhiteSpace(line.Unit)
            ? (object)DBNull.Value
            : line.Unit.Trim());
        await cmd.ExecuteNonQueryAsync();
    }

    private static ProductTemplate MapProduct(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        LegacyId = r.IsDBNull(r.GetOrdinal("LegacyId")) ? null : r.GetInt32(r.GetOrdinal("LegacyId")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Category = r.IsDBNull(r.GetOrdinal("Category")) ? null : r.GetString(r.GetOrdinal("Category")),
        Barcode = r.IsDBNull(r.GetOrdinal("Barcode")) ? null : r.GetString(r.GetOrdinal("Barcode")),
        SellingPrice = r.GetInt64(r.GetOrdinal("SellingPrice")),
        IsActive = r.GetInt32(r.GetOrdinal("IsActive")) == 1
    };
}
