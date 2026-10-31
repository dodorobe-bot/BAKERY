using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class DocumentSequenceRepository : IDocumentSequenceRepository
{
    private readonly DbConnectionFactory _factory;
    public DocumentSequenceRepository(DbConnectionFactory factory) => _factory = factory;

    // Kenaikan urutan dan pembacaan nomornya satu statement: dua panggilan berurutan
    // tidak pernah boleh membaca nilai yang sama.
    public Task<string> NextNumberAsync(DocumentKind kind, DateTime hariToko)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            var day = hariToko.Date;
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO DocumentSequences (Kind, DayKey, LastValue)
                VALUES (@kind, @day, 1)
                ON CONFLICT(Kind, DayKey) DO UPDATE SET LastValue = LastValue + 1
                RETURNING LastValue;";
            cmd.Parameters.AddWithValue("@kind", kind.ToString());
            cmd.Parameters.AddWithValue("@day", day.ToString("yyyy-MM-dd"));

            var sequence = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            return DocumentNumber.Format(kind, day, sequence);
        });
}
