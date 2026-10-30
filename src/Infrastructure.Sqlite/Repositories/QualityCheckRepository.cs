using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class QualityCheckRepository : IQualityCheckRepository
{
    private readonly DbConnectionFactory _factory;
    public QualityCheckRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<QualityCheckView>> GetAllAsync(int limit = 200)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT qc.Id, qc.WorkOrderId, wo.OrderNumber, pt.Name AS ProductName,
                   qc.SampleQuantity, qc.PassedQuantity, qc.FailedQuantity, qc.Decision,
                   qc.AppearanceNotes, qc.TasteNotes, qc.Notes, qc.CheckedAt, u.Username
            FROM QualityChecks qc
            JOIN WorkOrders wo ON wo.Id = qc.WorkOrderId
            LEFT JOIN ProductTemplates pt ON pt.Id = wo.ProductTemplateId
            LEFT JOIN Users u ON u.Id = qc.CheckedByUserId
            ORDER BY qc.CheckedAt DESC, qc.Id DESC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<QualityCheckView>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new QualityCheckView(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                BusinessTime.BacaCapWaktu(r.GetString(11)),
                r.IsDBNull(12) ? null : r.GetString(12)));
        }
        return list;
    }

    public async Task<QualityCheck?> GetByWorkOrderAsync(int workOrderId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM QualityChecks WHERE WorkOrderId = @wo;";
        cmd.Parameters.AddWithValue("@wo", workOrderId);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? Map(r) : null;
    }

    public Task<int> CreateAsync(QualityCheck check)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO QualityChecks
                    (WorkOrderId, CheckedAt, CheckedByUserId, SampleQuantity, PassedQuantity,
                     FailedQuantity, Decision, AppearanceNotes, TasteNotes, Notes, CreatedAt)
                VALUES (@wo, @checkedAt, @userId, @sample, @passed, @failed, @decision,
                        @appearance, @taste, @notes, datetime('now'));
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@wo", check.WorkOrderId);
            cmd.Parameters.AddWithValue("@checkedAt", check.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@userId", check.CheckedByUserId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sample", check.SampleQuantity);
            cmd.Parameters.AddWithValue("@passed", check.PassedQuantity);
            cmd.Parameters.AddWithValue("@failed", check.FailedQuantity);
            cmd.Parameters.AddWithValue("@decision", check.Decision.ToString());
            cmd.Parameters.AddWithValue("@appearance", check.AppearanceNotes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@taste", check.TasteNotes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@notes", check.Notes ?? (object)DBNull.Value);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        });

    private static QualityCheck Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        WorkOrderId = r.GetInt32(r.GetOrdinal("WorkOrderId")),
        CheckedAt = BusinessTime.BacaCapWaktu(r.GetString(r.GetOrdinal("CheckedAt"))),
        CheckedByUserId = r.IsDBNull(r.GetOrdinal("CheckedByUserId")) ? null : r.GetInt32(r.GetOrdinal("CheckedByUserId")),
        SampleQuantity = r.GetInt32(r.GetOrdinal("SampleQuantity")),
        PassedQuantity = r.GetInt32(r.GetOrdinal("PassedQuantity")),
        FailedQuantity = r.GetInt32(r.GetOrdinal("FailedQuantity")),
        Decision = Enum.Parse<QualityDecision>(r.GetString(r.GetOrdinal("Decision"))),
        AppearanceNotes = r.IsDBNull(r.GetOrdinal("AppearanceNotes")) ? null : r.GetString(r.GetOrdinal("AppearanceNotes")),
        TasteNotes = r.IsDBNull(r.GetOrdinal("TasteNotes")) ? null : r.GetString(r.GetOrdinal("TasteNotes")),
        Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
    };
}
