using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Daftar periode buku dibangun dari isi jurnal, bukan dari tabel FiscalPeriods saja: bulan yang
/// sudah berisi transaksi tapi belum pernah ditutup harus tetap muncul supaya bisa ditutup.
/// Bulan berjalan ikut selalu ditampilkan, walaupun belum ada satu pun entri, agar pemilik melihat
/// periode mana yang masih terbuka sebelum memutuskan menutup.
/// </summary>
public class FiscalPeriodRepository : IFiscalPeriodRepository
{
    private readonly DbConnectionFactory _factory;

    public FiscalPeriodRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<FiscalPeriodSummary>> ListAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            WITH isi AS (
                SELECT substr(je.TransactionDate, 1, 7) Kunci,
                       COUNT(DISTINCT je.Id) Entri,
                       COALESCE(SUM(jl.DebitAmount), 0) Debit
                  FROM JournalEntries je
                  LEFT JOIN JournalLines jl ON jl.JournalEntryId = je.Id
                 GROUP BY substr(je.TransactionDate, 1, 7)
            ),
            bulan AS (
                SELECT Kunci FROM isi
                UNION
                SELECT substr(StartDate, 1, 7) FROM FiscalPeriods
                UNION
                SELECT substr(@hariToko, 1, 7)
            )
            SELECT b.Kunci, fp.Id, fp.Name, fp.StartDate, fp.EndDate,
                   COALESCE(fp.Status, 'Open'), fp.ClosedAt, u.Username,
                   COALESCE(i.Entri, 0), COALESCE(i.Debit, 0)
              FROM bulan b
              LEFT JOIN FiscalPeriods fp ON substr(fp.StartDate, 1, 7) = b.Kunci
              LEFT JOIN Users u ON u.Id = fp.ClosedByUserId
              LEFT JOIN isi i ON i.Kunci = b.Kunci
             ORDER BY b.Kunci DESC;";
        cmd.Parameters.AddWithValue("@hariToko", BusinessTime.Today().ToString("yyyy-MM-dd"));

        var hariIni = BusinessTime.Today().ToString("yyyy-MM");
        var hasil = new List<FiscalPeriodSummary>();

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var kunci = reader.GetString(0);
            var adaBaris = !reader.IsDBNull(1);

            // Bulan yang belum pernah disentuh tidak punya baris FiscalPeriods, jadi rentang dan
            // namanya dihitung dari kuncinya — bukan dibiarkan kosong sehingga layar menampilkan
            // "—" untuk periode yang justru paling sering dilihat.
            var bulan = FiscalCalendar.MonthOf(DateTime.ParseExact(kunci + "-01", "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture));

            hasil.Add(new FiscalPeriodSummary
            {
                Id = adaBaris ? reader.GetInt32(1) : 0,
                Name = adaBaris && !reader.IsDBNull(2) ? reader.GetString(2) : bulan.Name,
                StartDate = adaBaris && !reader.IsDBNull(3) ? reader.GetString(3) : bulan.StartText,
                EndDate = adaBaris && !reader.IsDBNull(4) ? reader.GetString(4) : bulan.EndText,
                Status = reader.GetString(5),
                ClosedAt = reader.IsDBNull(6) ? null : BusinessTime.BacaCapWaktu(reader.GetString(6)),
                ClosedByName = reader.IsDBNull(7) ? null : reader.GetString(7),
                IsCurrent = kunci == hariIni,
                EntryCount = reader.GetInt32(8),
                DebitTotalMinorUnits = reader.GetInt64(9),
            });
        }

        return hasil;
    }

    /// <summary>
    /// Menutup dan membuka kembali hanya mengubah status satu baris. Jurnalnya tidak disentuh sama
    /// sekali, jadi tindakan ini selalu bisa dibatalkan dan riwayatnya tetap terbaca di laporan.
    /// </summary>
    public async Task UpsertStatusAsync(string name, string startDate, string endDate, string status, int userId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO FiscalPeriods (Name, StartDate, EndDate, Status, ClosedAt, ClosedByUserId)
            VALUES (@name, @start, @end, @status, @closedAt, @user)
            ON CONFLICT(StartDate) DO UPDATE
               SET Name = excluded.Name,
                   EndDate = excluded.EndDate,
                   Status = excluded.Status,
                   ClosedAt = excluded.ClosedAt,
                   ClosedByUserId = excluded.ClosedByUserId;";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@start", startDate);
        cmd.Parameters.AddWithValue("@end", endDate);
        cmd.Parameters.AddWithValue("@status", status);

        // Cap waktu ditulis UTC dalam format ISO, sama seperti kolom waktu lain di basis data ini;
        // layar yang menampilkannya bertanggung jawab memindahkannya ke zona toko.
        cmd.Parameters.AddWithValue("@closedAt", status == "Open"
            ? (object)DBNull.Value
            : DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@user", status == "Open" ? (object)DBNull.Value : userId);

        await cmd.ExecuteNonQueryAsync();
    }
}
