using Microsoft.Data.Sqlite;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Pola tulis satu transaksi: satu koneksi, satu transaksi, semua langkah masuk bersama
/// atau batal bersama. Galat constraint dari basis data diterjemahkan menjadi kalimat kerja
/// agar layar bisa menolak dengan 400, bukan menampilkan 500 tanpa arti.
/// </summary>
internal static class WriteTransaction
{
    public static async Task RunAsync(
        DbConnectionFactory factory, Func<SqliteConnection, SqliteTransaction, Task> work)
    {
        await RunAsync<object>(factory, async (conn, tx) =>
        {
            await work(conn, tx);
            return 0;
        });
    }

    public static async Task<T> RunAsync<T>(
        DbConnectionFactory factory, Func<SqliteConnection, SqliteTransaction, Task<T>> work)
    {
        using var conn = factory.CreateConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var result = await work(conn, tx);
            tx.Commit();
            return result;
        }
        catch (SqliteException ex)
        {
            tx.Rollback();
            throw new InvalidOperationException(Explain(ex), ex);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static string Explain(SqliteException ex)
    {
        if (ex.Message.Contains("IdempotencyKey"))
            return "Transaksi dengan kunci yang sama sudah pernah dicatat.";
        if (ex.Message.Contains("FOREIGN KEY"))
            return "Data rujukan tidak ditemukan (akun, bahan, lokasi, atau dokumen), perubahan dibatalkan.";
        if (ex.Message.Contains("OrderNumber") || ex.Message.Contains("PoNumber"))
            return "Nomor dokumen sudah dipakai oleh dokumen lain, perubahan dibatalkan.";
        if (ex.Message.Contains("QualityChecks.WorkOrderId"))
            return "SPK ini sudah diperiksa mutunya.";
        if (ex.Message.Contains("SalesReturns.ReturnNumber"))
            return "Nomor retur sudah dipakai oleh dokumen lain, perubahan dibatalkan.";

        // Pemicu RAISE(ABORT, 'kalimat') muncul sebagai SQLite Error 19: 'kalimat'.
        var first = ex.Message.IndexOf('\'');
        var last = ex.Message.LastIndexOf('\'');
        return first >= 0 && last > first
            ? ex.Message.Substring(first + 1, last - first - 1)
            : ex.Message;
    }
}
