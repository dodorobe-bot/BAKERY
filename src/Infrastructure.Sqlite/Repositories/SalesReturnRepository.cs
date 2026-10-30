using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class SalesReturnRepository : ISalesReturnRepository
{
    private readonly DbConnectionFactory _factory;
    public SalesReturnRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<SalesReturnView>> GetAllAsync(int limit = 200)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT r.Id, r.ReturnNumber, r.SalesOrderId, so.OrderNumber,
                   COALESCE(so.CustomerNameSnapshot, '') AS CustomerName,
                   r.ProductTemplateId, pt.Name AS ProductName,
                   r.Quantity, r.Amount, r.Reason, r.RefundMethod, r.ReturnedAt, u.Username
            FROM SalesReturns r
            JOIN SalesOrders so ON so.Id = r.SalesOrderId
            LEFT JOIN ProductTemplates pt ON pt.Id = r.ProductTemplateId
            LEFT JOIN Users u ON u.Id = r.CreatedByUserId
            ORDER BY r.ReturnedAt DESC, r.Id DESC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<SalesReturnView>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SalesReturnView(
                r.GetInt32(0), r.GetString(1), r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                Convert.ToDecimal(r.GetValue(7)), r.GetInt64(8),
                r.GetString(9), r.GetString(10), BusinessTime.BacaCapWaktu(r.GetString(11)),
                r.IsDBNull(12) ? null : r.GetString(12)));
        }
        return list;
    }

    public async Task<(decimal Quantity, long Amount)> GetReturnedTotalsAsync(int salesOrderId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(Quantity), 0), COALESCE(SUM(Amount), 0)
            FROM SalesReturns WHERE SalesOrderId = @id;";
        cmd.Parameters.AddWithValue("@id", salesOrderId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (0, 0);
        return (Convert.ToDecimal(r.GetValue(0)), r.GetInt64(1));
    }

    public Task<int> CreateWithJournalAsync(
        SalesReturn ret, JournalEntry? journal, IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO SalesReturns
                    (ReturnNumber, SalesOrderId, ProductTemplateId, Quantity, Amount, Reason,
                     RefundMethod, ReturnedAt, IdempotencyKey, CreatedByUserId, CreatedAt)
                VALUES (@number, @orderId, @productId, @qty, @amount, @reason,
                        @method, @returnedAt, @key, @userId, datetime('now'));
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@number", ret.ReturnNumber);
            cmd.Parameters.AddWithValue("@orderId", ret.SalesOrderId);
            cmd.Parameters.AddWithValue("@productId", ret.ProductTemplateId);
            cmd.Parameters.AddWithValue("@qty", ret.Quantity);
            cmd.Parameters.AddWithValue("@amount", ret.Amount);
            cmd.Parameters.AddWithValue("@reason", ret.Reason);
            cmd.Parameters.AddWithValue("@method", ret.RefundMethod);
            cmd.Parameters.AddWithValue("@returnedAt", ret.ReturnedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@key", ret.IdempotencyKey ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@userId", ret.CreatedByUserId ?? (object)DBNull.Value);
            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            // Jurnal pembalik pendapatan diberi nomor retur sebagai dokumen sumber, supaya
            // pertanyaan "kenapa kas berkurang?" bisa dijawab dari nomor yang tercetak.
            if (journal is not null)
            {
                journal.SourceDocumentId ??= id;
                var journalId = await JournalWriter.WriteAsync(conn, tx, journal);
                using var tautkan = conn.CreateCommand();
                tautkan.Transaction = tx;
                tautkan.CommandText = "UPDATE SalesReturns SET JournalEntryId = @je WHERE Id = @id;";
                tautkan.Parameters.AddWithValue("@je", journalId);
                tautkan.Parameters.AddWithValue("@id", id);
                await tautkan.ExecuteNonQueryAsync();
            }

            foreach (var change in finishedGoodsChanges)
                await FinishedGoodsLedger.ApplyAsync(conn, tx, change.LocationId, change.ProductTemplateId,
                    change.Delta, change.Type, change.SourceType, change.SourceId ?? id,
                    change.UserId, change.Notes);

            return id;
        });
}
