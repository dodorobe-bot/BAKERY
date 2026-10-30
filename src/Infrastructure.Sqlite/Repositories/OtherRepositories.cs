using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class StockMovementRepository : IStockMovementRepository
{
    private readonly DbConnectionFactory _factory;
    public StockMovementRepository(DbConnectionFactory factory) => _factory = factory;

    // Hanya baca: mutasi dicatat StockLedger bersamaan dengan perubahan LocationStock.
    public async Task<IReadOnlyList<StockMovement>> GetByIngredientAsync(int ingredientId)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM StockMovements WHERE IngredientId = @id ORDER BY CreatedAt DESC LIMIT 50;";
        cmd.Parameters.AddWithValue("@id", ingredientId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<StockMovement>();
        while (r.Read()) list.Add(new StockMovement
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            IngredientId = r.GetInt32(r.GetOrdinal("IngredientId")),
            MovementType = Enum.Parse<MovementType>(r.GetString(r.GetOrdinal("MovementType"))),
            Quantity = r.GetDecimal(r.GetOrdinal("Quantity")),
            LocationId = r.IsDBNull(r.GetOrdinal("LocationId")) ? null : r.GetInt32(r.GetOrdinal("LocationId")),
            SourceType = r.IsDBNull(r.GetOrdinal("SourceType")) ? null : r.GetString(r.GetOrdinal("SourceType")),
            SourceId = r.IsDBNull(r.GetOrdinal("SourceId")) ? null : r.GetInt32(r.GetOrdinal("SourceId")),
            Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
            CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
            CreatedByUserId = r.IsDBNull(r.GetOrdinal("CreatedByUserId")) ? null : r.GetInt32(r.GetOrdinal("CreatedByUserId"))
        });
        return list;
    }
}

public class SettingRepository : ISettingRepository
{
    private readonly DbConnectionFactory _factory;
    public SettingRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<string?> GetAsync(string key)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = @k;";
        cmd.Parameters.AddWithValue("@k", key);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull ? null : result?.ToString();
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM AppSettings;";
        using var r = await cmd.ExecuteReaderAsync();
        var dict = new Dictionary<string, string>();
        while (r.Read())
        {
            if (!r.IsDBNull(1)) dict[r.GetString(0)] = r.GetString(1);
        }
        return dict;
    }

    public async Task SetAsync(string key, string value, int? userId = null)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AppSettings (Key, Value, UpdatedAt, UpdatedByUserId)
            VALUES (@k, @v, @ua, @uid)
            ON CONFLICT(Key) DO UPDATE SET Value=@v, UpdatedAt=@ua, UpdatedByUserId=@uid;";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@uid", userId ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}

public class AccountRepository : IAccountRepository
{
    private readonly DbConnectionFactory _factory;
    public AccountRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<Account?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Accounts WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? Map(r) : null;
    }

    public async Task<Account?> GetByCodeAsync(string code)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Accounts WHERE Code = @c;";
        cmd.Parameters.AddWithValue("@c", code);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? Map(r) : null;
    }

    public async Task<IReadOnlyList<Account>> GetAllActiveAsync()
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Accounts WHERE IsActive = 1 ORDER BY Code;";
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Account>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public async Task<int> CreateAsync(Account account)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Accounts (Code, Name, Type, ParentId, IsSystem, IsActive)
            VALUES (@c, @n, @t, @pid, @sys, 1);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@c", account.Code);
        cmd.Parameters.AddWithValue("@n", account.Name);
        cmd.Parameters.AddWithValue("@t", account.Type.ToString());
        cmd.Parameters.AddWithValue("@pid", account.ParentId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@sys", account.IsSystem ? 1 : 0);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static Account Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        LegacyId = r.IsDBNull(r.GetOrdinal("LegacyId")) ? null : r.GetInt32(r.GetOrdinal("LegacyId")),
        Code = r.GetString(r.GetOrdinal("Code")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Type = Enum.Parse<AccountType>(r.GetString(r.GetOrdinal("Type"))),
        ParentId = r.IsDBNull(r.GetOrdinal("ParentId")) ? null : r.GetInt32(r.GetOrdinal("ParentId")),
        IsSystem = r.GetInt32(r.GetOrdinal("IsSystem")) == 1,
        IsActive = r.GetInt32(r.GetOrdinal("IsActive")) == 1
    };
}

