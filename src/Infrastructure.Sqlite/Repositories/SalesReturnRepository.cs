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

    public async Task<(decimal Quantity, long Amount, long Discount, long Tax)> GetReturnedTotalsAsync(int salesOrderId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(Quantity), 0), COALESCE(SUM(Amount), 0),
                   COALESCE(SUM(DiscountAmount), 0), COALESCE(SUM(TaxAmount), 0)
            FROM SalesReturns WHERE SalesOrderId = @id;";
        cmd.Parameters.AddWithValue("@id", salesOrderId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (0, 0, 0, 0);
        return (Convert.ToDecimal(r.GetValue(0)), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
    }

    public Task<int> CreateWithJournalAsync(
        SalesReturn ret, JournalEntry? journal, IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            // Status dan sisa retur dibaca ulang di dalam transaksi. Tanpa itu, pesanan yang
            // dibatalkan bersamaan masih bisa diretur (uang kembali dua kali), dan dua retur
            // bersamaan bisa mengembalikan lebih dari jumlah yang dijual.
            var state = await ReadOrderReturnStateAsync(conn, tx, ret.SalesOrderId)
                ?? throw new InvalidOperationException("Pesanan tidak ditemukan.");
            if (state.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "Pesanan sudah dibatalkan; stok dan dananya sudah dikembalikan lewat pembatalan.");
            if (state.Status != OrderStatus.Paid)
                throw new InvalidOperationException(
                    "Retur hanya untuk pesanan yang sudah lunas. Untuk pesanan berjalan, gunakan pembatalan pesanan.");

            var sisaJumlah = state.Quantity - state.ReturnedQuantity;
            if (ret.Quantity > sisaJumlah)
                throw new InvalidOperationException(
                    $"Jumlah retur melebihi sisa yang bisa diretur ({sisaJumlah:0.###} unit). Muat ulang pesanan lalu ulangi.");
            var sisaNilai = state.TotalAmount - state.ReturnedAmount;
            if (ret.Amount > sisaNilai)
                throw new InvalidOperationException(
                    "Nilai retur melebihi sisa nilai pesanan. Muat ulang pesanan lalu ulangi.");
            // Potongan dan pajak mengikuti nilai yang diretur, dan batasnya diperiksa di dalam
            // transaksi: dua retur bersamaan tidak boleh membalik PPN lebih besar daripada yang
            // pernah dipungut pada pesanan itu.
            var sisaDiskon = state.DiscountAmount - state.ReturnedDiscount;
            var sisaPajak = state.TaxAmount - state.ReturnedTax;
            if (ret.DiscountAmount > sisaDiskon || ret.TaxAmount > sisaPajak)
                throw new InvalidOperationException(
                    "Diskon atau PPN retur melebihi sisa pesanan. Muat ulang pesanan lalu ulangi.");

            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO SalesReturns
                    (ReturnNumber, SalesOrderId, ProductTemplateId, Quantity, Amount,
                     DiscountAmount, TaxAmount, Reason,
                     RefundMethod, ReturnedAt, IdempotencyKey, CreatedByUserId, CreatedAt)
                VALUES (@number, @orderId, @productId, @qty, @amount,
                        @discount, @tax, @reason,
                        @method, @returnedAt, @key, @userId, datetime('now'));
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@number", ret.ReturnNumber);
            cmd.Parameters.AddWithValue("@orderId", ret.SalesOrderId);
            cmd.Parameters.AddWithValue("@productId", ret.ProductTemplateId);
            cmd.Parameters.AddWithValue("@qty", ret.Quantity);
            cmd.Parameters.AddWithValue("@amount", ret.Amount);
            cmd.Parameters.AddWithValue("@discount", ret.DiscountAmount);
            cmd.Parameters.AddWithValue("@tax", ret.TaxAmount);
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
            {
                var applied = await FinishedGoodsLedger.ApplyAsync(conn, tx, change.LocationId, change.ProductTemplateId,
                    change.Delta, change.Type, change.SourceType, change.SourceId ?? id,
                    change.UserId, change.Notes, change.UnitCostMinorUnits);
                // Barang kembali ke rak dengan nilai HPP saat terjual, dan beban pokok penjualan
                // ikut dikurangi supaya laba retur tidak dihitung dua kali.
                if (change.Type == FinishedGoodsMovementType.Return)
                    await AccountingPoster.WriteCostOfGoodsReturnedAsync(conn, tx, id,
                        applied.ValueMinorUnits, change.UserId);
            }

            return id;
        });

    private sealed record ReturnState(
        OrderStatus Status, decimal Quantity, long TotalAmount,
        decimal ReturnedQuantity, long ReturnedAmount,
        long DiscountAmount, long ReturnedDiscount, long TaxAmount, long ReturnedTax);

    private static async Task<ReturnState?> ReadOrderReturnStateAsync(
        SqliteConnection conn, SqliteTransaction tx, int orderId)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT o.Status, o.Quantity, o.TotalAmount,
                   COALESCE((SELECT SUM(r.Quantity) FROM SalesReturns r WHERE r.SalesOrderId = o.Id), 0),
                   COALESCE((SELECT SUM(r.Amount) FROM SalesReturns r WHERE r.SalesOrderId = o.Id), 0),
                   o.DiscountAmount,
                   COALESCE((SELECT SUM(r.DiscountAmount) FROM SalesReturns r WHERE r.SalesOrderId = o.Id), 0),
                   o.TaxAmount,
                   COALESCE((SELECT SUM(r.TaxAmount) FROM SalesReturns r WHERE r.SalesOrderId = o.Id), 0)
            FROM SalesOrders o WHERE o.Id = @id;";
        cmd.Parameters.AddWithValue("@id", orderId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new ReturnState(
            Enum.Parse<OrderStatus>(r.GetString(0)),
            Convert.ToDecimal(r.GetValue(1)),
            r.GetInt64(2),
            Convert.ToDecimal(r.GetValue(3)),
            r.GetInt64(4),
            r.GetInt64(5),
            r.GetInt64(6),
            r.GetInt64(7),
            r.GetInt64(8));
    }
}
