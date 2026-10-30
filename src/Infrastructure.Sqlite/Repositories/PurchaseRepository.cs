using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly DbConnectionFactory _factory;

    public SupplierRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IEnumerable<Supplier>> GetAllAsync()
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, ContactName, Phone, Email, Address, Notes, IsActive, CreatedAt FROM Suppliers WHERE IsActive = 1 ORDER BY Name;";

        var list = new List<Supplier>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapSupplier(reader));
        }
        return list;
    }

    public async Task<Supplier?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, ContactName, Phone, Email, Address, Notes, IsActive, CreatedAt FROM Suppliers WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapSupplier(reader);
    }

    public async Task<int> CreateAsync(Supplier supplier)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Suppliers (Name, ContactName, Phone, Email, Address, Notes, IsActive, CreatedAt)
            VALUES (@name, @contact, @phone, @email, @address, @notes, 1, datetime('now'));
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", supplier.Name);
        cmd.Parameters.AddWithValue("@contact", supplier.ContactName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@phone", supplier.Phone ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@email", supplier.Email ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@address", supplier.Address ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", supplier.Notes ?? (object)DBNull.Value);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(Supplier supplier)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Suppliers SET
                Name = @name,
                ContactName = @contact,
                Phone = @phone,
                Email = @email,
                Address = @address,
                Notes = @notes
            WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@name", supplier.Name);
        cmd.Parameters.AddWithValue("@contact", supplier.ContactName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@phone", supplier.Phone ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@email", supplier.Email ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@address", supplier.Address ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", supplier.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id", supplier.Id);

        await cmd.ExecuteNonQueryAsync();
    }

    private static Supplier MapSupplier(SqliteDataReader reader)
    {
        return new Supplier
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            ContactName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
            Email = reader.IsDBNull(4) ? null : reader.GetString(4),
            Address = reader.IsDBNull(5) ? null : reader.GetString(5),
            Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
            IsActive = reader.GetInt32(7) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(8))
        };
    }
}

