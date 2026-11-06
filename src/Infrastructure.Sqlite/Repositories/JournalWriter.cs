using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Satu-satunya tempat SQL yang menulis jurnal. Sama seperti StockLedger, pemanggil
/// menyuplai koneksi dan transaksi yang sedang berjalan, sehingga aksi pengguna
/// (pesanan + stok + jurnal) bisa commit bersama atau gagal bersama.
/// </summary>
internal static class JournalWriter
{
    public static async Task<int> WriteAsync(
        SqliteConnection conn, SqliteTransaction? tx, JournalEntry entry)
    {
        if (entry.Lines.Count == 0)
            throw new InvalidOperationException($"Entri jurnal tanpa kaki: {entry.Description}");
        if (!entry.IsBalanced())
            throw new InvalidOperationException($"Jurnal tidak seimbang (debit != kredit): {entry.Description}");

        foreach (var line in entry.Lines)
        {
            if (line.DebitAmount < 0 || line.CreditAmount < 0)
                throw new InvalidOperationException(
                    $"Nilai kaki jurnal tidak boleh negatif: {entry.Description} akun {line.AccountId}");
            if (line.DebitAmount > 0 && line.CreditAmount > 0)
                throw new InvalidOperationException(
                    $"Satu kaki jurnal hanya boleh debit atau kredit: {entry.Description} akun {line.AccountId}");
        }

        await EnsureAccountsUsableAsync(conn, tx, entry);
        await EnsurePeriodOpenAsync(conn, tx, entry);

        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO JournalEntries
                (Description, TransactionDate, SourceDocumentType, SourceDocumentId,
                 Status, ReversalOfId, PostedAt, PostedByUserId, IdempotencyKey, CreatedAt)
            VALUES (@desc, @td, @sdt, @sdi, @st, @roi, @pa, @uid, @ik, @ca);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@desc", entry.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@td", entry.TransactionDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@sdt", entry.SourceDocumentType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@sdi", entry.SourceDocumentId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@st", entry.Status.ToString());
        cmd.Parameters.AddWithValue("@roi", entry.ReversalOfId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@pa", entry.PostedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@uid", entry.PostedByUserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ik", entry.IdempotencyKey ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", entry.CreatedAt.ToString("o"));

        var entryIdRaw = await cmd.ExecuteScalarAsync();
        int entryId = Convert.ToInt32(entryIdRaw);

        foreach (var line in entry.Lines)
        {
            var lCmd = conn.CreateCommand();
            lCmd.Transaction = tx;
            lCmd.CommandText = @"
                INSERT INTO JournalLines (JournalEntryId, AccountId, DebitAmount, CreditAmount, Notes)
                VALUES (@eid, @aid, @dr, @cr, @notes);";
            lCmd.Parameters.AddWithValue("@eid", entryId);
            lCmd.Parameters.AddWithValue("@aid", line.AccountId);
            lCmd.Parameters.AddWithValue("@dr", line.DebitAmount);
            lCmd.Parameters.AddWithValue("@cr", line.CreditAmount);
            lCmd.Parameters.AddWithValue("@notes", line.Notes ?? (object)DBNull.Value);
            await lCmd.ExecuteNonQueryAsync();
        }

        if (entry.ReversalOfId is { } originalId)
            await MarkReversedAsync(conn, tx, originalId, entry.Description);

        return entryId;
    }

