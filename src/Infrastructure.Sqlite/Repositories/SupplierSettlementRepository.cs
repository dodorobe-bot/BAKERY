using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Hutang pemasok dibaca dan diturunkan per struk penerimaan, karena kredit 2-002 lahir per struk
/// (jurnal penerimaan menautkan SourceDocumentId ke GoodsReceiptId). Retur dan pembayaran ditulis
/// bersama mutasi stok dan jurnalnya dalam satu transaksi, sehingga saldo struk tidak pernah turun
/// tanpa barang atau uang yang ikut bergerak.
/// </summary>
public class SupplierSettlementRepository : ISupplierSettlementRepository
{
    private const string UtangUsahaKode = "2-002";
    private const string KasBankKode = "1-001";

    private readonly DbConnectionFactory _factory;
    public SupplierSettlementRepository(DbConnectionFactory factory) => _factory = factory;

    public Task<IReadOnlyList<SupplierPayableView>> GetPayablesAsync(bool includeLunas = false, int limit = 200)
        => ReadPayablesAsync(null, includeLunas, limit);

    public async Task<SupplierPayableView?> GetPayableAsync(int goodsReceiptId)
    {
        var satu = await ReadPayablesAsync(goodsReceiptId, includeLunas: true, limit: 1);
        return satu.Count == 0 ? null : satu[0];
    }

    /// <summary>
    /// Struk dipilih dan dibatasi lebih dulu di CTE, baru barisnya di-join: LIMIT harus menghitung
    /// struk, bukan baris penerimaan, supaya satu struk besar tidak mengusir struk lain dari daftar.
    /// </summary>
    private async Task<IReadOnlyList<SupplierPayableView>> ReadPayablesAsync(
        int? goodsReceiptId, bool includeLunas, int limit)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            WITH struk AS (
                SELECT gr.Id GrId, gr.PurchaseOrderId PoId, po.PoNumber, po.SupplierId, s.Name SupplierName,
                       gr.ReceivedDate,
                       (SELECT COALESCE(SUM(CAST(ROUND(gl.ReceivedQty * gl.UnitCostMinorUnits) AS INTEGER)), 0)
                          FROM GoodsReceiptLines gl WHERE gl.GoodsReceiptId = gr.Id) NilaiTerima,
                       (SELECT COALESCE(SUM(l.ValueMinorUnits), 0)
                          FROM SupplierReturnLines l
                          JOIN GoodsReceiptLines gl ON gl.Id = l.GoodsReceiptLineId
                         WHERE gl.GoodsReceiptId = gr.Id) NilaiRetur,
                       (SELECT COALESCE(SUM(p.Amount), 0)
                          FROM SupplierPayments p WHERE p.GoodsReceiptId = gr.Id) NilaiBayar
                FROM GoodsReceipts gr
                JOIN PurchaseOrders po ON po.Id = gr.PurchaseOrderId
                LEFT JOIN Suppliers s ON s.Id = po.SupplierId
                WHERE (@GrId IS NULL OR gr.Id = @GrId)
            ),
            dipilih AS (
                SELECT * FROM struk
                WHERE (@Lunas = 1 OR NilaiTerima - NilaiRetur - NilaiBayar > 0)
                ORDER BY ReceivedDate DESC, GrId DESC
                LIMIT @Limit
            )
            SELECT d.GrId, d.PoId, d.PoNumber, d.SupplierId, d.SupplierName, d.ReceivedDate,
                   d.NilaiTerima, d.NilaiRetur, d.NilaiBayar,
                   gl.Id, gl.IngredientId, COALESCE(i.Name, ''), COALESCE(i.Unit, ''),
                   gl.LocationId, COALESCE(sl.Name, ''), gl.ReceivedQty, gl.UnitCostMinorUnits,
                   (SELECT COALESCE(SUM(l.Quantity), 0)
                      FROM SupplierReturnLines l WHERE l.GoodsReceiptLineId = gl.Id) JumlahRetur
            FROM dipilih d
            LEFT JOIN GoodsReceiptLines gl ON gl.GoodsReceiptId = d.GrId
            LEFT JOIN Ingredients i ON i.Id = gl.IngredientId
            LEFT JOIN StorageLocations sl ON sl.Id = gl.LocationId
            ORDER BY d.ReceivedDate DESC, d.GrId DESC, gl.Id;";
        cmd.Parameters.AddWithValue("@GrId", goodsReceiptId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Lunas", includeLunas ? 1 : 0);
        cmd.Parameters.AddWithValue("@Limit", limit);

        var urutan = new List<int>();
        var kepala = new Dictionary<int, (int PurchaseOrderId, string? PoNumber, int? SupplierId,
            string? SupplierName, DateTime ReceivedAt, long Terima, long Retur, long Bayar)>();
        var baris = new Dictionary<int, List<PayableReceiptLineView>>();

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var grId = r.GetInt32(0);
            if (!kepala.ContainsKey(grId))
            {
                urutan.Add(grId);
                kepala[grId] = (r.GetInt32(1), IsNull(r, 2) ? null : r.GetString(2),
                    IsNull(r, 3) ? null : r.GetInt32(3), IsNull(r, 4) ? null : r.GetString(4),
                    BusinessTime.BacaCapWaktu(r.GetString(5)), r.GetInt64(6), r.GetInt64(7), r.GetInt64(8));
                baris[grId] = new List<PayableReceiptLineView>();
            }

            // LEFT JOIN: struk tanpa baris tetap muncul sebagai kartu utang kosong, bukan hilang.
            if (IsNull(r, 9)) continue;
            var terima = Convert.ToDecimal(r.GetValue(15));
            var harga = r.GetInt64(16);
            baris[grId].Add(new PayableReceiptLineView(
                r.GetInt32(9), r.GetInt32(10), r.GetString(11), r.GetString(12),
                r.GetInt32(13), r.GetString(14), terima, Convert.ToDecimal(r.GetValue(17)),
                harga, NilaiBaris(terima, harga)));
        }