public class AuditLogRepository : IAuditLogRepository
{
    private readonly DbConnectionFactory _factory;
    public AuditLogRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task LogAsync(AuditLog entry)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AuditLog (UserId, Action, EntityType, EntityId, OldValues, NewValues, OccurredAt, SessionId)
            VALUES (@uid, @a, @et, @eid, @ov, @nv, @oa, @sid);";
        cmd.Parameters.AddWithValue("@uid", entry.UserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@a", entry.Action);
        cmd.Parameters.AddWithValue("@et", entry.EntityType);
        cmd.Parameters.AddWithValue("@eid", entry.EntityId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ov", entry.OldValues ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@nv", entry.NewValues ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@oa", entry.OccurredAt.ToString("o"));
        cmd.Parameters.AddWithValue("@sid", entry.SessionId ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AuditEntryView>> SearchAsync(AuditFilter filter)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        // Urut menurut Id, bukan teks OccurredAt: kolomnya punya dua format tulisan
        // (milik aplikasi dan milik DEFAULT kolom), sedangkan Id naik seiring waktu tulis.
        cmd.CommandText = @"
            SELECT a.Id, a.UserId, u.Username, a.Action, a.EntityType, a.EntityId,
                   a.OldValues, a.NewValues, a.OccurredAt, a.SessionId
            FROM AuditLog a
            LEFT JOIN Users u ON u.Id = a.UserId
            WHERE (@et IS NULL OR a.EntityType = @et)
              AND (@eid IS NULL OR a.EntityId = @eid)
              AND (@uid IS NULL OR a.UserId = @uid)
              AND (@dari IS NULL OR a.OccurredAt >= @dari)
              AND (@sampai IS NULL OR a.OccurredAt <= @sampai)
            ORDER BY a.Id DESC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@et", filter.EntityType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@eid", filter.EntityId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@uid", filter.UserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@dari", filter.From?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@sampai", filter.To?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", filter.Limit);

        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<AuditEntryView>();
        while (await r.ReadAsync())
        {
            list.Add(new AuditEntryView(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                BusinessTime.BacaCapWaktu(r.GetString(8)),
                r.IsDBNull(9) ? null : r.GetString(9)));
        }
        return list;
    }
}

/// <summary>
/// Lot bahan &amp; tanggal kedaluwarsa. Isinya berasal dari struk penerimaan (GoodsReceiptLineId)
/// atau dari catatan manual pemilik; tidak ada saldo stok per lot di sini.
/// </summary>
public class BatchRepository : IBatchRepository
{
    /// <summary>Lot dianggap "segera" kedaluwarsa bila tinggal sekian hari lagi.</summary>
    public const int AmbangSegeraHari = 14;

    private readonly DbConnectionFactory _factory;
    public BatchRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<ItemBatchView>> GetAsync(string? status = null, int limit = 200)
    {
        var hariIni = BusinessTime.Today().ToString("yyyy-MM-dd");
        var ambang = BusinessTime.Today().AddDays(AmbangSegeraHari).ToString("yyyy-MM-dd");

        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT b.Id, b.IngredientId, i.Name, i.Unit, b.BatchNumber,
                   b.ReceivedDate, b.ExpiryDate, b.UnitCostMinorUnits,
                   CAST(julianday(b.ExpiryDate) - julianday(@hariIni) AS INTEGER) AS sisa
            FROM ItemBatches b
            JOIN Ingredients i ON i.Id = b.IngredientId
            WHERE (@status IS NULL
                   OR (@status = 'kedaluwarsa' AND b.ExpiryDate < @hariIni)
                   OR (@status = 'segera' AND b.ExpiryDate >= @hariIni AND b.ExpiryDate <= @ambang)
                   OR (@status = 'aman' AND b.ExpiryDate > @ambang))
            ORDER BY b.ExpiryDate ASC, b.Id ASC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@hariIni", hariIni);
        cmd.Parameters.AddWithValue("@ambang", ambang);
        cmd.Parameters.AddWithValue("@status", status ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<ItemBatchView>();
        while (await r.ReadAsync())
        {
            var sisa = r.GetInt32(8);
            list.Add(new ItemBatchView(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4),
                Tanggal(r.GetString(5)), Tanggal(r.GetString(6)), r.GetInt64(7), sisa,
                sisa < 0 ? "kedaluwarsa" : sisa <= AmbangSegeraHari ? "segera" : "aman"));
        }
        return list;
    }

    public async Task<int> CreateAsync(NewBatch batch)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ItemBatches (IngredientId, BatchNumber, ExpiryDate, ReceivedDate, UnitCostMinorUnits, GoodsReceiptLineId)
            VALUES (@ing, @nomor, @kedaluwarsa, @terima, @harga, @baris);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@ing", batch.IngredientId);
        cmd.Parameters.AddWithValue("@nomor", batch.BatchNumber);
        cmd.Parameters.AddWithValue("@kedaluwarsa", batch.ExpiryDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@terima", batch.ReceivedDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@harga", batch.UnitCostMinorUnits);
        cmd.Parameters.AddWithValue("@baris", batch.GoodsReceiptLineId ?? (object)DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // Kolom tanggal ditulis sebagai teks 'yyyy-MM-dd' (DEFAULT date('now')), bukan format bundar,
    // jadi dibelah sendiri supaya tidak bergantung pada tafsir parser untuk dua bentuk.
    private static DateTime Tanggal(string teks) =>
        DateTime.TryParse(teks, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var nilai) ? nilai.Date : DateTime.MinValue;
}