    /// <summary>
    /// Entri asli ikut diberi status Reversed supaya saldo hanya menghitung satu arah.
    /// Dua pembalik untuk entri yang sama berarti uang keluar dua kali, jadi ditolak di sini.
    /// </summary>
    private static async Task MarkReversedAsync(
        SqliteConnection conn, SqliteTransaction? tx, int originalId, string? reversalDescription)
    {
        var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "SELECT Status FROM JournalEntries WHERE Id = @id;";
        check.Parameters.AddWithValue("@id", originalId);
        var status = await check.ExecuteScalarAsync();

        if (status is null or DBNull)
            throw new InvalidOperationException($"Entri asli Id {originalId} untuk pembalik tidak ditemukan.");
        if (status.ToString() == nameof(JournalStatus.Reversed))
            throw new InvalidOperationException(
                $"Entri Id {originalId} sudah pernah dibalik, pembatalan ganda ditolak ({reversalDescription}).");

        var update = conn.CreateCommand();
        update.Transaction = tx;
        update.CommandText = "UPDATE JournalEntries SET Status = 'Reversed' WHERE Id = @id;";
        update.Parameters.AddWithValue("@id", originalId);
        await update.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Pagar periode ditanyakan di sini, di satu-satunya tempat jurnal ditulis, karena aksi yang
    /// sama bisa datang dari layar Kasir, Pembelian, Produksi, Keuangan, maupun MCP. Kalau setiap
    /// jalur yang memeriksa, satu layar yang lupa sudah cukup untuk membuat bulan tertutup kembali
    /// bisa diisi. Pesan memuat nama penutup dan waktunya supaya pengguna tahu ke siapa harus
    /// bertanya, bukan sekadar "ditolak".
    /// </summary>
    private static async Task EnsurePeriodOpenAsync(
        SqliteConnection conn, SqliteTransaction? tx, JournalEntry entry)
    {
        var tanggal = entry.TransactionDate.ToString("yyyy-MM-dd");

        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT fp.Name, fp.StartDate, fp.EndDate, fp.ClosedAt, u.Username
              FROM FiscalPeriods fp
              LEFT JOIN Users u ON u.Id = fp.ClosedByUserId
             WHERE fp.Status IN ('Closed', 'Locked')
               AND fp.StartDate <= @td AND fp.EndDate >= @td
             ORDER BY fp.StartDate
             LIMIT 1;";
        cmd.Parameters.AddWithValue("@td", tanggal);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return;

        var nama = reader.GetString(0);
        var mulai = reader.GetString(1);
        var akhir = reader.GetString(2);
        var siapa = reader.IsDBNull(4) ? "pengguna yang sudah dihapus" : reader.GetString(4);
        var kapan = reader.IsDBNull(3)
            ? "waktu tidak tercatat"
            : TimeZoneInfo.ConvertTimeFromUtc(BusinessTime.BacaCapWaktu(reader.GetString(3)), BusinessTime.Zone)
                .ToString("yyyy-MM-dd HH:mm");

        throw new PeriodClosedException(
            $"Periode {nama} ({mulai} s/d {akhir}) sudah ditutup oleh {siapa} pada {kapan}, jadi jurnal " +
            $"bertanggal {tanggal} tidak bisa ditulis. Catat pada periode yang masih terbuka, atau minta " +
            "Owner membuka kembali periode itu lewat Keuangan › Laporan keuangan › Periode buku.");
    }

    // Foreign key menjaga akun tetap ada, tetapi tidak menjaga akun masih dipakai:
    // posting ke akun nonaktif menghasilkan jurnal yang tampak sah tetapi salah baca di laporan.
    private static async Task EnsureAccountsUsableAsync(
        SqliteConnection conn, SqliteTransaction? tx, JournalEntry entry)
    {
        var ids = entry.Lines.Select(l => l.AccountId).Distinct().ToArray();
        if (ids.Length == 0) return;

        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var names = ids.Select((_, i) => "@a" + i).ToArray();
        cmd.CommandText = $"SELECT Id, Code, Name, IsActive FROM Accounts WHERE Id IN ({string.Join(",", names)});";
        for (var i = 0; i < ids.Length; i++)
            cmd.Parameters.AddWithValue(names[i], ids[i]);

        var found = new Dictionary<int, (string Code, string Name, bool IsActive)>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                found[reader.GetInt32(0)] = (reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1);
        }

        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var account))
                throw new InvalidOperationException($"Akun jurnal Id {id} tidak ada di bagan akun.");
            if (!account.IsActive)
                throw new InvalidOperationException(
                    $"Akun {account.Code} · {account.Name} sudah dinonaktifkan, tidak bisa dipakai membukukan {entry.Description}.");
        }
    }
}
