using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class IdempotencyRepository : IIdempotencyRepository
{
    private readonly DbConnectionFactory _factory;
    public IdempotencyRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IdempotencyRecord?> GetAsync(string key)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Key, Route, UserId, ResourceId, ReservedAt, CompletedAt
            FROM IdempotencyKeys WHERE Key = @k;";
        cmd.Parameters.AddWithValue("@k", key);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new IdempotencyRecord(
            r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetInt32(2),
            r.IsDBNull(3) ? null : r.GetInt32(3),
            DateTime.Parse(r.GetString(4)),
            r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)));
    }

    // Pesan lebih dulu, kerjakan belakangan: kalau urutannya dibalik, dua permintaan yang datang
    // berbarengan sama-sama cek "belum ada" lalu sama-sama menulis dokumen.
    public async Task<bool> ReserveAsync(string key, string route, int? userId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO IdempotencyKeys (Key, Route, UserId) VALUES (@k, @r, @u);";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@r", route);
        cmd.Parameters.AddWithValue("@u", (object?)userId ?? DBNull.Value);
        try
        {
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (SqliteException ex) when (ex.Message.Contains("IdempotencyKeys"))
        {
            return false;
        }
    }

    public async Task CompleteAsync(string key, int? resourceId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE IdempotencyKeys SET ResourceId = @id, CompletedAt = datetime('now') WHERE Key = @k;";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@id", (object?)resourceId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ReleaseAsync(string key)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM IdempotencyKeys WHERE Key = @k AND CompletedAt IS NULL;";
        cmd.Parameters.AddWithValue("@k", key);
        await cmd.ExecuteNonQueryAsync();
    }
}
