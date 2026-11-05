using Microsoft.Data.Sqlite;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Lapisan biaya persediaan: tiap barang masuk disimpan sebagai satu baris dengan harga
/// satuan aslinya, dan barang keluar menghabiskan lapisan tertua lebih dulu (FIFO).
///
/// Ini memperbaiki BUG-032. Sebelum ada lapisan, nilai barang keluar dihitung ulang dari
/// harga master yang sedang terpasang, jadi jurnal boleh membukukan Rp 600 untuk barang yang
/// dibeli Rp 500 — stok fisik dan saldo akun lalu tidak lagi menjelaskan barang yang sama.
/// Dengan lapisan, nominal yang masuk ke 5-005/5-006 selalu harga barang yang benar-benar
/// berpindah, dan pemeriksaan "saldo akun = nilai stok" bisa dijawab tanpa toleransi.
///
/// Urutan FIFO memakai Id baris, bukan tanggal, karena CreatedAt hanya presisi sampai detik:
/// dua penerimaan pada detik yang sama harus tetap punya urutan keluar yang pasti.
/// </summary>
internal static class CostLayerLedger
{
    public const string Bahan = "Ingredient";
    public const string ProdukJadi = "FinishedGoods";

    /// <summary>Quantity disimpan sebagai REAL; sisa di bawah ini dianggap nol.</summary>
    private const decimal Epsilon = 0.0000001m;

