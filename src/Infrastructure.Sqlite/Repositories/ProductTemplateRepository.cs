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
            SELECT pt.*, rl.Id as RlId, rl.IngredientId, rl.Quantity,
                   i.Name as IngName, i.Unit as IngUnit, i.PricePerUnit
            FROM ProductTemplates pt
            LEFT JOIN RecipeLines rl ON pt.Id = rl.ProductTemplateId
            LEFT JOIN Ingredients i ON rl.IngredientId = i.Id
            WHERE pt.Id = @id AND pt.IsActive = 1;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        ProductTemplate? product = null;
        while (r.Read())
        {
            if (product is null) product = MapProduct(r);
            if (!r.IsDBNull(r.GetOrdinal("RlId")))
            {
                product.RecipeLines.Add(new RecipeLine
                {
                    Id = r.GetInt32(r.GetOrdinal("RlId")),
                    ProductTemplateId = id,
                    IngredientId = r.GetInt32(r.GetOrdinal("IngredientId")),
                    Quantity = r.GetDecimal(r.GetOrdinal("Quantity")),
                    Ingredient = new Ingredient
                    {
                        Id = r.GetInt32(r.GetOrdinal("IngredientId")),
                        Name = r.GetString(r.GetOrdinal("IngName")),
                        Unit = r.GetString(r.GetOrdinal("IngUnit")),
                        PricePerUnit = r.GetInt64(r.GetOrdinal("PricePerUnit"))
                    }
                });
            }
        }
        return product;
    }

    public async Task<IReadOnlyList<ProductTemplate>> GetAllActiveAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pt.*, rl.Id as RlId, rl.IngredientId, rl.Quantity,
                   i.Name as IngName, i.Unit as IngUnit, i.PricePerUnit
            FROM ProductTemplates pt
            LEFT JOIN RecipeLines rl ON pt.Id = rl.ProductTemplateId
            LEFT JOIN Ingredients i ON rl.IngredientId = i.Id
            WHERE pt.IsActive = 1
            ORDER BY pt.Name;";
        using var r = await cmd.ExecuteReaderAsync();
        var dict = new Dictionary<int, ProductTemplate>();
        while (r.Read())
        {
            var productId = r.GetInt32(r.GetOrdinal("Id"));
            if (!dict.TryGetValue(productId, out var product))
            {
                product = MapProduct(r);
                dict[productId] = product;
            }
            if (!r.IsDBNull(r.GetOrdinal("RlId")))
            {
                product.RecipeLines.Add(new RecipeLine
                {
                    Id = r.GetInt32(r.GetOrdinal("RlId")),
                    ProductTemplateId = productId,
                    IngredientId = r.GetInt32(r.GetOrdinal("IngredientId")),
                    Quantity = r.GetDecimal(r.GetOrdinal("Quantity")),
                    Ingredient = new Ingredient
                    {
                        Id = r.GetInt32(r.GetOrdinal("IngredientId")),
                        Name = r.IsDBNull(r.GetOrdinal("IngName")) ? "" : r.GetString(r.GetOrdinal("IngName")),
                        Unit = r.IsDBNull(r.GetOrdinal("IngUnit")) ? "" : r.GetString(r.GetOrdinal("IngUnit")),
                        PricePerUnit = r.IsDBNull(r.GetOrdinal("PricePerUnit")) ? 0 : r.GetInt64(r.GetOrdinal("PricePerUnit"))
                    }
                });
            }
        }
        return dict.Values.ToList();
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
            {
                var lCmd = conn.CreateCommand();
                lCmd.Transaction = tx;
                lCmd.CommandText = "INSERT INTO RecipeLines (ProductTemplateId, IngredientId, Quantity) VALUES (@pid, @iid, @q);";
                lCmd.Parameters.AddWithValue("@pid", id);
                lCmd.Parameters.AddWithValue("@iid", line.IngredientId);
                lCmd.Parameters.AddWithValue("@q", line.Quantity);
                await lCmd.ExecuteNonQueryAsync();
            }
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
            {
                var lCmd = conn.CreateCommand();
                lCmd.Transaction = tx;
                lCmd.CommandText = "INSERT INTO RecipeLines (ProductTemplateId, IngredientId, Quantity) VALUES (@pid, @iid, @q);";
                lCmd.Parameters.AddWithValue("@pid", template.Id);
                lCmd.Parameters.AddWithValue("@iid", line.IngredientId);
                lCmd.Parameters.AddWithValue("@q", line.Quantity);
                await lCmd.ExecuteNonQueryAsync();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task DeleteAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ProductTemplates SET IsActive = 0 WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
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