public class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly DbConnectionFactory _factory;

    public PurchaseOrderRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IEnumerable<PurchaseOrder>> GetAllAsync()
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT po.Id, po.PoNumber, po.SupplierId, s.Name AS SupplierName,
                   po.Status, po.OrderDate, po.ExpectedDate, po.Notes,
                   po.CreatedByUserId, po.CreatedAt
            FROM PurchaseOrders po
            LEFT JOIN Suppliers s ON po.SupplierId = s.Id
            ORDER BY po.CreatedAt DESC;";

        var list = new List<PurchaseOrder>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                list.Add(MapPurchaseOrderHeader(reader));
            }
        }

        // Baris ikut dimuat: tanpa baris, nilai pembelian dan utang pemasok tidak bisa dihitung di layar.
        using var lineCmd = conn.CreateCommand();
        lineCmd.CommandText = PurchaseOrderLinesSql + @"
            ORDER BY pol.PurchaseOrderId, pol.Id;";

        var byId = list.ToDictionary(po => po.Id);
        using var lineReader = await lineCmd.ExecuteReaderAsync();
        while (await lineReader.ReadAsync())
        {
            var line = MapPurchaseOrderLine(lineReader);
            if (byId.TryGetValue(line.PurchaseOrderId, out var po)) po.Lines.Add(line);
        }

        return list;
    }

    public async Task<IReadOnlyList<PurchaseOrder>> GetOpenAsync(int limit)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        // Hanya PO yang masih menunggu tindakan: daftar seluruh riwayat pembelian beserta barisnya
        // tidak pernah dibutuhkan kotak tugas.
        cmd.CommandText = @"
            SELECT po.Id, po.PoNumber, po.SupplierId, s.Name AS SupplierName,
                   po.Status, po.OrderDate, po.ExpectedDate, po.Notes,
                   po.CreatedByUserId, po.CreatedAt
            FROM PurchaseOrders po
            LEFT JOIN Suppliers s ON po.SupplierId = s.Id
            WHERE po.Status IN ('Draft','Sent','PartialReceived')
            ORDER BY po.ExpectedDate IS NULL, po.ExpectedDate ASC, po.Id ASC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<PurchaseOrder>();
        using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                list.Add(MapPurchaseOrderHeader(reader));
        if (list.Count == 0) return list;

        var names = list.Select((_, i) => "@p" + i).ToArray();
        using var lineCmd = conn.CreateCommand();
        lineCmd.CommandText = PurchaseOrderLinesSql + $@"
            WHERE pol.PurchaseOrderId IN ({string.Join(",", names)})
            ORDER BY pol.PurchaseOrderId, pol.Id;";
        for (var i = 0; i < list.Count; i++)
            lineCmd.Parameters.AddWithValue(names[i], list[i].Id);

        var byId = list.ToDictionary(po => po.Id);
        using var lineReader = await lineCmd.ExecuteReaderAsync();
        while (await lineReader.ReadAsync())
        {
            var line = MapPurchaseOrderLine(lineReader);
            if (byId.TryGetValue(line.PurchaseOrderId, out var po)) po.Lines.Add(line);
        }
        return list;
    }

    public async Task<IReadOnlyList<PurchaseOrder>> GetPageAsync(string? status, int limit, int offset)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        var saring = SaringStatus(status);
        using var cmd = conn.CreateCommand();
        // Halaman dibatasi Id terakhir pada urutan (CreatedAt, Id) supaya PO yang baru dibuat
        // tidak menggeser baris halaman berikutnya.
        cmd.CommandText = $@"
            SELECT po.Id, po.PoNumber, po.SupplierId, s.Name AS SupplierName,
                   po.Status, po.OrderDate, po.ExpectedDate, po.Notes,
                   po.CreatedByUserId, po.CreatedAt
            FROM PurchaseOrders po
            LEFT JOIN Suppliers s ON po.SupplierId = s.Id
            WHERE {saring}
            ORDER BY po.CreatedAt DESC, po.Id DESC
            LIMIT @limit OFFSET @offset;";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var list = new List<PurchaseOrder>();
        using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                list.Add(MapPurchaseOrderHeader(reader));
        if (list.Count == 0) return list;

        var names = list.Select((_, i) => "@p" + i).ToArray();
        using var lineCmd = conn.CreateCommand();
        lineCmd.CommandText = PurchaseOrderLinesSql + $@"
            WHERE pol.PurchaseOrderId IN ({string.Join(",", names)})
            ORDER BY pol.PurchaseOrderId, pol.Id;";
        for (var i = 0; i < list.Count; i++)
            lineCmd.Parameters.AddWithValue(names[i], list[i].Id);

        var byId = list.ToDictionary(po => po.Id);
        using var lineReader = await lineCmd.ExecuteReaderAsync();
        while (await lineReader.ReadAsync())
        {
            var line = MapPurchaseOrderLine(lineReader);
            if (byId.TryGetValue(line.PurchaseOrderId, out var po)) po.Lines.Add(line);
        }
        return list;
    }

    public async Task<int> CountAsync(string? status)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM PurchaseOrders po WHERE {SaringStatus(status)};";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // Nilai "open" adalah bahasa layar untuk PO yang masih menunggu tindakan, bukan status di basis data.
    private static string SaringStatus(string? status)
    {
        if (string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
            return "po.Status IN ('Draft','Sent','PartialReceived')";
        if (string.IsNullOrWhiteSpace(status))
            return "1=1";
        return status.Trim().ToLowerInvariant() switch
        {
            "draft" => "po.Status = 'Draft'",
            "sent" => "po.Status = 'Sent'",
            "partialreceived" => "po.Status = 'PartialReceived'",
            "received" => "po.Status = 'Received'",
            "cancelled" => "po.Status = 'Cancelled'",
            _ => "1=1",
        };
    }

    public async Task<DeadlineCounts> GetOpenBreakdownAsync(DateTime today)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(CASE WHEN po.ExpectedDate IS NOT NULL AND po.ExpectedDate < @hari THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN po.ExpectedDate IS NULL OR po.ExpectedDate >= @hari THEN 1 ELSE 0 END), 0)
            FROM PurchaseOrders po
            WHERE po.Status IN ('Draft','Sent','PartialReceived');";
        cmd.Parameters.AddWithValue("@hari", today.ToString("yyyy-MM-dd"));
        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new DeadlineCounts(reader.GetInt32(0), reader.GetInt32(1));
    }

    // Satu daftar kolom baris untuk GetAllAsync dan GetByIdAsync agar keduanya membaca hal yang sama.
    private const string PurchaseOrderLinesSql = @"
            SELECT pol.Id, pol.PurchaseOrderId, pol.IngredientId, i.Name AS IngredientName,
                   pol.OrderedQty, pol.ReceivedQty, pol.UnitCostMinorUnits, pol.Unit
            FROM PurchaseOrderLines pol
            LEFT JOIN Ingredients i ON pol.IngredientId = i.Id";

    private static PurchaseOrderLine MapPurchaseOrderLine(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        PurchaseOrderId = reader.GetInt32(1),
        IngredientId = reader.GetInt32(2),
        IngredientName = reader.IsDBNull(3) ? null : reader.GetString(3),
        OrderedQty = reader.GetDecimal(4),
        ReceivedQty = reader.GetDecimal(5),
        UnitCostMinorUnits = reader.GetInt64(6),
        Unit = reader.GetString(7)
    };

    public async Task<PurchaseOrder?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        // Load header
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT po.Id, po.PoNumber, po.SupplierId, s.Name AS SupplierName,
                   po.Status, po.OrderDate, po.ExpectedDate, po.Notes,
                   po.CreatedByUserId, po.CreatedAt
            FROM PurchaseOrders po
            LEFT JOIN Suppliers s ON po.SupplierId = s.Id
            WHERE po.Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var po = MapPurchaseOrderHeader(reader);
        reader.Close();

        // Load lines
        using var lineCmd = conn.CreateCommand();
        lineCmd.CommandText = PurchaseOrderLinesSql + @"
            WHERE pol.PurchaseOrderId = @id;";
        lineCmd.Parameters.AddWithValue("@id", id);

        using var lineReader = await lineCmd.ExecuteReaderAsync();
        while (await lineReader.ReadAsync())
        {
            po.Lines.Add(MapPurchaseOrderLine(lineReader));
        }

        return po;
    }

    public async Task<int> CreateAsync(PurchaseOrder po)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO PurchaseOrders (PoNumber, SupplierId, Status, OrderDate, ExpectedDate, Notes, CreatedByUserId, CreatedAt)
                VALUES (@poNum, @supplierId, @status, @orderDate, @expectedDate, @notes, @userId, datetime('now'));
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@poNum", po.PoNumber);
            cmd.Parameters.AddWithValue("@supplierId", po.SupplierId);
            cmd.Parameters.AddWithValue("@status", po.Status.ToString());
            cmd.Parameters.AddWithValue("@orderDate", po.OrderDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@expectedDate", po.ExpectedDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@notes", po.Notes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@userId", po.CreatedByUserId ?? (object)DBNull.Value);

            int poId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            foreach (var line in po.Lines)
            {
                using var lineCmd = conn.CreateCommand();
                lineCmd.Transaction = tx;
                lineCmd.CommandText = @"
                    INSERT INTO PurchaseOrderLines (PurchaseOrderId, IngredientId, OrderedQty, ReceivedQty, UnitCostMinorUnits, Unit)
                    VALUES (@poId, @ingId, @orderedQty, 0, @unitCost, @unit);";
                lineCmd.Parameters.AddWithValue("@poId", poId);
                lineCmd.Parameters.AddWithValue("@ingId", line.IngredientId);
                lineCmd.Parameters.AddWithValue("@orderedQty", line.OrderedQty);
                lineCmd.Parameters.AddWithValue("@unitCost", line.UnitCostMinorUnits);
                lineCmd.Parameters.AddWithValue("@unit", line.Unit);
                await lineCmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
            return poId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateStatusAsync(int id, PurchaseOrderStatus status)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE PurchaseOrders SET Status = @status WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task ReceiveWithJournalAsync(
        GoodsReceipt receipt, IReadOnlyList<StockChange> stockChanges,
        JournalEntry? journal, PurchaseOrderStatus? newStatus)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            // 1. Header penerimaan
            var grCmd = conn.CreateCommand();
            grCmd.Transaction = tx;
            grCmd.CommandText = @"
                INSERT INTO GoodsReceipts (PurchaseOrderId, ReceivedDate, Notes, CreatedByUserId, CreatedAt)
                VALUES (@poId, @receivedDate, @notes, @userId, datetime('now'));
                SELECT last_insert_rowid();";
            grCmd.Parameters.AddWithValue("@poId", receipt.PurchaseOrderId);
            grCmd.Parameters.AddWithValue("@receivedDate", receipt.ReceivedDate.ToString("yyyy-MM-dd"));
            grCmd.Parameters.AddWithValue("@notes", receipt.Notes ?? (object)DBNull.Value);
            grCmd.Parameters.AddWithValue("@userId", receipt.CreatedByUserId ?? (object)DBNull.Value);
            int receiptId = Convert.ToInt32(await grCmd.ExecuteScalarAsync());

            // 2. Baris penerimaan + jumlah diterima pada baris PO
            foreach (var line in receipt.Lines)
            {
                var lineCmd = conn.CreateCommand();
                lineCmd.Transaction = tx;
                lineCmd.CommandText = @"
                    INSERT INTO GoodsReceiptLines (GoodsReceiptId, PurchaseOrderLineId, IngredientId, ReceivedQty, UnitCostMinorUnits, LocationId, BatchNumber, ExpiryDate)
                    VALUES (@grId, @polId, @ingId, @qty, @unitCost, @locId, @batch, @kedaluwarsa);
                    SELECT last_insert_rowid();";
                lineCmd.Parameters.AddWithValue("@grId", receiptId);
                lineCmd.Parameters.AddWithValue("@polId", line.PurchaseOrderLineId);
                lineCmd.Parameters.AddWithValue("@ingId", line.IngredientId);
                lineCmd.Parameters.AddWithValue("@qty", line.ReceivedQty);
                lineCmd.Parameters.AddWithValue("@unitCost", line.UnitCostMinorUnits);
                lineCmd.Parameters.AddWithValue("@locId", line.LocationId);
                lineCmd.Parameters.AddWithValue("@batch", string.IsNullOrWhiteSpace(line.BatchNumber) ? (object)DBNull.Value : line.BatchNumber.Trim());
                lineCmd.Parameters.AddWithValue("@kedaluwarsa", line.ExpiryDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
                var receiptLineId = Convert.ToInt32(await lineCmd.ExecuteScalarAsync());

                // Lot yang diketik saat penerimaan langsung masuk daftar kedaluwarsa. Kalau
                // kolom opsional itu kosong, tidak ada baris ItemBatches yang dikarang:
                // daftar tetap berisi barang yang tanggal kedaluwarsanya benar-benar diketahui.
                if (line.ExpiryDate is { } expiry)
                {
                    var batchCmd = conn.CreateCommand();
                    batchCmd.Transaction = tx;
                    batchCmd.CommandText = @"
                        INSERT INTO ItemBatches (IngredientId, BatchNumber, ExpiryDate, ReceivedDate, UnitCostMinorUnits, GoodsReceiptLineId)
                        VALUES (@ingId, @nomor, @kedaluwarsa, @terima, @unitCost, @baris);";
                    batchCmd.Parameters.AddWithValue("@ingId", line.IngredientId);
                    batchCmd.Parameters.AddWithValue("@nomor",
                        string.IsNullOrWhiteSpace(line.BatchNumber) ? $"TERIMA-{receiptId}-{receiptLineId}" : line.BatchNumber.Trim());
                    batchCmd.Parameters.AddWithValue("@kedaluwarsa", expiry.ToString("yyyy-MM-dd"));
                    batchCmd.Parameters.AddWithValue("@terima", receipt.ReceivedDate.ToString("yyyy-MM-dd"));
                    batchCmd.Parameters.AddWithValue("@unitCost", line.UnitCostMinorUnits);
                    batchCmd.Parameters.AddWithValue("@baris", receiptLineId);
                    await batchCmd.ExecuteNonQueryAsync();
                }

                var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = @"
                    UPDATE PurchaseOrderLines SET ReceivedQty = ReceivedQty + @qty WHERE Id = @lineId;";
                updateCmd.Parameters.AddWithValue("@qty", line.ReceivedQty);
                updateCmd.Parameters.AddWithValue("@lineId", line.PurchaseOrderLineId);
                await updateCmd.ExecuteNonQueryAsync();
            }

            // 3. Stok fisik + buku mutasi
            foreach (var change in stockChanges)
                await StockLedger.ApplyAsync(conn, tx, change.LocationId, change.IngredientId, change.Delta,
                    change.Type, change.SourceType, change.SourceId, change.UserId, change.Notes);

            // 4. Jurnal Dr Persediaan / Cr Utang Usaha; nomor struk baru diketahui di sini
            if (journal is not null)
            {
                journal.SourceDocumentId ??= receiptId;
                await JournalWriter.WriteAsync(conn, tx, journal);
            }

            // 5. Status PO
            if (newStatus is { } status)
            {
                var statusCmd = conn.CreateCommand();
                statusCmd.Transaction = tx;
                statusCmd.CommandText = "UPDATE PurchaseOrders SET Status = @status WHERE Id = @id;";
                statusCmd.Parameters.AddWithValue("@status", status.ToString());
                statusCmd.Parameters.AddWithValue("@id", receipt.PurchaseOrderId);
                await statusCmd.ExecuteNonQueryAsync();
            }
        });

    private static PurchaseOrder MapPurchaseOrderHeader(SqliteDataReader reader)
    {
        return new PurchaseOrder
        {
            Id = reader.GetInt32(0),
            PoNumber = reader.GetString(1),
            SupplierId = reader.GetInt32(2),
            SupplierName = reader.IsDBNull(3) ? null : reader.GetString(3),
            Status = Enum.Parse<PurchaseOrderStatus>(reader.GetString(4)),
            OrderDate = DateTime.Parse(reader.GetString(5)),
            ExpectedDate = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
            Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedByUserId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            CreatedAt = DateTime.Parse(reader.GetString(9))
        };
    }
}