    /// <summary>Memasukkan barang baru. Mengembalikan nilai yang menempel padanya (sen).</summary>
    public static async Task<long> TambahAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        string itemKind, int itemId, int locationId, decimal quantity, long unitCostMinorUnits,
        string sourceType, int? sourceId)
    {
        if (quantity <= Epsilon) return 0;
        if (unitCostMinorUnits < 0)
            throw new InvalidOperationException("Biaya per satuan lapisan stok tidak boleh negatif.");

        await SisipAsync(conn, tx, itemKind, itemId, locationId, quantity, unitCostMinorUnits,
            sourceType, sourceId);
        return Bulat(quantity * unitCostMinorUnits);
    }

    /// <summary>
    /// Mengeluarkan barang dari lapisan tertua. Nilai yang dikembalikan adalah harga lapisan
    /// yang benar-benar terpakai, jadi pemanggil menulis jurnal dari angka ini, bukan dari
    /// kali-ulang harga lain.
    /// </summary>
    public static async Task<long> KeluarAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        string itemKind, int itemId, int locationId, decimal quantity, string label)
    {
        if (quantity <= Epsilon) return 0;

        decimal sisa = quantity;
        long nilai = 0;

        foreach (var layer in await LapisanTertuaAsync(conn, tx, itemKind, itemId, locationId))
        {
            if (sisa <= Epsilon) break;
            var ambil = Math.Min(sisa, layer.Quantity);
            nilai += Bulat(ambil * layer.UnitCostMinorUnits);
            await KurangiAsync(conn, tx, layer.Id, layer.Quantity, ambil);
            sisa -= ambil;
        }

        if (sisa > Epsilon)
            throw new InvalidOperationException(
                $"Lapisan biaya {label} di lokasi ini tinggal {quantity - sisa:0.###} dari " +
                $"{quantity:0.###} yang harus keluar. Stok fisiknya ada, tetapi tidak ada baris " +
                "lapisan yang menjelaskan harga barangnya — jalankan Periksa Basis Data, lalu " +
                "bukukan selisihnya lewat Stok Opname.");

        return nilai;
    }

    /// <summary>
    /// Memindahkan barang antar lokasi berikut harga aslinya: lapisan asal mengecil, lapisan
    /// tujuan lahir dengan biaya per satuan yang sama. Ini yang membuat transfer gudang ke
    /// dapur tidak mengubah nilai persediaan, hanya tempatnya.
    /// </summary>
    public static async Task<long> PindahkanAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        string itemKind, int itemId, int fromLocationId, int toLocationId,
        decimal quantity, string label, string sourceType, int? sourceId)
    {
        if (quantity <= Epsilon) return 0;
        if (fromLocationId == toLocationId)
            throw new InvalidOperationException("Lokasi asal dan tujuan lapisan harus berbeda.");

        decimal sisa = quantity;
        long nilai = 0;

        foreach (var layer in await LapisanTertuaAsync(conn, tx, itemKind, itemId, fromLocationId))
        {
            if (sisa <= Epsilon) break;
            var ambil = Math.Min(sisa, layer.Quantity);
            nilai += Bulat(ambil * layer.UnitCostMinorUnits);
            await KurangiAsync(conn, tx, layer.Id, layer.Quantity, ambil);
            await SisipAsync(conn, tx, itemKind, itemId, toLocationId, ambil,
                layer.UnitCostMinorUnits, sourceType, sourceId);
            sisa -= ambil;
        }

        if (sisa > Epsilon)
            throw new InvalidOperationException(
                $"Lapisan biaya {label} di lokasi asal tinggal {quantity - sisa:0.###} dari " +
                $"{quantity:0.###} yang ditransfer. Jalankan Periksa Basis Data sebelum memindahkan " +
                "barang supaya harga yang ikut berpindah benar.");

        return nilai;
    }

    /// <summary>Total kuantitas dan nilai lapisan yang tersisa di satu lokasi untuk satu item.</summary>
    public static async Task<(decimal Quantity, long Value)> RingkasAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        string itemKind, int itemId, int locationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Nilai ditotal per lapisan, sama seperti saat barang keluar, supaya angka layar dan
        // angka jurnal berasal dari penjumlahan yang identik.
        cmd.CommandText = @"
            SELECT COALESCE(SUM(Quantity), 0),
                   COALESCE(SUM(CAST(ROUND(Quantity * UnitCostMinorUnits) AS INTEGER)), 0)
              FROM StockCostLayers
             WHERE ItemKind = @k AND ItemId = @i AND LocationId = @l;";
        cmd.Parameters.AddWithValue("@k", itemKind);
        cmd.Parameters.AddWithValue("@i", itemId);
        cmd.Parameters.AddWithValue("@l", locationId);

        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var quantity = reader.GetDecimal(0);
        var value = reader.GetInt64(1);
        return (quantity, value);
    }

    private record Layer(int Id, decimal Quantity, long UnitCostMinorUnits);

    private static async Task<List<Layer>> LapisanTertuaAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        string itemKind, int itemId, int locationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT Id, Quantity, UnitCostMinorUnits FROM StockCostLayers
             WHERE ItemKind = @k AND ItemId = @i AND LocationId = @l AND Quantity > 0
             ORDER BY Id;";
        cmd.Parameters.AddWithValue("@k", itemKind);
        cmd.Parameters.AddWithValue("@i", itemId);
        cmd.Parameters.AddWithValue("@l", locationId);

        var layers = new List<Layer>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            layers.Add(new Layer(reader.GetInt32(0), reader.GetDecimal(1), reader.GetInt64(2)));
        return layers;
    }

    /// <summary>
    /// Menyisip lapisan baru, atau menambah isinya ke lapisan termuda yang harganya sama.
    /// Tanpa penggabungan ini, SPK yang diproduksi tiap hari membuat ribuan baris berharga
    /// identik yang tidak menambah informasi apa pun.
    /// </summary>
    private static async Task SisipAsync(
        SqliteConnection conn, SqliteTransaction? tx,
        string itemKind, int itemId, int locationId, decimal quantity, long unitCostMinorUnits,
        string sourceType, int? sourceId)
    {
        using var find = conn.CreateCommand();
        find.Transaction = tx;
        find.CommandText = @"
            SELECT Id FROM StockCostLayers
             WHERE ItemKind = @k AND ItemId = @i AND LocationId = @l
               AND UnitCostMinorUnits = @c AND Quantity > 0
             ORDER BY Id DESC LIMIT 1;";
        find.Parameters.AddWithValue("@k", itemKind);
        find.Parameters.AddWithValue("@i", itemId);
        find.Parameters.AddWithValue("@l", locationId);
        find.Parameters.AddWithValue("@c", unitCostMinorUnits);
        var existing = await find.ExecuteScalarAsync();

        if (existing is not null and not DBNull)
        {
            using var grow = conn.CreateCommand();
            grow.Transaction = tx;
            grow.CommandText = @"
                UPDATE StockCostLayers
                   SET Quantity = Quantity + @q, UpdatedAt = datetime('now')
                 WHERE Id = @id;";
            grow.Parameters.AddWithValue("@q", quantity);
            grow.Parameters.AddWithValue("@id", Convert.ToInt32(existing));
            await grow.ExecuteNonQueryAsync();
            return;
        }

        using var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"
            INSERT INTO StockCostLayers
                (ItemKind, ItemId, LocationId, Quantity, UnitCostMinorUnits,
                 SourceType, SourceId, CreatedAt, UpdatedAt)
            VALUES (@k, @i, @l, @q, @c, @st, @si, datetime('now'), datetime('now'));";
        insert.Parameters.AddWithValue("@k", itemKind);
        insert.Parameters.AddWithValue("@i", itemId);
        insert.Parameters.AddWithValue("@l", locationId);
        insert.Parameters.AddWithValue("@q", quantity);
        insert.Parameters.AddWithValue("@c", unitCostMinorUnits);
        insert.Parameters.AddWithValue("@st", sourceType);
        insert.Parameters.AddWithValue("@si", sourceId ?? (object)DBNull.Value);
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>Mengurangi atau menghapus satu lapisan. Sisa pembulatan float dihapus, bukan dibiarkan jadi baris hantu.</summary>
    private static async Task KurangiAsync(
        SqliteConnection conn, SqliteTransaction? tx, int layerId, decimal layerQuantity, decimal taken)
    {
        var sisa = layerQuantity - taken;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("@id", layerId);

        if (sisa <= Epsilon)
        {
            cmd.CommandText = "DELETE FROM StockCostLayers WHERE Id = @id;";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE StockCostLayers
                   SET Quantity = @sisa, UpdatedAt = datetime('now')
                 WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@sisa", sisa);
        }
        await cmd.ExecuteNonQueryAsync();
    }

    private static long Bulat(decimal value) =>
        (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
