using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Jurnal yang nilainya baru diketahui di dalam transaksi stok — terutama harga pokok
/// penjualan, yang memakai biaya rata-rata pada saat barang keluar. Ditulis lewat
/// JournalWriter yang sama dengan jurnal lain supaya pagar keseimbangan tetap berlaku.
/// </summary>
internal static class AccountingPoster
{
    private const string PersediaanBahanKode = "1-003";
    private const string PersediaanDapurKode = "1-004";
    private const string PersediaanJadiKode = "1-006";
    private const string BebanPokokPenjualanKode = "5-005";
    private const string BebanSelisihPersediaanKode = "5-006";

    /// <summary>Akun persediaan yang mewakili sebuah lokasi penyimpanan.</summary>
    public static async Task<string> InventoryCodeForLocationAsync(
        SqliteConnection conn, SqliteTransaction? tx, int locationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Type FROM StorageLocations WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", locationId);
        var tipe = (string?)await cmd.ExecuteScalarAsync()
                   ?? throw new InvalidOperationException($"Lokasi penyimpanan Id {locationId} tidak ditemukan.");

        return tipe switch
        {
            "Warehouse" => PersediaanBahanKode,
            "Kitchen" => PersediaanDapurKode,
            "Display" => PersediaanJadiKode,
            _ => throw new InvalidOperationException($"Jenis lokasi penyimpanan tidak dikenal: {tipe}.")
        };
    }

    public static async Task<int> AccountIdAsync(
        SqliteConnection conn, SqliteTransaction? tx, string code)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id FROM Accounts WHERE Code = @code AND IsActive = 1;";
        cmd.Parameters.AddWithValue("@code", code);
        var id = await cmd.ExecuteScalarAsync();
        return id is null or DBNull
            ? throw new InvalidOperationException($"Akun {code} tidak ditemukan atau tidak aktif.")
            : Convert.ToInt32(id);
    }

    /// <summary>Menulis jurnal dua kaki bila nominalnya bermakna.</summary>
    public static async Task WriteAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        string debitCode, string creditCode, long amount,
        string description, string sourceType, int? sourceId, int? userId)
    {
        if (amount <= 0 || debitCode == creditCode) return;

        var entry = new JournalEntry
        {
            Description = description,
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = sourceType,
            SourceDocumentId = sourceId,
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines =
            {
                new() { AccountId = await AccountIdAsync(conn, tx, debitCode), DebitAmount = amount },
                new() { AccountId = await AccountIdAsync(conn, tx, creditCode), CreditAmount = amount }
            }
        };
        await JournalWriter.WriteAsync(conn, tx, entry);
    }

    /// <summary>Dr Beban Pokok Penjualan / Cr Persediaan Produk Jadi.</summary>
    public static async Task WriteCostOfGoodsSoldAsync(
        SqliteConnection conn, SqliteTransaction? tx, int orderId, long amount, int? userId)
    {
        if (amount <= 0) return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT OrderNumber FROM SalesOrders WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", orderId);
        var nomor = (string?)await cmd.ExecuteScalarAsync() ?? $"#{orderId}";

        await WriteAsync(conn, tx, BebanPokokPenjualanKode, PersediaanJadiKode, amount,
            $"Harga Pokok Penjualan: {nomor}", "SalesOrder", orderId, userId);
    }

    /// <summary>Pembalik COGS saat retur: Dr Persediaan Produk Jadi / Cr Beban Pokok Penjualan.</summary>
    public static async Task WriteCostOfGoodsReturnedAsync(
        SqliteConnection conn, SqliteTransaction? tx, int returnId, long amount, int? userId)
    {
        if (amount <= 0) return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT ReturnNumber FROM SalesReturns WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", returnId);
        var nomor = (string?)await cmd.ExecuteScalarAsync() ?? $"#{returnId}";

        await WriteAsync(conn, tx, PersediaanJadiKode, BebanPokokPenjualanKode, amount,
            $"Pembalik Harga Pokok Penjualan Retur: {nomor}", "SalesReturn", returnId, userId);
    }

    /// <summary>Transfer antar lokasi: Dr persediaan tujuan / Cr persediaan asal.</summary>
    public static async Task WriteTransferAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int fromLocationId, int toLocationId, int ingredientId, decimal quantity,
        long amount, string? notes, int? userId)
    {
        if (amount <= 0 || fromLocationId == toLocationId) return;

        var asal = await InventoryCodeForLocationAsync(conn, tx, fromLocationId);
        var tujuan = await InventoryCodeForLocationAsync(conn, tx, toLocationId);

        var bahan = await IngredientLabelAsync(conn, tx, ingredientId);
        var keterangan = $"Transfer stok {bahan} ({quantity:0.###})";
        if (!string.IsNullOrWhiteSpace(notes)) keterangan += $": {notes.Trim()}";
        await WriteAsync(conn, tx, tujuan, asal, amount, keterangan,
            "LocationTransfer", fromLocationId, userId);
    }

    /// <summary>Opname: selisih menambah persediaan melawan beban selisih, atau sebaliknya.</summary>
    public static async Task WriteStockAdjustmentAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int locationId, int ingredientId, decimal delta, long amount,
        string? reason, int? userId)
    {
        if (amount <= 0 || delta == 0) return;

        var persediaan = await InventoryCodeForLocationAsync(conn, tx, locationId);
        var bahan = await IngredientLabelAsync(conn, tx, ingredientId);
        var keterangan = $"Stok opname {bahan} ({delta:+0.###;-0.###})";
        if (!string.IsNullOrWhiteSpace(reason)) keterangan += $": {reason.Trim()}";

        if (delta > 0)
            await WriteAsync(conn, tx, persediaan, BebanSelisihPersediaanKode, amount,
                keterangan, "StockAdjustment", locationId, userId);
        else
            await WriteAsync(conn, tx, BebanSelisihPersediaanKode, persediaan, amount,
                keterangan, "StockAdjustment", locationId, userId);
    }

    private static async Task<string> IngredientLabelAsync(
        SqliteConnection conn, SqliteTransaction? tx, int ingredientId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Name, Unit FROM Ingredients WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", ingredientId);
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? $"{reader.GetString(0)} ({reader.GetString(1)})" : $"bahan #{ingredientId}";
    }
}
