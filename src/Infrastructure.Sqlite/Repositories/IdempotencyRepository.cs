using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class IdempotencyRepository : IIdempotencyRepository
{
    // Pesanan yang tertinggal oleh proses yang mati di tengah jalan tidak boleh mengunci niat
    // itu selamanya: pengguna akan terus mendapat 409 walau tidak ada aksi yang berjalan.
    private const int MenitPesananBasi = 30;

    // Kunci selesai hanya berguna selama klien masih mungkin mengulang permintaan yang sama.
    // Setelah itu barisnya dibersihkan supaya tabel tidak tumbuh tanpa batas.
    private const int HariSimpanSelesai = 30;

    private readonly DbConnectionFactory _factory;
    public IdempotencyRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IdempotencyRecord?> GetAsync(string key)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Basi dihitung di SQL (UTC yang sama dengan ReservedAt), bukan di proses: jam lokal
        // perangkat toko tidak boleh menggeser batas ini.
        cmd.CommandText = @"
            SELECT Key, Route, UserId, ResourceId, ReservedAt, CompletedAt,
                   CASE WHEN CompletedAt IS NULL AND ReservedAt < datetime('now', @basi)
                        THEN 1 ELSE 0 END
            FROM IdempotencyKeys WHERE Key = @k;";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@basi", $"-{MenitPesananBasi} minutes");
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        // Baris basi diperlakukan seperti belum ada; ReserveAsync yang menghapusnya sebelum
        // menulis baris baru. Tanpa ini pengambilalihan tidak pernah tercapai.
        if (r.GetInt32(6) == 1) return null;
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
        await BersihkanAsync(conn);
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

    /// <summary>
    /// Buang pesanan basi (proses berhenti sebelum menutup kunci) dan kunci selesai yang sudah
    /// melewati masa simpan. Dijalankan sesaat sebelum kunci baru ditulis, jadi biayanya hanya
    /// satu DELETE ber-indeks per aksi tulis, bukan pemindaian latar yang bersaing dengan transaksi.
    /// </summary>
    private static async Task BersihkanAsync(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM IdempotencyKeys
             WHERE (CompletedAt IS NULL AND ReservedAt < datetime('now', @basi))
                OR (CompletedAt IS NOT NULL AND ReservedAt < datetime('now', @simpan));";
        cmd.Parameters.AddWithValue("@basi", $"-{MenitPesananBasi} minutes");
        cmd.Parameters.AddWithValue("@simpan", $"-{HariSimpanSelesai} days");
        await cmd.ExecuteNonQueryAsync();
    }
}
