using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class JournalRepository : IJournalRepository
{
    private readonly DbConnectionFactory _factory;
    public JournalRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<int> CreateAsync(JournalEntry entry)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var entryId = await JournalWriter.WriteAsync(conn, tx, entry);
            tx.Commit();
            return entryId;
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<JournalEntry?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM JournalEntries WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        if (!r.Read()) return null;
        var entry = MapEntry(r);
        r.Close();
        entry.Lines = await GetLinesAsync(conn, id);
        return entry;
    }

    public async Task<JournalEntry?> GetByIdempotencyKeyAsync(string key)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM JournalEntries WHERE IdempotencyKey = @k;";
        cmd.Parameters.AddWithValue("@k", key);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? MapEntry(r) : null;
    }

    public async Task<IReadOnlyList<JournalEntry>> GetRecentAsync(int limit = 30)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Daftar menampilkan entri Posted saja. Entri asli yang sudah dibalik berstatus Reversed
        // dan yang tampil adalah entri pembaliknya, sehingga tidak ada dua set angka di layar.
        // Sebaliknya, setiap penjumlahan (saldo, arus kas, rekap harian, beban, rentang, ekspor)
        // wajib memuat Posted dan Reversed bersama: keduanya pasangan yang saling meniadakan.
        // Menghitung pembaliknya saja membuat dokumen yang dibatalkan muncul sebagai angka negatif.
        cmd.CommandText = EntryWithLinesSql + @"
            WHERE je.Status = 'Posted'
              AND je.Id IN (SELECT Id FROM JournalEntries WHERE Status = 'Posted'
                            ORDER BY TransactionDate DESC, Id DESC LIMIT @limit)
            ORDER BY je.TransactionDate DESC, je.Id DESC, jl.Id;";
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadEntriesWithLinesAsync(conn, cmd);
    }

    public async Task<IReadOnlyList<JournalEntry>> GetByDateRangePageAsync(
        DateTime from, DateTime to, DateTime? beforeDate, int? beforeId, int limit)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // TransactionDate disimpan 'yyyy-MM-dd' (JournalWriter), jadi batas atas setengah terbuka
        // memakai teks dan indeks (Status, TransactionDate) tetap terpakai. Id entri dipilih dulu
        // lewat subkueri supaya LIMIT berlaku per entri, bukan per kaki jurnal hasil join.
        cmd.CommandText = EntryWithLinesSql + @"
            WHERE je.Status = 'Posted'
              AND je.Id IN (
                  SELECT Id FROM JournalEntries
                  WHERE Status = 'Posted'
                    AND TransactionDate >= @from AND TransactionDate < @to
                    AND (@beforeDate IS NULL OR TransactionDate < @beforeDate
                         OR (TransactionDate = @beforeDate AND Id < @beforeId))
                  ORDER BY TransactionDate DESC, Id DESC
                  LIMIT @limit)
            ORDER BY je.TransactionDate DESC, je.Id DESC, jl.Id;";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.AddDays(1).ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@beforeDate",
            beforeDate.HasValue ? beforeDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@beforeId", beforeId.HasValue ? beforeId.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadEntriesWithLinesAsync(conn, cmd);
    }

    public async Task<JournalRangeSummary> GetRangeSummaryAsync(DateTime from, DateTime to, string? sourceType)
    {
        using var conn = _factory.CreateConnection();
        var saring = "";
        if (string.Equals(sourceType, "none", StringComparison.OrdinalIgnoreCase))
            saring = " AND (je.SourceDocumentType IS NULL OR je.SourceDocumentType = '')";
        else if (!string.IsNullOrWhiteSpace(sourceType))
            saring = " AND je.SourceDocumentType = @sumber";

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT a.Id, a.Code, a.Name, a.Type,
                   COALESCE(SUM(jl.DebitAmount), 0) AS Debit,
                   COALESCE(SUM(jl.CreditAmount), 0) AS Kredit,
                   COUNT(*) AS Kaki
            FROM JournalEntries je
            JOIN JournalLines jl ON jl.JournalEntryId = je.Id
            JOIN Accounts a ON a.Id = jl.AccountId
            WHERE je.Status IN ('Posted','Reversed')
              AND je.TransactionDate >= @from AND je.TransactionDate < @to{saring}
            GROUP BY a.Id, a.Code, a.Name, a.Type
            ORDER BY a.Code;";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.AddDays(1).ToString("yyyy-MM-dd"));
        if (saring.Contains("@sumber", StringComparison.Ordinal))
            cmd.Parameters.AddWithValue("@sumber", sourceType!);

        var akun = new List<AccountPeriodTotal>();
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                akun.Add(new AccountPeriodTotal(
                    r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetInt64(4), r.GetInt64(5), r.GetInt32(6)));
        }

        var hitung = conn.CreateCommand();
        hitung.CommandText = $@"
            SELECT COUNT(*)
            FROM JournalEntries je
            WHERE je.Status IN ('Posted','Reversed')
              AND je.TransactionDate >= @from AND je.TransactionDate < @to{saring};";
        hitung.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        hitung.Parameters.AddWithValue("@to", to.AddDays(1).ToString("yyyy-MM-dd"));
        if (saring.Contains("@sumber", StringComparison.Ordinal))
            hitung.Parameters.AddWithValue("@sumber", sourceType!);
        var jumlahEntri = Convert.ToInt32(await hitung.ExecuteScalarAsync());

        var sumberCmd = conn.CreateCommand();
        sumberCmd.CommandText = $@"
            SELECT DISTINCT je.SourceDocumentType
            FROM JournalEntries je
            WHERE je.Status IN ('Posted','Reversed')
              AND je.TransactionDate >= @from AND je.TransactionDate < @to
              AND je.SourceDocumentType IS NOT NULL AND je.SourceDocumentType <> ''
            ORDER BY je.SourceDocumentType;";
        sumberCmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        sumberCmd.Parameters.AddWithValue("@to", to.AddDays(1).ToString("yyyy-MM-dd"));
        var sumber = new List<string>();
        using (var r = await sumberCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) sumber.Add(r.GetString(0));
        }

        return new JournalRangeSummary(
            jumlahEntri, akun.Sum(a => a.Legs), akun, sumber);
    }

    public async Task<IReadOnlyList<JournalEntry>> GetByDateRangeAsync(DateTime from, DateTime to)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Perbandingan tanggal penuh: TransactionDate menyimpan jam, jadi 'BETWEEN' pada string
        // 'yyyy-MM-dd' akan membuang seluruh entri di tanggal penutup rentang.
        cmd.CommandText = EntryWithLinesSql + @"
            WHERE date(je.TransactionDate) BETWEEN date(@from) AND date(@to) AND je.Status IN ('Posted','Reversed')
            ORDER BY je.TransactionDate ASC, je.Id ASC, jl.Id;";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
        return await ReadEntriesWithLinesAsync(conn, cmd);
    }

    public async Task<IReadOnlyList<JournalEntry>> GetBySourceDocumentAsync(
        string sourceDocumentType, int sourceDocumentId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = EntryWithLinesSql + @"
            WHERE je.SourceDocumentType = @sdt AND je.SourceDocumentId = @sdi
              AND je.Status = 'Posted'
            ORDER BY je.Id ASC, jl.Id;";
        cmd.Parameters.AddWithValue("@sdt", sourceDocumentType);
        cmd.Parameters.AddWithValue("@sdi", sourceDocumentId);
        return await ReadEntriesWithLinesAsync(conn, cmd);
    }

    // Baris jurnal ikut terbaca di sini: tanpa akun, layar buku besar dan ekspor hanya
    // menghasilkan kolom kosong dan angka nol.
    private const string EntryWithLinesSql = @"
        SELECT je.*,
               jl.Id             AS LineId,
               jl.JournalEntryId AS LineJournalEntryId,
               jl.AccountId      AS LineAccountId,
               jl.DebitAmount,
               jl.CreditAmount,
               jl.Notes          AS LineNotes,
               a.Code            AS AccountCode,
               a.Name            AS AccountName,
               a.Type            AS AccountType
        FROM JournalEntries je
        LEFT JOIN JournalLines jl ON jl.JournalEntryId = je.Id
        LEFT JOIN Accounts a ON a.Id = jl.AccountId";

    private static async Task<List<JournalEntry>> ReadEntriesWithLinesAsync(
        SqliteConnection conn, SqliteCommand cmd)
    {
        var byId = new Dictionary<int, JournalEntry>();
        var order = new List<JournalEntry>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var entryId = r.GetInt32(r.GetOrdinal("Id"));
            if (!byId.TryGetValue(entryId, out var entry))
            {
                entry = MapEntry(r);
                byId[entryId] = entry;
                order.Add(entry);
            }
            if (!r.IsDBNull(r.GetOrdinal("LineId")))
                entry.Lines.Add(new JournalLine
                {
                    Id = r.GetInt32(r.GetOrdinal("LineId")),
                    JournalEntryId = r.GetInt32(r.GetOrdinal("LineJournalEntryId")),
                    AccountId = r.GetInt32(r.GetOrdinal("LineAccountId")),
                    DebitAmount = r.GetInt64(r.GetOrdinal("DebitAmount")),
                    CreditAmount = r.GetInt64(r.GetOrdinal("CreditAmount")),
                    Notes = r.IsDBNull(r.GetOrdinal("LineNotes")) ? null : r.GetString(r.GetOrdinal("LineNotes")),
                    Account = new Account
                    {
                        Id = r.GetInt32(r.GetOrdinal("LineAccountId")),
                        Code = r.IsDBNull(r.GetOrdinal("AccountCode")) ? string.Empty : r.GetString(r.GetOrdinal("AccountCode")),
                        Name = r.IsDBNull(r.GetOrdinal("AccountName")) ? string.Empty : r.GetString(r.GetOrdinal("AccountName")),
                        Type = r.IsDBNull(r.GetOrdinal("AccountType"))
                            ? AccountType.Asset
                            : Enum.Parse<AccountType>(r.GetString(r.GetOrdinal("AccountType")))
                    }
                });
        }
        return order;
    }

    public async Task UpdateStatusAsync(int id, JournalStatus status)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE JournalEntries SET Status = @st WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@st", status.ToString());
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AccountBalance>> GetBalancesAsync(DateTime? asOf = null)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Saldo dilaporkan pada arah normal akun. Entri Reversed tetap dihitung bersama entri
        // pembaliknya: keduanya adalah pasangan yang saling meniadakan. Kalau entri asli dibuang
        // tetapi pembaliknya dihitung, setiap pembatalan menggeser saldo sebesar nilai dokumennya.
        // asOf memotong saldo pada akhir satu tanggal: buku kas rentang lama tidak boleh ikut
        // menghitung transaksi sesudahnya. Batasnya ditaruh di dalam CASE karena LEFT JOIN.
        cmd.CommandText = @"
            SELECT a.Id, a.Code, a.Name, a.Type,
                   CASE WHEN a.Type IN ('Asset','Expense')
                        THEN COALESCE(SUM(CASE WHEN je.Status IN ('Posted','Reversed')
                                                AND (@asOf IS NULL OR je.TransactionDate <= @asOf)
                                               THEN jl.DebitAmount ELSE 0 END), 0)
                           - COALESCE(SUM(CASE WHEN je.Status IN ('Posted','Reversed')
                                                AND (@asOf IS NULL OR je.TransactionDate <= @asOf)
                                               THEN jl.CreditAmount ELSE 0 END), 0)
                        ELSE COALESCE(SUM(CASE WHEN je.Status IN ('Posted','Reversed')
                                                AND (@asOf IS NULL OR je.TransactionDate <= @asOf)
                                               THEN jl.CreditAmount ELSE 0 END), 0)
                           - COALESCE(SUM(CASE WHEN je.Status IN ('Posted','Reversed')
                                                AND (@asOf IS NULL OR je.TransactionDate <= @asOf)
                                               THEN jl.DebitAmount ELSE 0 END), 0)
                   END AS Balance
            FROM Accounts a
            LEFT JOIN JournalLines jl ON jl.AccountId = a.Id
            LEFT JOIN JournalEntries je ON je.Id = jl.JournalEntryId
            WHERE a.IsActive = 1
            GROUP BY a.Id, a.Code, a.Name, a.Type
            ORDER BY a.Code;";
        cmd.Parameters.AddWithValue("@asOf",
            asOf.HasValue ? asOf.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<AccountBalance>();
        while (await r.ReadAsync())
            list.Add(new AccountBalance(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                Enum.Parse<AccountType>(r.GetString(3)), r.GetInt64(4)));
        return list;
    }

    public async Task<long> GetAccountBalanceAsync(string code)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Hanya satu akun yang dibaca lewat indeks (AccountId, ...): dashboard tidak perlu
        // menjumlah seluruh bagan akun untuk menampilkan saldo kas.
        cmd.CommandText = @"
            SELECT COALESCE(SUM(CASE WHEN a.Type IN ('Asset','Expense')
                                     THEN jl.DebitAmount - jl.CreditAmount
                                     ELSE jl.CreditAmount - jl.DebitAmount END), 0)
            FROM Accounts a
            JOIN JournalLines jl ON jl.AccountId = a.Id
            JOIN JournalEntries je ON je.Id = jl.JournalEntryId
            WHERE a.Code = @kode AND je.Status IN ('Posted','Reversed');";
        cmd.Parameters.AddWithValue("@kode", code);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task<CashFlowSummary> GetCashFlowAsync(DateTime day, string accountCode)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // TransactionDate disimpan 'yyyy-MM-dd', jadi rentang setengah terbuka cukup membandingkan teks
        // dan indeks (Status, TransactionDate) tetap terpakai — date() pada kolom mematikan indeks itu.
        cmd.CommandText = @"
            SELECT COALESCE(SUM(jl.DebitAmount), 0), COALESCE(SUM(jl.CreditAmount), 0)
            FROM JournalEntries je
            JOIN JournalLines jl ON jl.JournalEntryId = je.Id
            WHERE je.Status IN ('Posted','Reversed')
              AND je.TransactionDate >= @from AND je.TransactionDate < @to
              AND jl.AccountId = (SELECT Id FROM Accounts WHERE Code = @kode);";
        cmd.Parameters.AddWithValue("@from", day.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", day.AddDays(1).ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@kode", accountCode);
        using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new CashFlowSummary(r.GetInt64(0), r.GetInt64(1));
    }

    public async Task<IReadOnlyList<DailyJournalTotals>> GetDailyTotalsAsync(DateTime from, DateTime to)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Baris jurnal dibaca berurutan lalu entrinya dicari lewat primary key (CROSS JOIN),
        // bukan sebaliknya. Diukur pada 500 ribu pesanan / 430 ribu baris jurnal: membaca
        // entri lebih dulu membuat setiap baris dibaca acak dari tabel besar (0,8 detik untuk
        // satu tahun), sedangkan pemindaian berurutan selesai ~0,24 detik. Urutan gabung ini
        // disengaja; tanpa CROSS JOIN, perencana kueri memilih entri lebih dulu.
        cmd.CommandText = @"
            SELECT substr(je.TransactionDate, 1, 10) AS Hari,
                   COALESCE(SUM(CASE WHEN a.Type = 'Revenue' THEN jl.CreditAmount - jl.DebitAmount ELSE 0 END), 0) AS Pendapatan,
                   COALESCE(SUM(CASE WHEN a.Type = 'Expense' THEN jl.DebitAmount - jl.CreditAmount ELSE 0 END), 0) AS Beban
            FROM JournalLines jl
            CROSS JOIN JournalEntries je ON je.Id = jl.JournalEntryId
            JOIN Accounts a ON a.Id = jl.AccountId
            WHERE je.Status IN ('Posted','Reversed')
              AND je.TransactionDate >= @from AND je.TransactionDate < @to
            GROUP BY Hari ORDER BY Hari;";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
        var list = new List<DailyJournalTotals>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new DailyJournalTotals(
                DateTime.Parse(r.GetString(0)), r.GetInt64(1), r.GetInt64(2)));
        return list;
    }

    public async Task<IReadOnlyList<AccountExpenseTotal>> GetExpenseTotalsAsync(DateTime from, DateTime to)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT a.Code, a.Name,
                   COALESCE(SUM(jl.DebitAmount - jl.CreditAmount), 0) AS Total
            FROM JournalLines jl
            CROSS JOIN JournalEntries je ON je.Id = jl.JournalEntryId
            JOIN Accounts a ON a.Id = jl.AccountId
            WHERE je.Status IN ('Posted','Reversed') AND a.Type = 'Expense'
              AND je.TransactionDate >= @from AND je.TransactionDate < @to
            GROUP BY a.Id, a.Code, a.Name
            ORDER BY Total DESC, a.Code;";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
        var list = new List<AccountExpenseTotal>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new AccountExpenseTotal(r.GetString(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    private async Task<List<JournalLine>> GetLinesAsync(SqliteConnection conn, int entryId)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT jl.Id, jl.JournalEntryId, jl.AccountId, jl.DebitAmount, jl.CreditAmount, jl.Notes,
                   a.Code AS AccountCode, a.Name AS AccountName, a.Type AS AccountType
            FROM JournalLines jl
            LEFT JOIN Accounts a ON a.Id = jl.AccountId
            WHERE jl.JournalEntryId = @id;";
        cmd.Parameters.AddWithValue("@id", entryId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<JournalLine>();
        while (await r.ReadAsync()) list.Add(new JournalLine
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            JournalEntryId = r.GetInt32(r.GetOrdinal("JournalEntryId")),
            AccountId = r.GetInt32(r.GetOrdinal("AccountId")),
            DebitAmount = r.GetInt64(r.GetOrdinal("DebitAmount")),
            CreditAmount = r.GetInt64(r.GetOrdinal("CreditAmount")),
            Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
            Account = new Account
            {
                Id = r.GetInt32(r.GetOrdinal("AccountId")),
                Code = r.IsDBNull(r.GetOrdinal("AccountCode")) ? string.Empty : r.GetString(r.GetOrdinal("AccountCode")),
                Name = r.IsDBNull(r.GetOrdinal("AccountName")) ? string.Empty : r.GetString(r.GetOrdinal("AccountName")),
                Type = r.IsDBNull(r.GetOrdinal("AccountType"))
                    ? AccountType.Asset
                    : Enum.Parse<AccountType>(r.GetString(r.GetOrdinal("AccountType")))
            }
        });
        return list;
    }

    private static JournalEntry MapEntry(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        TransactionDate = DateTime.Parse(r.GetString(r.GetOrdinal("TransactionDate"))),
        SourceDocumentType = r.IsDBNull(r.GetOrdinal("SourceDocumentType")) ? null : r.GetString(r.GetOrdinal("SourceDocumentType")),
        SourceDocumentId = r.IsDBNull(r.GetOrdinal("SourceDocumentId")) ? null : r.GetInt32(r.GetOrdinal("SourceDocumentId")),
        Status = Enum.Parse<JournalStatus>(r.GetString(r.GetOrdinal("Status"))),
        ReversalOfId = r.IsDBNull(r.GetOrdinal("ReversalOfId")) ? null : r.GetInt32(r.GetOrdinal("ReversalOfId")),
        PostedAt = r.IsDBNull(r.GetOrdinal("PostedAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("PostedAt"))),
        PostedByUserId = r.IsDBNull(r.GetOrdinal("PostedByUserId")) ? null : r.GetInt32(r.GetOrdinal("PostedByUserId")),
        IdempotencyKey = r.IsDBNull(r.GetOrdinal("IdempotencyKey")) ? null : r.GetString(r.GetOrdinal("IdempotencyKey")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
    };
}
