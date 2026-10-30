using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class UserRepository : IUserRepository
{
    private readonly DbConnectionFactory _factory;
    public UserRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<User?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? MapUser(r) : null;
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users WHERE Username = @u COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("@u", username);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? MapUser(r) : null;
    }

    public async Task<IReadOnlyList<User>> GetAllAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users ORDER BY Username;";
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<User>();
        while (r.Read()) list.Add(MapUser(r));
        return list;
    }

    public async Task<int> CreateAsync(User user)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Users (Username, PasswordHash, Role, IsActive, MustChangePassword, CreatedAt, TokenVersion)
            VALUES (@u, @ph, @r, @a, @mc, @ca, @tv);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@u", user.Username);
        cmd.Parameters.AddWithValue("@ph", user.PasswordHash);
        cmd.Parameters.AddWithValue("@r", user.Role.ToString());
        cmd.Parameters.AddWithValue("@a", user.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@mc", user.MustChangePassword ? 1 : 0);
        cmd.Parameters.AddWithValue("@ca", user.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@tv", user.TokenVersion);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(User user)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Users SET
                PasswordHash = @ph, Role = @r, IsActive = @a,
                MustChangePassword = @mc, LastLoginAt = @ll,
                FailedLoginCount = @fc, LockedUntil = @lu,
                TokenVersion = @tv
            WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@ph", user.PasswordHash);
        cmd.Parameters.AddWithValue("@r", user.Role.ToString());
        cmd.Parameters.AddWithValue("@a", user.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@mc", user.MustChangePassword ? 1 : 0);
        cmd.Parameters.AddWithValue("@ll", user.LastLoginAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@fc", user.FailedLoginCount);
        cmd.Parameters.AddWithValue("@lu", user.LockedUntil?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tv", user.TokenVersion);
        cmd.Parameters.AddWithValue("@id", user.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET IsActive = 0 WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static User MapUser(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        LegacyId = r.IsDBNull(r.GetOrdinal("LegacyId")) ? null : r.GetInt32(r.GetOrdinal("LegacyId")),
        Username = r.GetString(r.GetOrdinal("Username")),
        PasswordHash = r.GetString(r.GetOrdinal("PasswordHash")),
        Role = Enum.Parse<UserRole>(r.GetString(r.GetOrdinal("Role"))),
        IsActive = r.GetInt32(r.GetOrdinal("IsActive")) == 1,
        MustChangePassword = r.GetInt32(r.GetOrdinal("MustChangePassword")) == 1,
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        LastLoginAt = r.IsDBNull(r.GetOrdinal("LastLoginAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("LastLoginAt"))),
        FailedLoginCount = r.GetInt32(r.GetOrdinal("FailedLoginCount")),
        LockedUntil = r.IsDBNull(r.GetOrdinal("LockedUntil")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("LockedUntil"))),
        TokenVersion = r.GetInt32(r.GetOrdinal("TokenVersion"))
    };
}
