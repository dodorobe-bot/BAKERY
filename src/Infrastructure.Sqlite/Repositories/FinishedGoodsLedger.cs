using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>Hasil satu perubahan stok produk jadi, termasuk nilainya saat keluar/masuk.</summary>
internal record FinishedGoodsApplied(decimal Quantity, long UnitCostMinorUnits, long ValueMinorUnits);

/// <summary>
/// Satu-satunya tempat SQL yang mengubah stok produk jadi dan mencatat mutasinya, meniru
/// StockLedger untuk bahan mentah. Dipisahkan karena produk jadi tidak punya Id bahan dan
/// tidak boleh tertulis di baris LocationStock yang sama.
///
/// Barang keluar memakai lapisan biaya per batch produksi (FIFO), bukan rata-rata bergerak:
/// penjualan roti hasil SPK kemarin harus membebankan HPP SPK kemarin, bukan HPP hari ini
/// (BUG-034 / kasus E3a). AverageCostMinorUnits pada baris stok tetap dipelihara, sekarang
/// sebagai rata-rata tertimbang dari lapisan yang tersisa, supaya layar Katalog dan HPP masih
/// punya satu angka per produk dan saldo akun 1-006 selalu sama dengan nilai lapisan.
/// </summary>
internal static class FinishedGoodsLedger
{
    public static async Task<FinishedGoodsApplied> ApplyAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int locationId, int productTemplateId, decimal delta, FinishedGoodsMovementType type,
        string? sourceType, int? sourceId, int? userId, string? notes, long unitCostMinorUnits = 0)
    {
        if (delta == 0)
            return new FinishedGoodsApplied(0, 0, 0);

        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "SELECT Quantity FROM FinishedGoodsStock WHERE LocationId = @locId AND ProductTemplateId = @prodId;";
        check.Parameters.AddWithValue("@locId", locationId);
        check.Parameters.AddWithValue("@prodId", productTemplateId);

        decimal onHand = 0;
        using (var reader = await check.ExecuteReaderAsync())
            if (await reader.ReadAsync()) onHand = reader.GetDecimal(0);

        if (onHand + delta < 0)
            throw new InvalidOperationException(await BuildInsufficientStockMessageAsync(
                conn, tx, locationId, productTemplateId, onHand, delta));

        var lapisanDari = sourceType ?? type.ToString();
        long nilai;
        long biayaMutasi;

        if (delta < 0)
        {
            // Barang keluar menghabiskan lapisan tertua; nilainya adalah harga batch yang
            // benar-benar Leaving, dan itulah yang kemudian didebit ke 5-005.
            var produk = await NamaProdukAsync(conn, tx, productTemplateId);
            nilai = await CostLayerLedger.KeluarAsync(conn, tx, CostLayerLedger.ProdukJadi,
                productTemplateId, locationId, -delta, produk);
            biayaMutasi = Bulat((decimal)nilai / -delta);
        }
        else
        {
            // Barang masuk membawa biayanya sendiri (hasil produksi, retur, pembatalan).
            // Bila tidak ada — misalnya koreksi stok dari layar — pakai rata-rata lapisan yang
            // sedang berlaku supaya penyesuaian tidak menghapus nilai barang.
            var (sisaSebelum, nilaiSebelum) = await CostLayerLedger.RingkasAsync(
                conn, tx, CostLayerLedger.ProdukJadi, productTemplateId, locationId);
            var harga = unitCostMinorUnits > 0
                ? unitCostMinorUnits
                : sisaSebelum > 0 ? Bulat((decimal)nilaiSebelum / sisaSebelum) : 0;

            nilai = await CostLayerLedger.TambahAsync(conn, tx, CostLayerLedger.ProdukJadi,
                productTemplateId, locationId, delta, harga, lapisanDari, sourceId);
            biayaMutasi = harga;
        }

        var (sisaQty, sisaNilai) = await CostLayerLedger.RingkasAsync(
            conn, tx, CostLayerLedger.ProdukJadi, productTemplateId, locationId);
        var newAverageCost = sisaQty > 0 ? Bulat((decimal)sisaNilai / sisaQty) : 0;

        // Sama seperti StockLedger: UPDATE dulu, sisip hanya bila barisnya belum ada, karena
        // SQLite menilai CHECK atas nilai yang akan disisipkan sebelum resolusi konflik.
        using var upsert = conn.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = @"
            UPDATE FinishedGoodsStock
               SET Quantity = Quantity + @delta,
                   AverageCostMinorUnits = @avgCost,
                   UpdatedAt = datetime('now')
             WHERE LocationId = @locId AND ProductTemplateId = @prodId;
            INSERT INTO FinishedGoodsStock (LocationId, ProductTemplateId, Quantity, AverageCostMinorUnits, UpdatedAt)
            SELECT @locId, @prodId, @delta, @avgCost, datetime('now')
             WHERE NOT EXISTS (
                SELECT 1 FROM FinishedGoodsStock WHERE LocationId = @locId AND ProductTemplateId = @prodId);";
        upsert.Parameters.AddWithValue("@locId", locationId);
        upsert.Parameters.AddWithValue("@prodId", productTemplateId);
        upsert.Parameters.AddWithValue("@delta", delta);
        upsert.Parameters.AddWithValue("@avgCost", newAverageCost);
        await upsert.ExecuteNonQueryAsync();

        using var ledger = conn.CreateCommand();
        ledger.Transaction = tx;
        ledger.CommandText = @"
            INSERT INTO FinishedGoodsMovements (
                ProductTemplateId, LocationId, MovementType, Quantity, UnitCostMinorUnits,
                ValueMinorUnits, SourceType, SourceId, Notes, CreatedAt, CreatedByUserId
            ) VALUES (@prodId, @locId, @mt, @delta, @unitCost, @value, @st, @si, @notes, datetime('now'), @userId);";
        ledger.Parameters.AddWithValue("@prodId", productTemplateId);
        ledger.Parameters.AddWithValue("@locId", locationId);
        ledger.Parameters.AddWithValue("@mt", type.ToString());
        ledger.Parameters.AddWithValue("@delta", delta);
        ledger.Parameters.AddWithValue("@unitCost", biayaMutasi);
        ledger.Parameters.AddWithValue("@value", nilai);
        ledger.Parameters.AddWithValue("@st", sourceType ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@si", sourceId ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@notes", notes ?? (object)DBNull.Value);
        ledger.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
        await ledger.ExecuteNonQueryAsync();

        return new FinishedGoodsApplied(delta, biayaMutasi, nilai);
    }

    private static long Bulat(decimal value) =>
        (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static async Task<string> NamaProdukAsync(
        SqliteConnection conn, SqliteTransaction? tx, int productTemplateId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Name FROM ProductTemplates WHERE Id = @prodId;";
        cmd.Parameters.AddWithValue("@prodId", productTemplateId);
        return (string?)await cmd.ExecuteScalarAsync() ?? $"produk #{productTemplateId}";
    }

    private static async Task<string> BuildInsufficientStockMessageAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        int locationId, int productTemplateId, decimal onHand, decimal delta)
    {
        using var nameCmd = conn.CreateCommand();
        nameCmd.Transaction = tx;
        nameCmd.CommandText = @"
            SELECT sl.Name, pt.Name
            FROM StorageLocations sl, ProductTemplates pt
            WHERE sl.Id = @locId AND pt.Id = @prodId;";
        nameCmd.Parameters.AddWithValue("@locId", locationId);
        nameCmd.Parameters.AddWithValue("@prodId", productTemplateId);
        using var reader = await nameCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return $"Stok produk jadi tidak mencukupi: tersedia {onHand}, perubahan diminta {delta}.";
        return $"Stok produk {reader.GetString(1)} di {reader.GetString(0)} tidak cukup: " +
               $"tersedia {onHand}, dibutuhkan {-delta}.";
    }
}