        return urutan.Select(grId =>
        {
            var k = kepala[grId];
            var lines = baris[grId];
            return new SupplierPayableView(grId, k.PurchaseOrderId, k.PoNumber, k.SupplierId, k.SupplierName,
                k.ReceivedAt, k.Terima, k.Retur, k.Bayar, k.Terima - k.Retur - k.Bayar, lines);
        }).ToList();
    }

    public async Task<IReadOnlyList<SupplierReturnView>> GetReturnsAsync(
        int? goodsReceiptId = null, int limit = 200)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            WITH dokumen AS (
                SELECT r.Id, r.ReturnNumber, r.GoodsReceiptId, r.PurchaseOrderId, po.PoNumber,
                       s.Name SupplierName, r.Reason, r.Notes, r.ReturnedAt, u.Username,
                       (SELECT COALESCE(SUM(l.ValueMinorUnits), 0)
                          FROM SupplierReturnLines l WHERE l.SupplierReturnId = r.Id) Nilai
                FROM SupplierReturns r
                JOIN PurchaseOrders po ON po.Id = r.PurchaseOrderId
                LEFT JOIN Suppliers s ON s.Id = po.SupplierId
                LEFT JOIN Users u ON u.Id = r.CreatedByUserId
                WHERE (@GrId IS NULL OR r.GoodsReceiptId = @GrId)
                ORDER BY r.ReturnedAt DESC, r.Id DESC
                LIMIT @Limit
            )
            SELECT d.Id, d.ReturnNumber, d.GoodsReceiptId, d.PurchaseOrderId, d.PoNumber,
                   d.SupplierName, d.Reason, d.Notes, d.Nilai, d.ReturnedAt, d.Username,
                   rl.GoodsReceiptLineId, rl.IngredientId, COALESCE(i.Name, ''), COALESCE(i.Unit, ''),
                   rl.Quantity, rl.UnitCostMinorUnits, rl.ValueMinorUnits
            FROM dokumen d
            LEFT JOIN SupplierReturnLines rl ON rl.SupplierReturnId = d.Id
            LEFT JOIN Ingredients i ON i.Id = rl.IngredientId
            ORDER BY d.ReturnedAt DESC, d.Id DESC, rl.GoodsReceiptLineId;";
        cmd.Parameters.AddWithValue("@GrId", goodsReceiptId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Limit", limit);

        var urutan = new List<int>();
        var kepala = new Dictionary<int, (string ReturnNumber, int GrId, int PoId, string? PoNumber,
            string? SupplierName, string Reason, string? Notes, long Nilai, DateTime ReturnedAt, string? User)>();
        var baris = new Dictionary<int, List<SupplierReturnLineView>>();

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            if (!kepala.ContainsKey(id))
            {
                urutan.Add(id);
                kepala[id] = (r.GetString(1), r.GetInt32(2), r.GetInt32(3),
                    IsNull(r, 4) ? null : r.GetString(4), IsNull(r, 5) ? null : r.GetString(5),
                    r.GetString(6), IsNull(r, 7) ? null : r.GetString(7), r.GetInt64(8),
                    BusinessTime.BacaCapWaktu(r.GetString(9)), IsNull(r, 10) ? null : r.GetString(10));
                baris[id] = new List<SupplierReturnLineView>();
            }

            if (IsNull(r, 11)) continue;
            var qty = Convert.ToDecimal(r.GetValue(15));
            var harga = r.GetInt64(16);
            baris[id].Add(new SupplierReturnLineView(
                r.GetInt32(11), r.GetInt32(12), IsNull(r, 13) ? null : r.GetString(13), r.GetString(14),
                qty, harga, r.GetInt64(17)));
        }

        return urutan.Select(id =>
        {
            var k = kepala[id];
            return new SupplierReturnView(id, k.ReturnNumber, k.GrId, k.PoId, k.PoNumber, k.SupplierName,
                k.Reason, k.Notes, k.Nilai, k.ReturnedAt, k.User, baris[id]);
        }).ToList();
    }

    public async Task<IReadOnlyList<SupplierPaymentView>> GetPaymentsAsync(
        int? goodsReceiptId = null, int limit = 200)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.PaymentNumber, p.GoodsReceiptId, po.Id, po.PoNumber, s.Name,
                   p.Amount, p.Method, p.Notes, p.PaidAt, u.Username
            FROM SupplierPayments p
            JOIN GoodsReceipts gr ON gr.Id = p.GoodsReceiptId
            JOIN PurchaseOrders po ON po.Id = gr.PurchaseOrderId
            LEFT JOIN Suppliers s ON s.Id = po.SupplierId
            LEFT JOIN Users u ON u.Id = p.CreatedByUserId
            WHERE (@GrId IS NULL OR p.GoodsReceiptId = @GrId)
            ORDER BY p.PaidAt DESC, p.Id DESC
            LIMIT @Limit;";
        cmd.Parameters.AddWithValue("@GrId", goodsReceiptId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Limit", limit);

        var list = new List<SupplierPaymentView>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SupplierPaymentView(
                r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3),
                IsNull(r, 4) ? null : r.GetString(4), IsNull(r, 5) ? null : r.GetString(5),
                r.GetInt64(6), r.GetString(7), IsNull(r, 8) ? null : r.GetString(8),
                BusinessTime.BacaCapWaktu(r.GetString(9)), IsNull(r, 10) ? null : r.GetString(10)));
        }
        return list;
    }

    public Task<int> CreateReturnWithJournalAsync(SupplierReturn ret, JournalEntry? journal)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            // Sisa hutang dan harga baris dibaca ulang di dalam transaksi. Permintaan yang disusun
            // dari daftar utang yang sudah basi tidak boleh melepas hutang yang barusan dibayar atau
            // diretur jalur lain, dan harga struk tidak pernah diambil dari sisi pemanggil.
            var struk = await ReadStrukStateAsync(conn, tx, ret.GoodsReceiptId)
                ?? throw new InvalidOperationException("Struk penerimaan tidak ditemukan.");
            var sisa = struk.Terima - struk.Retur - struk.Bayar;
            if (sisa <= 0)
                throw new InvalidOperationException(struk.Bayar > 0
                    ? "Hutang struk ini sudah lunas; retur tidak bisa mengurangi saldo di bawah nol."
                    : "Seluruh hutang struk ini sudah diretur; tidak ada sisa yang bisa dikurangi.");
            if (ret.Lines.Count == 0)
                throw new InvalidOperationException("Tidak ada baris barang yang diretur.");

            ret.PurchaseOrderId = struk.PurchaseOrderId;
            var nilaiRetur = 0L;
            var nilaiPerAkun = new Dictionary<string, long>();
            foreach (var line in ret.Lines)
            {
                if (!struk.Baris.TryGetValue(line.GoodsReceiptLineId, out var asal))
                    throw new InvalidOperationException(
                        $"Baris penerimaan Id {line.GoodsReceiptLineId} tidak termasuk struk ini.");
                if (asal.IngredientId != line.IngredientId)
                    throw new InvalidOperationException(
                        $"Baris penerimaan Id {line.GoodsReceiptLineId} diisi bahan lain (Id {line.IngredientId}).");
                if (line.Quantity <= 0)
                    throw new InvalidOperationException("Jumlah yang diretur harus lebih besar dari nol.");

                var sisaJumlah = asal.ReceivedQty - asal.ReturnedQty;
                if (line.Quantity > sisaJumlah)
                    throw new InvalidOperationException(
                        $"Jumlah retur {line.Quantity:0.###} melebihi sisa yang bisa diretur pada baris ini " +
                        $"({sisaJumlah:0.###}). Muat ulang struk lalu ulangi.");

                line.UnitCostMinorUnits = asal.UnitCost;
                line.ValueMinorUnits = NilaiBaris(line.Quantity, asal.UnitCost);
                nilaiRetur += line.ValueMinorUnits;
                var kode = await AccountingPoster.InventoryCodeForLocationAsync(conn, tx, line.LocationId);
                nilaiPerAkun[kode] = nilaiPerAkun.GetValueOrDefault(kode) + line.ValueMinorUnits;
            }

            if (nilaiRetur > sisa)
                throw new InvalidOperationException(
                    $"Nilai retur {Money.Format(nilaiRetur)} melebihi sisa hutang struk ini ({Money.Format(sisa)}). " +
                    "Muat ulang struk lalu ulangi.");
            await VerifikasiJurnalReturAsync(conn, tx, journal, nilaiPerAkun, nilaiRetur);

            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO SupplierReturns
                    (ReturnNumber, GoodsReceiptId, PurchaseOrderId, Reason, Notes, ReturnedAt,
                     IdempotencyKey, CreatedByUserId, CreatedAt)
                VALUES (@number, @grId, @poId, @reason, @notes, @returnedAt, @key, @userId, datetime('now'));
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@number", ret.ReturnNumber);
            cmd.Parameters.AddWithValue("@grId", ret.GoodsReceiptId);
            cmd.Parameters.AddWithValue("@poId", ret.PurchaseOrderId);
            cmd.Parameters.AddWithValue("@reason", ret.Reason);
            cmd.Parameters.AddWithValue("@notes", ret.Notes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@returnedAt", ret.ReturnedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@key", ret.IdempotencyKey ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@userId", ret.CreatedByUserId ?? (object)DBNull.Value);
            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            foreach (var line in ret.Lines)
            {
                var lCmd = conn.CreateCommand();
                lCmd.Transaction = tx;
                lCmd.CommandText = @"
                    INSERT INTO SupplierReturnLines
                        (SupplierReturnId, GoodsReceiptLineId, IngredientId, LocationId,
                         Quantity, UnitCostMinorUnits, ValueMinorUnits)
                    VALUES (@retId, @baris, @ingId, @locId, @qty, @unitCost, @nilai);";
                lCmd.Parameters.AddWithValue("@retId", id);
                lCmd.Parameters.AddWithValue("@baris", line.GoodsReceiptLineId);
                lCmd.Parameters.AddWithValue("@ingId", line.IngredientId);
                lCmd.Parameters.AddWithValue("@locId", line.LocationId);
                lCmd.Parameters.AddWithValue("@qty", line.Quantity);
                lCmd.Parameters.AddWithValue("@unitCost", line.UnitCostMinorUnits);
                lCmd.Parameters.AddWithValue("@nilai", line.ValueMinorUnits);
                await lCmd.ExecuteNonQueryAsync();
            }

            // Barang keluar dari lokasi yang tercatat di baris retur, dan nilai yang bergerak dari
            // persediaan adalah lapisan FIFO yang benar-benar habis — bukan harga struk. Selisih
            // keduanya dikumpulkan per akun persediaan dan ditutup dengan jurnal selisih di bawah,
            // supaya saldo 1-003/1-004 tetap sama persis dengan total lapisan.
            var selisihPerAkun = new Dictionary<string, long>();
            foreach (var line in ret.Lines)
            {
                var nilaiLapisan = await StockLedger.ApplyAsync(conn, tx, line.LocationId, line.IngredientId,
                    -line.Quantity, MovementType.Deduction, "SupplierReturn", id, ret.CreatedByUserId,
                    $"Retur {ret.ReturnNumber} · struk penerimaan #{ret.GoodsReceiptId}");
                var kode = await AccountingPoster.InventoryCodeForLocationAsync(conn, tx, line.LocationId);
                selisihPerAkun[kode] = selisihPerAkun.GetValueOrDefault(kode) + nilaiLapisan - line.ValueMinorUnits;
            }

            if (journal is not null)
            {
                journal.SourceDocumentId ??= id;
                var journalId = await JournalWriter.WriteAsync(conn, tx, journal);
                using var tautkan = conn.CreateCommand();
                tautkan.Transaction = tx;
                tautkan.CommandText = "UPDATE SupplierReturns SET JournalEntryId = @je WHERE Id = @id;";
                tautkan.Parameters.AddWithValue("@je", journalId);
                tautkan.Parameters.AddWithValue("@id", id);
                await tautkan.ExecuteNonQueryAsync();
            }

            foreach (var (kode, selisih) in selisihPerAkun.OrderBy(k => k.Key, StringComparer.Ordinal))
                await AccountingPoster.WriteSupplierReturnVarianceAsync(conn, tx, id, kode, selisih, ret.CreatedByUserId);

            return id;
        });

    public Task<int> CreatePaymentWithJournalAsync(SupplierPayment payment, JournalEntry journal)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            var struk = await ReadStrukStateAsync(conn, tx, payment.GoodsReceiptId)
                ?? throw new InvalidOperationException("Struk penerimaan tidak ditemukan.");
            var sisa = struk.Terima - struk.Retur - struk.Bayar;
            if (sisa <= 0)
                throw new InvalidOperationException(
                    "Hutang struk ini sudah lunas; tidak ada yang perlu dibayar lagi.");
            if (payment.AmountMinorUnits > sisa)
                throw new InvalidOperationException(
                    $"Pembayaran {Money.Format(payment.AmountMinorUnits)} melebihi sisa hutang struk ini ({Money.Format(sisa)}). " +
                    "Muat ulang struk lalu ulangi.");

            var utangId = await AccountingPoster.AccountIdAsync(conn, tx, UtangUsahaKode);
            var kasId = await AccountingPoster.AccountIdAsync(conn, tx, KasBankKode);
            var debitUtang = journal.Lines.Where(l => l.AccountId == utangId).Sum(l => l.DebitAmount);
            var kreditKas = journal.Lines.Where(l => l.AccountId == kasId).Sum(l => l.CreditAmount);
            if (debitUtang != payment.AmountMinorUnits || kreditKas != payment.AmountMinorUnits)
                throw new InvalidOperationException(
                    "Jurnal pembayaran tidak cocok dengan nominal yang dibayar. Muat ulang struk lalu ulangi.");

            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO SupplierPayments
                    (PaymentNumber, GoodsReceiptId, Amount, Method, Notes, PaidAt,
                     IdempotencyKey, CreatedByUserId, CreatedAt)
                VALUES (@number, @grId, @amount, @method, @notes, @paidAt, @key, @userId, datetime('now'));
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@number", payment.PaymentNumber);
            cmd.Parameters.AddWithValue("@grId", payment.GoodsReceiptId);
            cmd.Parameters.AddWithValue("@amount", payment.AmountMinorUnits);
            cmd.Parameters.AddWithValue("@method", payment.Method);
            cmd.Parameters.AddWithValue("@notes", payment.Notes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@paidAt", payment.PaidAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@key", payment.IdempotencyKey ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@userId", payment.CreatedByUserId ?? (object)DBNull.Value);
            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            journal.SourceDocumentId ??= id;
            var journalId = await JournalWriter.WriteAsync(conn, tx, journal);
            using var tautkan = conn.CreateCommand();
            tautkan.Transaction = tx;
            tautkan.CommandText = "UPDATE SupplierPayments SET JournalEntryId = @je WHERE Id = @id;";
            tautkan.Parameters.AddWithValue("@je", journalId);
            tautkan.Parameters.AddWithValue("@id", id);
            await tautkan.ExecuteNonQueryAsync();

            return id;
        });

    /// <summary>
    /// Jurnal dibentuk oleh layanan dari angka yang dilihat pengguna, sedangkan nilai yang sah
    /// baru dihitung di sini. Kalau keduanya berbeda, pengguna diminta memuat ulang struk:
    /// memposting jurnal yang tidak sama dengan barisnya berarti 2-002 turun sebesar angka yang
    /// tidak bisa dijelaskan dokumen.
    /// </summary>
    private static async Task VerifikasiJurnalReturAsync(
        SqliteConnection conn, SqliteTransaction tx, JournalEntry? journal,
        Dictionary<string, long> nilaiPerAkun, long nilaiRetur)
    {
        // Retur barang bernilai nol (kado/diskon penuh di struk) tidak punya hutang yang dilepas,
        // jadi tidak ada jurnal utama — hanya jurnal selisih kalau lapisan yang habis berharga.
        if (journal is null)
        {
            if (nilaiRetur > 0)
                throw new InvalidOperationException(
                    "Jurnal retur tidak ikut dibentuk. Muat ulang struk lalu ulangi.");
            return;
        }

        var utangId = await AccountingPoster.AccountIdAsync(conn, tx, UtangUsahaKode);
        var debitUtang = journal.Lines.Where(l => l.AccountId == utangId).Sum(l => l.DebitAmount);
        if (debitUtang != nilaiRetur)
            throw new InvalidOperationException(
                $"Hutang yang dilepas jurnal ({Money.Format(debitUtang)}) tidak sama dengan nilai retur " +
                $"({Money.Format(nilaiRetur)}). Muat ulang struk lalu ulangi.");

        foreach (var (kode, nilai) in nilaiPerAkun)
        {
            if (nilai <= 0) continue;
            var akunId = await AccountingPoster.AccountIdAsync(conn, tx, kode);
            var kredit = journal.Lines.Where(l => l.AccountId == akunId).Sum(l => l.CreditAmount);
            if (kredit != nilai)
                throw new InvalidOperationException(
                    $"Kredit akun {kode} di jurnal ({Money.Format(kredit)}) tidak sama dengan nilai retur " +
                    $"dari baris struk ({Money.Format(nilai)}). Muat ulang struk lalu ulangi.");
        }
    }

    private sealed record BarisStruk(int IngredientId, decimal ReceivedQty, decimal ReturnedQty, long UnitCost);

    private sealed record StrukState(
        int PurchaseOrderId, long Terima, long Retur, long Bayar, Dictionary<int, BarisStruk> Baris);

    private static async Task<StrukState?> ReadStrukStateAsync(
        SqliteConnection conn, SqliteTransaction tx, int goodsReceiptId)
    {
        var header = conn.CreateCommand();
        header.Transaction = tx;
        header.CommandText = @"
            SELECT gr.PurchaseOrderId,
                   (SELECT COALESCE(SUM(CAST(ROUND(gl.ReceivedQty * gl.UnitCostMinorUnits) AS INTEGER)), 0)
                      FROM GoodsReceiptLines gl WHERE gl.GoodsReceiptId = gr.Id),
                   (SELECT COALESCE(SUM(l.ValueMinorUnits), 0)
                      FROM SupplierReturnLines l
                      JOIN GoodsReceiptLines gl ON gl.Id = l.GoodsReceiptLineId
                     WHERE gl.GoodsReceiptId = gr.Id),
                   (SELECT COALESCE(SUM(p.Amount), 0)
                      FROM SupplierPayments p WHERE p.GoodsReceiptId = gr.Id)
            FROM GoodsReceipts gr WHERE gr.Id = @id;";
        header.Parameters.AddWithValue("@id", goodsReceiptId);
        long terima, retur, bayar;
        int purchaseOrderId;
        using (var r = await header.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return null;
            purchaseOrderId = r.GetInt32(0);
            terima = r.GetInt64(1);
            retur = r.GetInt64(2);
            bayar = r.GetInt64(3);
        }

        var baris = new Dictionary<int, BarisStruk>();
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT gl.Id, gl.IngredientId, gl.ReceivedQty, gl.UnitCostMinorUnits,
                   (SELECT COALESCE(SUM(l.Quantity), 0)
                      FROM SupplierReturnLines l WHERE l.GoodsReceiptLineId = gl.Id)
            FROM GoodsReceiptLines gl WHERE gl.GoodsReceiptId = @id;";
        cmd.Parameters.AddWithValue("@id", goodsReceiptId);
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                baris[r.GetInt32(0)] = new BarisStruk(r.GetInt32(1), Convert.ToDecimal(r.GetValue(2)),
                    Convert.ToDecimal(r.GetValue(4)), r.GetInt64(3));
        }

        return new StrukState(purchaseOrderId, terima, retur, bayar, baris);
    }

    /// <summary>
    /// Pembulatan per baris sama seperti struk penerimaannya dihitung: total hutang adalah jumlah
    /// nilai baris, bukan nilai hasil pembulatan total.
    /// </summary>
    private static long NilaiBaris(decimal quantity, long unitCostMinorUnits)
        => (long)Math.Round(quantity * unitCostMinorUnits, 0, MidpointRounding.AwayFromZero);

    private static bool IsNull(SqliteDataReader r, int ordinal) => r.IsDBNull(ordinal);
}
