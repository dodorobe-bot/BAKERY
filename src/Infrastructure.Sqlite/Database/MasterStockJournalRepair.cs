using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace Seruni22Bakery.Infrastructure.Sqlite.Database;

/// <summary>Satu bahan yang stoknya diketik dari layar Bahan Baku tanpa jurnal.</summary>
public sealed record GapStokBahan(int IngredientId, string Name, string Unit, decimal Quantity, long AmountMinor);

/// <summary>
/// Menambal stok yang lahir sebelum jalur Bahan Baku ikut membukukan jurnalnya.
///
/// Alasan bukan migrasi: nilai ini masuk ke neraca (Debet persediaan / Kredit Modal), dan
/// memutuskan bahwa stok lama memang "modal" adalah keputusan pemilik, bukan keputusan teknis
/// yang boleh diambil diam-diam saat aplikasi naik versi. Perintah ini karena itu harus dijalankan
/// sendiri, dan tanpa <c>--terapkan</c> ia hanya membaca.
///
/// Bahan yang sudah punya jurnal IngredientMaster dilewati: pasca-perbaikan, layar bahan baku
/// membukukan setiap perubahan, jadi tidak ada lagi celah yang perlu ditambal di sana.
/// </summary>
public static class MasterStockJournalRepair
{
    private const string QueryGap = @"
        SELECT s.IngredientId, i.Name, i.Unit, s.qty,
               CAST(ROUND(s.qty * i.PricePerUnit) AS INTEGER) AS nilai
          FROM (SELECT m.IngredientId, SUM(m.Quantity) AS qty
                  FROM StockMovements m
                 WHERE m.SourceType = 'IngredientMaster'
                 GROUP BY m.IngredientId) s
          JOIN Ingredients i ON i.Id = s.IngredientId
         WHERE ROUND(s.qty * i.PricePerUnit) <> 0
           AND NOT EXISTS (SELECT 1 FROM JournalEntries je
                            WHERE je.SourceDocumentType = 'IngredientMaster'
                              AND je.SourceDocumentId = s.IngredientId)
         ORDER BY i.Name;";

    public static async Task<IReadOnlyList<GapStokBahan>> CariGapAsync(DbConnectionFactory factory)
    {
        using var conn = factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = QueryGap;
        var hasil = new List<GapStokBahan>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            hasil.Add(new GapStokBahan(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetInt64(4)));
        return hasil;
    }

    /// <summary>
    /// Membukukan seluruh celah dalam satu transaksi. Nilai memakai harga yang berlaku sekarang
    /// karena stok warisan tidak menyimpan harga saat ia diketik — ini sengaja sama dengan cara
    /// layar Bahan Baku menghitung nilainya hari ini.
    /// </summary>
    public static async Task<int> TerapkanAsync(
        DbConnectionFactory factory, int? userId, Action<string>? log = null)
    {
        var gap = await CariGapAsync(factory);
        if (gap.Count == 0) return 0;

        return await WriteTransaction.RunAsync(factory, async (conn, tx) =>
        {
            using var lokasiCmd = conn.CreateCommand();
            lokasiCmd.Transaction = tx;
            lokasiCmd.CommandText =
                "SELECT Id FROM StorageLocations WHERE Type = 'Warehouse' ORDER BY Id LIMIT 1;";
            var lokasiId = await lokasiCmd.ExecuteScalarAsync();
            if (lokasiId is null or DBNull)
                throw new InvalidOperationException(
                    "Lokasi bertipe Warehouse tidak ditemukan; jurnal persediaan bahan tidak bisa ditulis.");
            var gudang = Convert.ToInt32(lokasiId);

            var ditulis = 0;
            foreach (var bahan in gap)
            {
                await AccountingPoster.WriteIngredientMasterStockAsync(conn, tx, gudang,
                    bahan.IngredientId, bahan.Quantity, Math.Abs(bahan.AmountMinor), stokAwal: true, userId);
                log?.Invoke($"dibukukan: {bahan.Name} {bahan.Quantity} {bahan.Unit} = {bahan.AmountMinor} sen");
                ditulis++;
            }
            return ditulis;
        });
    }
}
