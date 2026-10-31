using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class ProductionRepository : IProductionRepository
{
    private readonly DbConnectionFactory _factory;

    public ProductionRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<WorkOrder?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, OrderNumber, ProductTemplateId, TargetQuantity, ActualGoodQty, ActualFailedQty,
                   Status, TotalMaterialCost, LaborCost, OverheadCost, StartDate, CompletedDate,
                   CreatedByUserId, CreatedAt
            FROM WorkOrders WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var wo = new WorkOrder
        {
            Id = reader.GetInt32(0),
            OrderNumber = reader.GetString(1),
            ProductTemplateId = reader.GetInt32(2),
            TargetQuantity = reader.GetInt32(3),
            ActualGoodQty = reader.GetInt32(4),
            ActualFailedQty = reader.GetInt32(5),
            Status = Enum.Parse<WorkOrderStatus>(reader.GetString(6)),
            TotalMaterialCost = reader.GetInt64(7),
            LaborCost = reader.GetInt64(8),
            OverheadCost = reader.GetInt64(9),
            StartDate = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)),
            CompletedDate = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
            CreatedByUserId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
            CreatedAt = DateTime.Parse(reader.GetString(13))
        };
        reader.Close();

        // Load Materials
        using var matCmd = conn.CreateCommand();
        matCmd.CommandText = @"
            SELECT m.Id, m.WorkOrderId, m.IngredientId, m.StandardQty, m.ActualQty, m.UnitCost,
                   i.Name, i.Unit
            FROM WorkOrderMaterials m
            JOIN Ingredients i ON m.IngredientId = i.Id
            WHERE m.WorkOrderId = @id;";
        matCmd.Parameters.AddWithValue("@id", id);
        using var matReader = await matCmd.ExecuteReaderAsync();
        while (await matReader.ReadAsync())
        {
            wo.Materials.Add(new WorkOrderMaterial
            {
                Id = matReader.GetInt32(0),
                WorkOrderId = matReader.GetInt32(1),
                IngredientId = matReader.GetInt32(2),
                StandardQty = matReader.GetDecimal(3),
                ActualQty = matReader.GetDecimal(4),
                UnitCost = matReader.GetInt64(5),
                IngredientName = matReader.GetString(6),
                Unit = matReader.GetString(7)
            });
        }
        matReader.Close();

        // Load Scraps
        using var scrapCmd = conn.CreateCommand();
        scrapCmd.CommandText = @"
            SELECT Id, WorkOrderId, Quantity, ReasonCategory, ActionTaken, Notes, CreatedAt, CreatedByUserId
            FROM WorkOrderScraps WHERE WorkOrderId = @id;";
        scrapCmd.Parameters.AddWithValue("@id", id);
        using var scrapReader = await scrapCmd.ExecuteReaderAsync();
        while (await scrapReader.ReadAsync())
        {
            wo.Scraps.Add(new WorkOrderScrap
            {
                Id = scrapReader.GetInt32(0),
                WorkOrderId = scrapReader.GetInt32(1),
                Quantity = scrapReader.GetInt32(2),
                ReasonCategory = scrapReader.GetString(3),
                ActionTaken = scrapReader.GetString(4),
                Notes = scrapReader.IsDBNull(5) ? null : scrapReader.GetString(5),
                CreatedAt = DateTime.Parse(scrapReader.GetString(6)),
                CreatedByUserId = scrapReader.IsDBNull(7) ? null : scrapReader.GetInt32(7)
            });
        }

        return wo;
    }

    public async Task<IReadOnlyList<WorkOrder>> GetAllAsync(int limit = 50)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT wo.Id, wo.OrderNumber, wo.ProductTemplateId, wo.TargetQuantity,
                   wo.ActualGoodQty, wo.ActualFailedQty,
                   wo.Status, wo.TotalMaterialCost, wo.LaborCost, wo.OverheadCost,
                   wo.StartDate, wo.CompletedDate, wo.CreatedByUserId, wo.CreatedAt,
                   pt.Name AS ProductName
            FROM WorkOrders wo
            LEFT JOIN ProductTemplates pt ON wo.ProductTemplateId = pt.Id
            ORDER BY wo.Id DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<WorkOrder>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new WorkOrder
            {
                Id = reader.GetInt32(0),
                OrderNumber = reader.GetString(1),
                ProductTemplateId = reader.GetInt32(2),
                TargetQuantity = reader.GetInt32(3),
                ActualGoodQty = reader.GetInt32(4),
                ActualFailedQty = reader.GetInt32(5),
                Status = Enum.Parse<WorkOrderStatus>(reader.GetString(6)),
                TotalMaterialCost = reader.GetInt64(7),
                LaborCost = reader.GetInt64(8),
                OverheadCost = reader.GetInt64(9),
                StartDate = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)),
                CompletedDate = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
                CreatedByUserId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                CreatedAt = DateTime.Parse(reader.GetString(13)),
                ProductName = reader.IsDBNull(14) ? null : reader.GetString(14)
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<CompletedWorkOrderCost>> GetCompletedCostLinesAsync()
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT wo.Id, wo.ProductTemplateId, wo.CompletedDate,
                   wo.ActualGoodQty, wo.ActualFailedQty,
                   COALESCE((SELECT SUM(s.Quantity) FROM WorkOrderScraps s
                              WHERE s.WorkOrderId = wo.Id AND s.ActionTaken <> 'Rework'), 0),
                   wo.TotalMaterialCost, wo.LaborCost, wo.OverheadCost
            FROM WorkOrders wo
            WHERE wo.Status = 'Completed'
            ORDER BY wo.Id;";

        var list = new List<CompletedWorkOrderCost>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CompletedWorkOrderCost(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8)));
        }
        return list;
    }

    public async Task<IReadOnlyList<WorkOrder>> GetOpenAsync(int limit)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT wo.Id, wo.OrderNumber, wo.ProductTemplateId, wo.TargetQuantity,
                   wo.ActualGoodQty, wo.ActualFailedQty,
                   wo.Status, wo.TotalMaterialCost, wo.LaborCost, wo.OverheadCost,
                   wo.StartDate, wo.CompletedDate, wo.CreatedByUserId, wo.CreatedAt,
                   pt.Name AS ProductName
            FROM WorkOrders wo
            LEFT JOIN ProductTemplates pt ON wo.ProductTemplateId = pt.Id
            WHERE wo.Status IN ('Planned','In_Process')
            ORDER BY wo.StartDate IS NULL, wo.StartDate ASC, wo.Id ASC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<WorkOrder>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new WorkOrder
            {
                Id = reader.GetInt32(0),
                OrderNumber = reader.GetString(1),
                ProductTemplateId = reader.GetInt32(2),
                TargetQuantity = reader.GetInt32(3),
                ActualGoodQty = reader.GetInt32(4),
                ActualFailedQty = reader.GetInt32(5),
                Status = Enum.Parse<WorkOrderStatus>(reader.GetString(6)),
                TotalMaterialCost = reader.GetInt64(7),
                LaborCost = reader.GetInt64(8),
                OverheadCost = reader.GetInt64(9),
                StartDate = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)),
                CompletedDate = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
                CreatedByUserId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                CreatedAt = DateTime.Parse(reader.GetString(13)),
                ProductName = reader.IsDBNull(14) ? null : reader.GetString(14)
            });
        }
        return list;
    }

    public async Task<DeadlineCounts> GetOpenBreakdownAsync(DateTime today)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        // StartDate menyimpan jam, jadi tanggalnya dibandingkan lewat 10 karakter pertama.
        cmd.CommandText = @"
            SELECT COALESCE(SUM(CASE WHEN wo.StartDate IS NOT NULL AND substr(wo.StartDate, 1, 10) < @hari THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN wo.StartDate IS NULL OR substr(wo.StartDate, 1, 10) >= @hari THEN 1 ELSE 0 END), 0)
            FROM WorkOrders wo
            WHERE wo.Status IN ('Planned','In_Process');";
        cmd.Parameters.AddWithValue("@hari", today.ToString("yyyy-MM-dd"));
        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new DeadlineCounts(reader.GetInt32(0), reader.GetInt32(1));
    }

    public async Task<int> CreateAsync(WorkOrder order, IEnumerable<WorkOrderMaterial> materials)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO WorkOrders (
                    OrderNumber, ProductTemplateId, TargetQuantity, Status,
                    TotalMaterialCost, LaborCost, OverheadCost, StartDate, CreatedByUserId, CreatedAt
                ) VALUES (
                    @orderNum, @tmplId, @targetQty, @status,
                    @matCost, @laborCost, @overheadCost, @startDate, @userId, datetime('now')
                );
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@orderNum", order.OrderNumber);
            cmd.Parameters.AddWithValue("@tmplId", order.ProductTemplateId);
            cmd.Parameters.AddWithValue("@targetQty", order.TargetQuantity);
            cmd.Parameters.AddWithValue("@status", order.Status.ToString());
            cmd.Parameters.AddWithValue("@matCost", order.TotalMaterialCost);
            cmd.Parameters.AddWithValue("@laborCost", order.LaborCost);
            cmd.Parameters.AddWithValue("@overheadCost", order.OverheadCost);
            cmd.Parameters.AddWithValue("@startDate", order.StartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@userId", order.CreatedByUserId ?? (object)DBNull.Value);

            int id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            foreach (var m in materials)
            {
                using var mCmd = conn.CreateCommand();
                mCmd.Transaction = tx;
                mCmd.CommandText = @"
                    INSERT INTO WorkOrderMaterials (WorkOrderId, IngredientId, StandardQty, ActualQty, UnitCost)
                    VALUES (@woId, @ingId, @stdQty, @actQty, @unitCost);";
                mCmd.Parameters.AddWithValue("@woId", id);
                mCmd.Parameters.AddWithValue("@ingId", m.IngredientId);
                mCmd.Parameters.AddWithValue("@stdQty", m.StandardQty);
                mCmd.Parameters.AddWithValue("@actQty", m.ActualQty);
                mCmd.Parameters.AddWithValue("@unitCost", m.UnitCost);
                await mCmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
            return id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateAsync(WorkOrder order)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE WorkOrders SET
                ActualGoodQty = @good,
                ActualFailedQty = @failed,
                Status = @status,
                TotalMaterialCost = @matCost,
                LaborCost = @laborCost,
                OverheadCost = @overheadCost,
                CompletedDate = @completedDate
            WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@good", order.ActualGoodQty);
        cmd.Parameters.AddWithValue("@failed", order.ActualFailedQty);
        cmd.Parameters.AddWithValue("@status", order.Status.ToString());
        cmd.Parameters.AddWithValue("@matCost", order.TotalMaterialCost);
        cmd.Parameters.AddWithValue("@laborCost", order.LaborCost);
        cmd.Parameters.AddWithValue("@overheadCost", order.OverheadCost);
        cmd.Parameters.AddWithValue("@completedDate", order.CompletedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id", order.Id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task CancelWorkOrderAsync(int id, string? reason)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            // Check current status before cancelling
            using var checkCmd = conn.CreateCommand();
            checkCmd.Transaction = tx;
            checkCmd.CommandText = "SELECT Status FROM WorkOrders WHERE Id = @id;";
            checkCmd.Parameters.AddWithValue("@id", id);
            var statusStr = (string?)await checkCmd.ExecuteScalarAsync();
            if (statusStr == null) throw new InvalidOperationException("WorkOrder tidak ditemukan.");

            var status = Enum.Parse<WorkOrderStatus>(statusStr);
            if (status == WorkOrderStatus.Completed || status == WorkOrderStatus.Cancelled)
                throw new InvalidOperationException($"SPK tidak dapat dibatalkan karena statusnya sudah {status}.");

            // Cancel the work order and persist the reason
            using var cancelCmd = conn.CreateCommand();
            cancelCmd.Transaction = tx;
            cancelCmd.CommandText = @"
                UPDATE WorkOrders SET Status = 'Cancelled', CancelReason = @reason
                WHERE Id = @id;";
            cancelCmd.Parameters.AddWithValue("@id", id);
            cancelCmd.Parameters.AddWithValue("@reason", reason ?? (object)DBNull.Value);
            await cancelCmd.ExecuteNonQueryAsync();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public Task CompleteWorkOrderAsync(
        int id,
        int actualGood,
        int actualFailed,
        IEnumerable<WorkOrderMaterial> actualMaterials,
        IEnumerable<WorkOrderScrap> scraps,
        JournalEntry? journal,
        long costPerGoodUnitMinorUnits)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            var bahanMasuk = actualMaterials?.ToList() ?? new List<WorkOrderMaterial>();
            var daftarScrap = scraps?.ToList() ?? new List<WorkOrderScrap>();

            if (actualGood < 0 || actualFailed < 0)
                throw new InvalidOperationException("Jumlah produk jadi dan rusak tidak boleh negatif.");
            if (actualGood + actualFailed <= 0)
                throw new InvalidOperationException("Penyelesaian SPK harus melaporkan hasil produksi lebih dari nol.");

            // 1. SPK hanya boleh diselesaikan sekali: menyelesaikan ulang membukukan biaya dua kali.
            var stateCmd = conn.CreateCommand();
            stateCmd.Transaction = tx;
            stateCmd.CommandText = "SELECT Status FROM WorkOrders WHERE Id = @id;";
            stateCmd.Parameters.AddWithValue("@id", id);
            var currentStatus = await stateCmd.ExecuteScalarAsync();
            if (currentStatus is null or DBNull)
                throw new InvalidOperationException("SPK tidak ditemukan.");
            var status = currentStatus.ToString();
            if (status is "Completed" or "Cancelled")
                throw new InvalidOperationException($"SPK berstatus {status} tidak dapat diselesaikan ulang.");

            // 2. Rencana bahan yang tersimpan adalah satu-satunya sumber set bahan dan harganya.
            // Harga dari pemanggil diabaikan: nilai stok dan HPP tidak boleh bisa ditekan lewat
            // payload, dan setiap baris rencana wajib dilaporkan tepat satu kali.
            var rencana = new Dictionary<int, (decimal StandardQty, long UnitCost)>();
            using (var planCmd = conn.CreateCommand())
            {
                planCmd.Transaction = tx;
                planCmd.CommandText =
                    "SELECT IngredientId, StandardQty, UnitCost FROM WorkOrderMaterials WHERE WorkOrderId = @id;";
                planCmd.Parameters.AddWithValue("@id", id);
                using var planReader = await planCmd.ExecuteReaderAsync();
                while (await planReader.ReadAsync())
                {
                    var ingredientId = planReader.GetInt32(0);
                    if (!rencana.TryAdd(ingredientId,
                            (planReader.GetDecimal(1), planReader.GetInt64(2))))
                        throw new InvalidOperationException(
                            $"Rencana SPK memuat bahan #{ingredientId} lebih dari satu baris; perbaiki resep lebih dulu.");
                }
            }

            var dilaporkan = new HashSet<int>();
            var rincian = new List<(int IngredientId, decimal Quantity, long UnitCost)>();
            foreach (var m in bahanMasuk)
            {
                if (m.ActualQty < 0)
                    throw new InvalidOperationException("Jumlah bahan terpakai tidak boleh negatif.");
                if (!rencana.TryGetValue(m.IngredientId, out var planned))
                    throw new InvalidOperationException(
                        $"Bahan #{m.IngredientId} tidak ada dalam rencana SPK ini.");
                if (!dilaporkan.Add(m.IngredientId))
                    throw new InvalidOperationException(
                        $"Bahan #{m.IngredientId} dilaporkan lebih dari sekali.");
                rincian.Add((m.IngredientId, m.ActualQty, planned.UnitCost));
            }
            foreach (var ingredientId in rencana.Keys)
                if (!dilaporkan.Contains(ingredientId))
                    throw new InvalidOperationException($"Bahan rencana #{ingredientId} belum dilaporkan.");

            // 3. Status, hasil, dan total biaya bahan aktual satu paket dengan jurnalnya
            long totalMaterialCost = rincian.Sum(r =>
                (long)Math.Round(r.Quantity * r.UnitCost, 0, MidpointRounding.AwayFromZero));
            var woCmd = conn.CreateCommand();
            woCmd.Transaction = tx;
            woCmd.CommandText = @"
                UPDATE WorkOrders SET
                    ActualGoodQty = @good,
                    ActualFailedQty = @failed,
                    Status = 'Completed',
                    TotalMaterialCost = @matCost,
                    CompletedDate = datetime('now')
                WHERE Id = @id AND Status NOT IN ('Completed','Cancelled');";
            woCmd.Parameters.AddWithValue("@good", actualGood);
            woCmd.Parameters.AddWithValue("@failed", actualFailed);
            woCmd.Parameters.AddWithValue("@matCost", totalMaterialCost);
            woCmd.Parameters.AddWithValue("@id", id);
            if (await woCmd.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("SPK sudah diselesaikan atau dibatalkan oleh proses lain.");

            // 4. Pemakaian bahan aktual + pengurangan stok dapur
            var woHeader = conn.CreateCommand();
            woHeader.Transaction = tx;
            woHeader.CommandText = "SELECT CreatedByUserId, ProductTemplateId FROM WorkOrders WHERE Id = @id;";
            woHeader.Parameters.AddWithValue("@id", id);
            int? woCreatedBy = null;
            int productTemplateId = 0;
            using (var headerReader = await woHeader.ExecuteReaderAsync())
                if (await headerReader.ReadAsync())
                {
                    if (!headerReader.IsDBNull(0))
                        woCreatedBy = headerReader.GetInt32(0);
                    productTemplateId = headerReader.GetInt32(1);
                }

            int kitchenLocationId = await StockLedger.ResolveLocationIdByTypeAsync(conn, tx, "Kitchen");
            foreach (var (ingredientId, quantity, unitCost) in rincian)
            {
                var mCmd = conn.CreateCommand();
                mCmd.Transaction = tx;
                mCmd.CommandText = @"
                    UPDATE WorkOrderMaterials SET ActualQty = @actQty, UnitCost = @unitCost
                    WHERE WorkOrderId = @woId AND IngredientId = @ingId;";
                mCmd.Parameters.AddWithValue("@actQty", quantity);
                mCmd.Parameters.AddWithValue("@unitCost", unitCost);
                mCmd.Parameters.AddWithValue("@woId", id);
                mCmd.Parameters.AddWithValue("@ingId", ingredientId);
                if (await mCmd.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException(
                        $"Baris bahan #{ingredientId} tidak ditemukan; penyelesaian dibatalkan.");

                await StockLedger.ApplyAsync(
                    conn, tx, kitchenLocationId, ingredientId, -quantity,
                    MovementType.Deduction, "WorkOrder", id, woCreatedBy, null);
            }

            // 5. Produk rusak
            foreach (var s in daftarScrap)
            {
                var sCmd = conn.CreateCommand();
                sCmd.Transaction = tx;
                sCmd.CommandText = @"
                    INSERT INTO WorkOrderScraps (WorkOrderId, Quantity, ReasonCategory, ActionTaken, Notes, CreatedAt, CreatedByUserId)
                    VALUES (@woId, @qty, @reason, @action, @notes, datetime('now'), @userId);";
                sCmd.Parameters.AddWithValue("@woId", id);
                sCmd.Parameters.AddWithValue("@qty", s.Quantity);
                sCmd.Parameters.AddWithValue("@reason", s.ReasonCategory);
                sCmd.Parameters.AddWithValue("@action", s.ActionTaken);
                sCmd.Parameters.AddWithValue("@notes", s.Notes ?? (object)DBNull.Value);
                sCmd.Parameters.AddWithValue("@userId", s.CreatedByUserId ?? (object)DBNull.Value);
                await sCmd.ExecuteNonQueryAsync();
            }

            // 6. Hasil baik masuk stok produk jadi di etalase, bukan mengurangi bahan saat dijual
            if (actualGood > 0)
            {
                int displayLocationId = await StockLedger.ResolveLocationIdByTypeAsync(conn, tx, "Display");
                await FinishedGoodsLedger.ApplyAsync(
                    conn, tx, displayLocationId, productTemplateId, actualGood,
                    FinishedGoodsMovementType.Production, "WorkOrder", id, woCreatedBy,
                    $"Hasil produksi SPK", costPerGoodUnitMinorUnits);
            }

            // 7. Jurnal produksi — gagal jurnal berarti seluruh penyelesaian batal
            if (journal is not null)
                await JournalWriter.WriteAsync(conn, tx, journal);
        });
}
