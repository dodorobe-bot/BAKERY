using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class PrintJobRepository : IPrintJobRepository
{
    private readonly DbConnectionFactory _factory;
    public PrintJobRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<int> CreateAsync(PrintJob job)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO PrintJobs
                (OrderId, Kind, PrinterName, PaperWidthMm, Status, Attempts, LastError, Payload,
                 CreatedAt, CreatedByUserId, FinishedAt)
            VALUES (@oid, @kind, @printer, @width, @status, @attempts, @err, @payload,
                    @created, @uid, @finished);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@oid", job.OrderId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@kind", job.Kind.ToString());
        cmd.Parameters.AddWithValue("@printer", job.PrinterName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@width", job.PaperWidthMm);
        cmd.Parameters.AddWithValue("@status", job.Status.ToString());
        cmd.Parameters.AddWithValue("@attempts", job.Attempts);
        cmd.Parameters.AddWithValue("@err", job.LastError ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@payload", job.Payload);
        cmd.Parameters.AddWithValue("@created", job.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@uid", job.CreatedByUserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@finished", job.FinishedAt?.ToString("o") ?? (object)DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task MarkPrintedAsync(int id, DateTime finishedAt)
    {
        await UpdateStatusAsync(id, PrintJobStatus.Printed, null, finishedAt);
    }

    public async Task MarkFailedAsync(int id, string error, DateTime finishedAt)
    {
        // Pesan galat dipotong supaya satu kegagalan driver yang panjang tidak memenuhi kolom;
        // percobaan tetap dihitung agar "sudah berapa kali dicoba" bisa dijawab.
        var pesan = error.Length > 1000 ? error[..1000] : error;
        await UpdateStatusAsync(id, PrintJobStatus.Failed, pesan, finishedAt);
    }

    private async Task UpdateStatusAsync(int id, PrintJobStatus status, string? error, DateTime finishedAt)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE PrintJobs
            SET Status = @status, LastError = @err, Attempts = Attempts + 1, FinishedAt = @finished
            WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@err", error ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@finished", finishedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<PrintJob>> GetRecentAsync(int limit = 50)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM PrintJobs ORDER BY CreatedAt DESC, Id DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<PrintJob>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public async Task<int> FailStalePendingAsync(string error, DateTime createdBefore)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE PrintJobs
            SET Status = 'Failed', LastError = @err, FinishedAt = @now
            WHERE Status = 'Pending' AND CreatedAt < @batas;";
        cmd.Parameters.AddWithValue("@err", error);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@batas", createdBefore.ToString("o"));
        return await cmd.ExecuteNonQueryAsync();
    }

    private static PrintJob Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        OrderId = r.IsDBNull(r.GetOrdinal("OrderId")) ? null : r.GetInt32(r.GetOrdinal("OrderId")),
        Kind = Enum.Parse<PrintJobKind>(r.GetString(r.GetOrdinal("Kind"))),
        PrinterName = r.IsDBNull(r.GetOrdinal("PrinterName")) ? null : r.GetString(r.GetOrdinal("PrinterName")),
        PaperWidthMm = r.GetInt32(r.GetOrdinal("PaperWidthMm")),
        Status = Enum.Parse<PrintJobStatus>(r.GetString(r.GetOrdinal("Status"))),
        Attempts = r.GetInt32(r.GetOrdinal("Attempts")),
        LastError = r.IsDBNull(r.GetOrdinal("LastError")) ? null : r.GetString(r.GetOrdinal("LastError")),
        Payload = r.GetString(r.GetOrdinal("Payload")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        CreatedByUserId = r.IsDBNull(r.GetOrdinal("CreatedByUserId")) ? null : r.GetInt32(r.GetOrdinal("CreatedByUserId")),
        FinishedAt = r.IsDBNull(r.GetOrdinal("FinishedAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("FinishedAt"))),
    };
}
