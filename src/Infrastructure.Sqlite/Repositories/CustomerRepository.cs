using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly DbConnectionFactory _factory;
    public CustomerRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<Customer?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Customers WHERE Id = @id AND IsActive = 1;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? Map(r) : null;
    }

    public async Task<Customer?> GetByPhoneAsync(string phone)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Customers WHERE Phone = @p AND IsActive = 1;";
        cmd.Parameters.AddWithValue("@p", phone);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? Map(r) : null;
    }

    public async Task<IReadOnlyList<Customer>> GetAllActiveAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Customers WHERE IsActive = 1 ORDER BY Name;";
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Customer>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyList<Customer>> GetPageAsync(string? search, int limit, int offset)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Nama diurutkan supaya halaman berikutnya stabil; Id jadi pemecah seri.
        cmd.CommandText = @"
            SELECT * FROM Customers
            WHERE IsActive = 1 AND (@cari IS NULL OR Name LIKE @pola OR Phone LIKE @pola)
            ORDER BY Name, Id
            LIMIT @limit OFFSET @offset;";
        TambahPencarian(cmd, search);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Customer>();
        while (await r.ReadAsync()) list.Add(Map(r));
        return list;
    }

    public async Task<int> CountActiveAsync(string? search)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM Customers
            WHERE IsActive = 1 AND (@cari IS NULL OR Name LIKE @pola OR Phone LIKE @pola);";
        TambahPencarian(cmd, search);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static void TambahPencarian(SqliteCommand cmd, string? search)
    {
        var kata = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        cmd.Parameters.AddWithValue("@cari", kata ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@pola", kata is null ? (object)DBNull.Value : $"%{kata}%");
    }

    public async Task<int> CreateAsync(Customer c)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Customers (Name, Phone, Email, Notes, LoyaltyPoints, IsActive, CreatedAt)
            VALUES (@n, @p, @e, @notes, @lp, 1, @ca);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@n", c.Name);
        cmd.Parameters.AddWithValue("@p", c.Phone ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@e", c.Email ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", c.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@lp", c.LoyaltyPoints);
        cmd.Parameters.AddWithValue("@ca", c.CreatedAt.ToString("o"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(Customer c)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Customers SET Name=@n, Phone=@p, Email=@e, Notes=@notes, LoyaltyPoints=@lp WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@n", c.Name);
        cmd.Parameters.AddWithValue("@p", c.Phone ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@e", c.Email ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", c.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@lp", c.LoyaltyPoints);
        cmd.Parameters.AddWithValue("@id", c.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Customer Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        LegacyId = r.IsDBNull(r.GetOrdinal("LegacyId")) ? null : r.GetInt32(r.GetOrdinal("LegacyId")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Phone = r.IsDBNull(r.GetOrdinal("Phone")) ? null : r.GetString(r.GetOrdinal("Phone")),
        Email = r.IsDBNull(r.GetOrdinal("Email")) ? null : r.GetString(r.GetOrdinal("Email")),
        Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
        LoyaltyPoints = r.GetInt32(r.GetOrdinal("LoyaltyPoints")),
        IsActive = r.GetInt32(r.GetOrdinal("IsActive")) == 1,
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
    };
}
