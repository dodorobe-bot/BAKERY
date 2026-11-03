using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class SalesOrderRepository : ISalesOrderRepository
{
    private readonly DbConnectionFactory _factory;
    public SalesOrderRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<SalesOrder?> GetByIdAsync(int id)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM SalesOrders WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        if (!r.Read()) return null;
        var order = MapOrder(r);
        r.Close();
        order.Payments = await GetPaymentsAsync(conn, id);
        return order;
    }

    public async Task<IReadOnlyList<SalesOrder>> GetAllAsync(int limit = 100, int? createdByUserId = null, int offset = 0)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = createdByUserId is null
            ? "SELECT * FROM SalesOrders ORDER BY PickupDate DESC, Id DESC LIMIT @limit OFFSET @offset;"
            : "SELECT * FROM SalesOrders WHERE CreatedByUserId = @userId ORDER BY PickupDate DESC, Id DESC LIMIT @limit OFFSET @offset;";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", Math.Max(0, offset));
        if (createdByUserId is not null)
            cmd.Parameters.AddWithValue("@userId", createdByUserId.Value);
        var list = await ReadOrdersAsync(conn, cmd);
        await AttachPaymentsAsync(conn, list);
        return list;
    }

    public async Task<int> CountAsync(int? createdByUserId = null)
    {
        using var conn = _factory.CreateConnection();
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = createdByUserId is null
            ? "SELECT COUNT(*) FROM SalesOrders;"
            : "SELECT COUNT(*) FROM SalesOrders WHERE CreatedByUserId = @userId;";
        if (createdByUserId is not null)
            cmd.Parameters.AddWithValue("@userId", createdByUserId.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<SalesOrder>> GetUpcomingAsync(DateTime from)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM SalesOrders
            WHERE PickupDate >= @from AND Status != 'Cancelled'
            ORDER BY PickupDate ASC LIMIT 50;";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        var list = await ReadOrdersAsync(conn, cmd);
        await AttachPaymentsAsync(conn, list);
        return list;
    }

    public async Task<IReadOnlyList<SalesOrder>> GetByPickupDateRangeAsync(DateTime from, DateTime to)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // PickupDate disimpan 'yyyy-MM-dd'. Batas atas setengah terbuka supaya seluruh jam pada
        // tanggal penutup ikut; tanpa itu laporan periode berakhir selalu kehilangan hari terakhir.
        cmd.CommandText = @"
            SELECT * FROM SalesOrders
            WHERE PickupDate >= @from AND PickupDate < @to
            ORDER BY PickupDate ASC, Id ASC;";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.Date.AddDays(1).ToString("yyyy-MM-dd"));
        var list = await ReadOrdersAsync(conn, cmd);
        await AttachPaymentsAsync(conn, list);
        return list;
    }

    public async Task<OutstandingSummary> GetOutstandingSummaryAsync()
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*), COALESCE(SUM({RemainingExpr}), 0)
            FROM SalesOrders o
            WHERE {NotSettledFilter} AND {RemainingExpr} > 0;";
        using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new OutstandingSummary(r.GetInt32(0), r.GetInt64(1));
    }

    public async Task<DeadlineCounts> GetOutstandingBreakdownAsync(DateTime today, int? createdByUserId = null)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT COALESCE(SUM(CASE WHEN o.PickupDate < @hari THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN o.PickupDate >= @hari THEN 1 ELSE 0 END), 0)
            FROM SalesOrders o
            WHERE {NotSettledFilter} AND {RemainingExpr} > 0
              AND (@userId IS NULL OR o.CreatedByUserId = @userId);";
        cmd.Parameters.AddWithValue("@hari", today.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@userId", (object?)createdByUserId ?? DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new DeadlineCounts(r.GetInt32(0), r.GetInt32(1));
    }

    public Task<IReadOnlyList<SalesOrder>> GetOutstandingOverdueAsync(
        DateTime today, int limit, int? createdByUserId = null) =>
        GetOutstandingPageAsync(today, sebelum: true, limit, createdByUserId);

    public Task<IReadOnlyList<SalesOrder>> GetOutstandingUpcomingAsync(
        DateTime today, int limit, int? createdByUserId = null) =>
        GetOutstandingPageAsync(today, sebelum: false, limit, createdByUserId);

    public async Task<int> CountOutstandingAsync(int? createdByUserId = null)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*) FROM SalesOrders o
            WHERE {NotSettledFilter} AND {RemainingExpr} > 0
              AND (@userId IS NULL OR o.CreatedByUserId = @userId);";
        cmd.Parameters.AddWithValue("@userId", (object?)createdByUserId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<SalesOrder>> GetOutstandingListAsync(
        int limit, int offset, int? createdByUserId = null)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = OutstandingSql + @"
            AND (@userId IS NULL OR o.CreatedByUserId = @userId)
            ORDER BY o.PickupDate ASC, o.Id ASC LIMIT @limit OFFSET @offset;";
        cmd.Parameters.AddWithValue("@userId", (object?)createdByUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);
        var list = await ReadOrdersAsync(conn, cmd);
        await AttachPaymentsAsync(conn, list);
        return list;
    }

    private async Task<IReadOnlyList<SalesOrder>> GetOutstandingPageAsync(
        DateTime today, bool sebelum, int limit, int? createdByUserId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = OutstandingSql + $@"
            AND o.PickupDate {(sebelum ? "<" : ">=")} @hari
            AND (@userId IS NULL OR o.CreatedByUserId = @userId)
            ORDER BY o.PickupDate ASC, o.Id ASC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@hari", today.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@userId", (object?)createdByUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = await ReadOrdersAsync(conn, cmd);
        await AttachPaymentsAsync(conn, list);
        return list;
    }

    public async Task<IReadOnlyList<DailySalesSummary>> GetDailySalesAsync(DateTime from, DateTime to)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Rentang datang sebagai tanggal kalender toko, sedangkan CreatedAt disimpan UTC.
        // Batasnya diterjemahkan dulu ke UTC, lalu jam UTC tiap baris dipetakan balik ke tanggal
        // toko. Cara lama (substr tanggal UTC) menaruh penjualan 00:00-06:59 WIB pada hari kemarin.
        var (dariHari, sampaiHari, dariTeks, sampaiTeks) = RentangTokoUtc(from, to);
        cmd.CommandText = @"
            SELECT substr(o.CreatedAt, 1, 13) AS Jam,
                   SUM(CASE WHEN o.Status <> 'Cancelled' THEN 1 ELSE 0 END) AS JumlahPesanan,
                   SUM(CASE WHEN o.Status = 'Cancelled' THEN 1 ELSE 0 END) AS JumlahBatal,
                   COALESCE(SUM(CASE WHEN o.Status <> 'Cancelled' THEN o.Quantity ELSE 0 END), 0) AS Kuantitas,
                   COALESCE(SUM(CASE WHEN o.Status <> 'Cancelled' THEN o.TotalAmount ELSE 0 END), 0) AS Nilai
            FROM SalesOrders o
            WHERE o.CreatedAt >= @dariHari AND o.CreatedAt < @sampaiHari
              AND replace(substr(o.CreatedAt, 1, 19), 'T', ' ') >= @dariTeks
              AND replace(substr(o.CreatedAt, 1, 19), 'T', ' ') < @sampaiTeks
            GROUP BY Jam ORDER BY Jam;";
        cmd.Parameters.AddWithValue("@dariHari", dariHari);
        cmd.Parameters.AddWithValue("@sampaiHari", sampaiHari);
        cmd.Parameters.AddWithValue("@dariTeks", dariTeks);
        cmd.Parameters.AddWithValue("@sampaiTeks", sampaiTeks);
        var list = new Dictionary<DateTime, (int Pesanan, int Batal, decimal Kuantitas, long Nilai)>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var tanggal = TanggalToko(r.GetString(0));
            var ada = list.TryGetValue(tanggal, out var terkumpul) ? terkumpul : (0, 0, 0m, 0L);
            list[tanggal] = (
                ada.Item1 + r.GetInt32(1),
                ada.Item2 + r.GetInt32(2),
                ada.Item3 + Convert.ToDecimal(r.GetValue(3)),
                ada.Item4 + r.GetInt64(4));
        }
        return list.OrderBy(h => h.Key)
            .Select(h => new DailySalesSummary(h.Key, h.Value.Pesanan, h.Value.Batal, h.Value.Kuantitas, h.Value.Nilai))
            .ToList();
    }

    public async Task<IReadOnlyList<ProductSalesSummary>> GetTopProductSalesAsync(DateTime from, DateTime to, int take)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // Produk nonaktif tidak punya nama aktif, sama seperti daftar produk yang bisa dipilih di kasir:
        // pesanannya masuk keranjang "tanpa produk terdaftar", bukan diberi nama produk yang sudah dimatikan.
        var (dariHari, sampaiHari, dariTeks, sampaiTeks) = RentangTokoUtc(from, to);
        cmd.CommandText = @"
            SELECT COALESCE(pt.Name, 'Pesanan tanpa produk terdaftar') AS Nama,
                   COUNT(*) AS JumlahPesanan,
                   COALESCE(SUM(o.Quantity), 0) AS Kuantitas,
                   COALESCE(SUM(o.TotalAmount), 0) AS Nilai
            FROM SalesOrders o
            LEFT JOIN ProductTemplates pt ON pt.Id = o.ProductTemplateId AND pt.IsActive = 1
            WHERE o.Status <> 'Cancelled' AND o.CreatedAt >= @dariHari AND o.CreatedAt < @sampaiHari
              AND replace(substr(o.CreatedAt, 1, 19), 'T', ' ') >= @dariTeks
              AND replace(substr(o.CreatedAt, 1, 19), 'T', ' ') < @sampaiTeks
            GROUP BY Nama
            ORDER BY Nilai DESC, Nama ASC
            LIMIT @take;";
        cmd.Parameters.AddWithValue("@dariHari", dariHari);
        cmd.Parameters.AddWithValue("@sampaiHari", sampaiHari);
        cmd.Parameters.AddWithValue("@dariTeks", dariTeks);
        cmd.Parameters.AddWithValue("@sampaiTeks", sampaiTeks);
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<ProductSalesSummary>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new ProductSalesSummary(
                r.GetString(0), r.GetInt32(1), Convert.ToDecimal(r.GetValue(2)), r.GetInt64(3)));
        return list;
    }

    /// <summary>
    /// Batas UTC untuk rentang tanggal toko: dua versi, "yyyy-MM-dd" untuk saringan kasar yang
    /// tetap memakai indeks CreatedAt, dan cap waktu penuh untuk saringan tepat. Versi penuh
    /// dibandingkan setelah 'T' diganti spasi supaya baris lama berformat "yyyy-MM-dd HH:mm:ss"
    /// tidak salah urut terhadap baris baru berformat ISO.
    /// </summary>
    private static (string DariHari, string SampaiHari, string DariTeks, string SampaiTeks) RentangTokoUtc(
        DateTime from, DateTime to)
    {
        var dari = BusinessTime.AwalHariUtc(from);
        var sampai = BusinessTime.AwalHariUtc(to);
        return (dari.ToString("yyyy-MM-dd"),
                sampai.Date.AddDays(1).ToString("yyyy-MM-dd"),
                dari.ToString("yyyy-MM-dd HH:mm:ss"),
                sampai.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    // Jam UTC dikelompokkan per jam, lalu tiap jam dipetakan ke tanggal toko. Untuk zona
    // ber-offset jam bulat (seluruh Indonesia) pemetaan ini tepat per menit; zona ber-offset
    // 30/45 menit hanya bisa bergeser pada jam pertama/terakhir hari itu.
    private static DateTime TanggalToko(string jamUtc)
    {
        var waktu = DateTime.ParseExact(jamUtc.Replace('T', ' '), "yyyy-MM-dd HH",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(waktu, DateTimeKind.Utc), BusinessTime.Zone).Date;
    }

    // Sisa bayar dihitung di SQL agar sama dengan RemainingAmount() setelah pembayaran dimuat.
    private const string RemainingExpr =
        "(o.TotalAmount - o.DownPayment - COALESCE((SELECT SUM(p.Amount) FROM SalesPayments p WHERE p.SalesOrderId = o.Id), 0))";

    private const string NotSettledFilter = "o.Status NOT IN ('Paid','Cancelled')";

    private const string OutstandingSql = @"
        SELECT o.* FROM SalesOrders o
        WHERE o.Status NOT IN ('Paid','Cancelled')
          AND (o.TotalAmount - o.DownPayment -
               COALESCE((SELECT SUM(p.Amount) FROM SalesPayments p WHERE p.SalesOrderId = o.Id), 0)) > 0";

    private static async Task<List<SalesOrder>> ReadOrdersAsync(SqliteConnection conn, SqliteCommand cmd)
    {
        var list = new List<SalesOrder>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapOrder(r));
        return list;
    }

    /// <summary>
    /// Daftar pesanan tanpa pembayaran membuat "sisa bayar" di layar penjualan
    /// melaporkan pelanggan yang sudah lunas sebagai masih berutang.
    /// </summary>
    private static async Task AttachPaymentsAsync(SqliteConnection conn, List<SalesOrder> orders)
    {
        if (orders.Count == 0) return;
        var names = orders.Select((_, i) => "@p" + i).ToArray();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM SalesPayments WHERE SalesOrderId IN ({string.Join(",", names)});";
        for (var i = 0; i < orders.Count; i++)
            cmd.Parameters.AddWithValue(names[i], orders[i].Id);
        var byId = orders.ToDictionary(o => o.Id);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var payment = MapPayment(r);
            byId[payment.SalesOrderId].Payments.Add(payment);
        }
    }

    public Task<int> CreateWithJournalAsync(
        SalesOrder order, IReadOnlyList<JournalEntry> journals,
        IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            // Pelanggan dan poin loyalti ikut transaksi pesanan: nomor telepon unik, jadi
            // pemeriksaan "sudah ada atau belum" di luar transaksi bisa menabrak constraint,
            // dan poin yang dihitung dari bacaan lama bisa saling menimpa.
            await LinkCustomerAsync(conn, tx, order);
            var id = await InsertOrderAsync(conn, tx, order);
            // Nomor pesanan baru diketahui di dalam transaksi, jadi tautan dokumen diisi di sini.
            foreach (var entry in journals)
            {
                entry.SourceDocumentId ??= id;
                await JournalWriter.WriteAsync(conn, tx, entry);
            }
            foreach (var change in finishedGoodsChanges)
            {
                var applied = await FinishedGoodsLedger.ApplyAsync(conn, tx, change.LocationId, change.ProductTemplateId,
                    change.Delta, change.Type, change.SourceType, id, change.UserId, change.Notes,
                    change.UnitCostMinorUnits);
                // Harga pokok hanya diketahui dari stok yang benar-benar keluar, jadi jurnalnya
                // ditulis di dalam transaksi yang sama, bukan dihitung ulang dari resep.
                if (change.Type == FinishedGoodsMovementType.Sale)
                    await AccountingPoster.WriteCostOfGoodsSoldAsync(conn, tx, id, applied.ValueMinorUnits, change.UserId);
            }
            return id;
        });

    public Task RecordPaymentWithJournalAsync(
        SalesPayment payment, IReadOnlyList<JournalEntry> journals, OrderStatus? newOrderStatus,
        IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            // Jurnal dan keputusan "lunas" disusun servis dari bacaan di luar transaksi.
            // Di sini keadaan pesanan dibaca ulang: bila pesanan sudah batal/lunas, sisa bayar
            // berubah, atau stok sudah terpotong, permintaan ditolak — bukan ditulis dengan
            // angka lama yang bisa membuat pesanan lunas dua kali.
            var state = await ReadOrderStateAsync(conn, tx, payment.SalesOrderId)
                ?? throw new InvalidOperationException("Pesanan tidak ditemukan.");
            if (state.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException("Pesanan sudah dibatalkan.");
            if (state.Remaining <= 0)
                throw new InvalidOperationException("Pesanan sudah lunas.");
            if (payment.Amount > state.Remaining)
                throw new InvalidOperationException(
                    $"Pembayaran {Money.Format(payment.Amount)} melebihi sisa bayar {Money.Format(state.Remaining)}.");

            var fullyPaid = payment.Amount == state.Remaining;
            if (fullyPaid != (newOrderStatus == OrderStatus.Paid))
                throw new InvalidOperationException(
                    "Saldo pesanan berubah saat pembayaran dicatat. Muat ulang pesanan lalu ulangi pembayaran.");

            var wantsStockDeduction = finishedGoodsChanges.Any(c => c.Type == FinishedGoodsMovementType.Sale);
            if (wantsStockDeduction && state.StockDeducted)
                throw new InvalidOperationException(
                    "Stok pesanan ini sudah dikurangi. Muat ulang pesanan lalu ulangi pembayaran.");

            int? firstEntryId = null;
            foreach (var entry in journals)
            {
                var entryId = await JournalWriter.WriteAsync(conn, tx, entry);
                firstEntryId ??= entryId;
            }
            // Pembayaran ditautkan ke entri pertamanya: baris bayar tanpa jejak jurnal adalah celah rekonsiliasi.
            payment.JournalEntryId = firstEntryId;
            await InsertPaymentAsync(conn, tx, payment);

            if (newOrderStatus is { } status
                && !await UpdateOrderStatusAsync(conn, tx, payment.SalesOrderId, status))
                throw new InvalidOperationException(
                    "Status pesanan berubah saat pembayaran dicatat. Muat ulang pesanan lalu ulangi.");

            foreach (var change in finishedGoodsChanges)
            {
                var applied = await FinishedGoodsLedger.ApplyAsync(conn, tx, change.LocationId, change.ProductTemplateId,
                    change.Delta, change.Type, change.SourceType, change.SourceId, change.UserId, change.Notes,
                    change.UnitCostMinorUnits);
                if (change.Type == FinishedGoodsMovementType.Sale)
                    await AccountingPoster.WriteCostOfGoodsSoldAsync(conn, tx, payment.SalesOrderId,
                        applied.ValueMinorUnits, change.UserId);
            }

            // Penanda sudah-dikurangi disimpan di sini supaya pembatalan nanti mengembalikan
            // stok tepat satu kali, dan pesanan ini tidak pernah dipotong dua kali.
            if (wantsStockDeduction)
            {
                using var flag = conn.CreateCommand();
                flag.Transaction = tx;
                flag.CommandText = "UPDATE SalesOrders SET StockDeducted = 1 WHERE Id = @id;";
                flag.Parameters.AddWithValue("@id", payment.SalesOrderId);
                await flag.ExecuteNonQueryAsync();
            }
        });

    public Task SaveStatusChangeWithJournalAsync(
        SalesOrder order, OrderStatus expectedStatus, bool expectedStockDeducted, long expectedTotalPaid,
        IReadOnlyList<StockChange> stockChanges,
        IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges, IReadOnlyList<JournalEntry> journals)
        => WriteTransaction.RunAsync(_factory, async (conn, tx) =>
        {
            // Keputusan stok dan jurnal pembalik disusun dari salinan pesanan saat layar dimuat.
            // Bila pesanan, penanda stok, atau total pembayarannya sudah berubah, perubahan ini
            // dibatalkan supaya stok tidak terpotong dua kali dan jurnal tidak terbalik ganda.
            var state = await ReadOrderStateAsync(conn, tx, order.Id)
                ?? throw new InvalidOperationException("Pesanan tidak ditemukan.");
            if (state.Status != expectedStatus || state.StockDeducted != expectedStockDeducted)
                throw new InvalidOperationException(
                    "Pesanan berubah di perangkat lain sejak layar dimuat. Muat ulang pesanan lalu ulangi.");
            if (state.TotalPaid != expectedTotalPaid)
                throw new InvalidOperationException(
                    "Pembayaran pesanan ini berubah sejak layar dimuat. Muat ulang pesanan lalu ulangi.");
            if (order.Status == OrderStatus.Cancelled && await HasReturnsAsync(conn, tx, order.Id))
                throw new InvalidOperationException(
                    "Pesanan sudah memiliki retur, jadi tidak bisa dibatalkan. Kembalikan sisa unit lewat retur penjualan.");

            foreach (var change in stockChanges)
                await StockLedger.ApplyAsync(conn, tx, change.LocationId, change.IngredientId, change.Delta,
                    change.Type, change.SourceType, change.SourceId, change.UserId, change.Notes);

            foreach (var change in finishedGoodsChanges)
            {
                var applied = await FinishedGoodsLedger.ApplyAsync(conn, tx, change.LocationId, change.ProductTemplateId,
                    change.Delta, change.Type, change.SourceType, change.SourceId, change.UserId, change.Notes,
                    change.UnitCostMinorUnits);
                if (change.Type == FinishedGoodsMovementType.Sale)
                    await AccountingPoster.WriteCostOfGoodsSoldAsync(conn, tx, order.Id,
                        applied.ValueMinorUnits, change.UserId);
            }

            if (!await UpdateOrderAsync(conn, tx, order, expectedStatus, expectedStockDeducted))
                throw new InvalidOperationException(
                    "Pesanan berubah di perangkat lain sejak layar dimuat. Muat ulang pesanan lalu ulangi.");

            foreach (var entry in journals)
                await JournalWriter.WriteAsync(conn, tx, entry);
        });

    private sealed record OrderState(
        OrderStatus Status, bool StockDeducted, long TotalAmount, long DownPayment, long TotalPaid)
    {
        public long Remaining => TotalAmount - TotalPaid;
    }

    private static async Task<OrderState?> ReadOrderStateAsync(
        SqliteConnection conn, SqliteTransaction tx, int orderId)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT o.Status, o.StockDeducted, o.TotalAmount, o.DownPayment,
                   COALESCE((SELECT SUM(p.Amount) FROM SalesPayments p
                              WHERE p.SalesOrderId = o.Id), 0)
            FROM SalesOrders o WHERE o.Id = @id;";
        cmd.Parameters.AddWithValue("@id", orderId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var downPayment = r.GetInt64(3);
        return new OrderState(
            Enum.Parse<OrderStatus>(r.GetString(0)),
            r.GetInt32(1) == 1,
            r.GetInt64(2),
            downPayment,
            downPayment + r.GetInt64(4));
    }

    private static async Task<bool> HasReturnsAsync(
        SqliteConnection conn, SqliteTransaction tx, int orderId)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM SalesReturns WHERE SalesOrderId = @id);";
        cmd.Parameters.AddWithValue("@id", orderId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }

    /// <summary>
    /// Tautkan pesanan ke pelanggan lewat nomor telepon dan tambahkan poinnya. Pencarian dan
    /// penambahan dilakukan di SQL dalam transaksi, jadi dua pesanan bersamaan dengan nomor
    /// baru yang sama tidak menabrak UNIQUE dan poinnya tidak ada yang hilang.
    /// </summary>
    private static async Task LinkCustomerAsync(
        SqliteConnection conn, SqliteTransaction tx, SalesOrder order)
    {
        if (string.IsNullOrWhiteSpace(order.PhoneSnapshot)) return;

        var find = conn.CreateCommand();
        find.Transaction = tx;
        find.CommandText = "SELECT Id FROM Customers WHERE Phone = @phone;";
        find.Parameters.AddWithValue("@phone", order.PhoneSnapshot);
        var found = await find.ExecuteScalarAsync();

        int customerId;
        if (found is not null && found != DBNull.Value)
        {
            customerId = Convert.ToInt32(found);
        }
        else
        {
            var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"
                INSERT INTO Customers (Name, Phone, LoyaltyPoints, IsActive, CreatedAt)
                VALUES (@name, @phone, 0, 1, @now);
                SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("@name", order.CustomerNameSnapshot);
            insert.Parameters.AddWithValue("@phone", order.PhoneSnapshot);
            insert.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            customerId = Convert.ToInt32(await insert.ExecuteScalarAsync());
        }

        order.CustomerId = customerId;

        var points = Customer.LoyaltyPointsFor(order.TotalAmount);
        if (points <= 0) return;
        var add = conn.CreateCommand();
        add.Transaction = tx;
        add.CommandText = "UPDATE Customers SET LoyaltyPoints = LoyaltyPoints + @points WHERE Id = @id;";
        add.Parameters.AddWithValue("@points", points);
        add.Parameters.AddWithValue("@id", customerId);
        await add.ExecuteNonQueryAsync();
    }

    public async Task<SalesPayment?> GetPaymentByIdempotencyKeyAsync(string key)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM SalesPayments WHERE IdempotencyKey = @k;";
        cmd.Parameters.AddWithValue("@k", key);
        using var r = await cmd.ExecuteReaderAsync();
        return r.Read() ? MapPayment(r) : null;
    }

    private static async Task<int> InsertOrderAsync(
        SqliteConnection conn, SqliteTransaction tx, SalesOrder order)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO SalesOrders
                (OrderNumber, CustomerId, ProductTemplateId, CustomerNameSnapshot, PhoneSnapshot,
                 Description, PickupDate, TotalAmount, DownPayment, Quantity, Status, StockDeducted,
                 Notes, CreatedAt, CreatedByUserId)
            VALUES (@on, @cid, @pid, @cn, @ph, @desc, @pd, @ta, @dp, @qty, @st, @sd, @notes, @ca, @uid);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@on", order.OrderNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cid", order.CustomerId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", order.ProductTemplateId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cn", order.CustomerNameSnapshot);
        cmd.Parameters.AddWithValue("@ph", order.PhoneSnapshot ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@desc", order.Description);
        cmd.Parameters.AddWithValue("@pd", order.PickupDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@ta", order.TotalAmount);
        cmd.Parameters.AddWithValue("@dp", order.DownPayment);
        cmd.Parameters.AddWithValue("@qty", order.Quantity);
        cmd.Parameters.AddWithValue("@st", order.Status.ToString());
        cmd.Parameters.AddWithValue("@sd", order.StockDeducted ? 1 : 0);
        cmd.Parameters.AddWithValue("@notes", order.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", order.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@uid", order.CreatedByUserId ?? (object)DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<bool> UpdateOrderAsync(
        SqliteConnection conn, SqliteTransaction tx, SalesOrder order,
        OrderStatus expectedStatus, bool expectedStockDeducted)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            UPDATE SalesOrders SET
                Status = @st, StockDeducted = @sd,
                CancelledAt = @cat, CancelReason = @cr
            WHERE Id = @id AND Status = @oldStatus AND StockDeducted = @oldStockDeducted;";
        cmd.Parameters.AddWithValue("@st", order.Status.ToString());
        cmd.Parameters.AddWithValue("@sd", order.StockDeducted ? 1 : 0);
        cmd.Parameters.AddWithValue("@cat", order.CancelledAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cr", order.CancelReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id", order.Id);
        cmd.Parameters.AddWithValue("@oldStatus", expectedStatus.ToString());
        cmd.Parameters.AddWithValue("@oldStockDeducted", expectedStockDeducted ? 1 : 0);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    private static async Task<bool> UpdateOrderStatusAsync(
        SqliteConnection conn, SqliteTransaction tx, int orderId, OrderStatus status)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Pesanan yang sudah batal atau lunas tidak boleh ditimpa statusnya oleh pembayaran.
        cmd.CommandText = @"
            UPDATE SalesOrders SET Status = @st
            WHERE Id = @id AND Status NOT IN ('Cancelled', 'Paid');";
        cmd.Parameters.AddWithValue("@st", status.ToString());
        cmd.Parameters.AddWithValue("@id", orderId);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    private static async Task<int> InsertPaymentAsync(
        SqliteConnection conn, SqliteTransaction tx, SalesPayment payment)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO SalesPayments
                (SalesOrderId, Amount, PaymentDate, Method, Notes, JournalEntryId,
                 CreatedAt, CreatedByUserId, IdempotencyKey)
            VALUES (@oid, @amt, @pd, @m, @notes, @jeid, @ca, @uid, @ik);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@oid", payment.SalesOrderId);
        cmd.Parameters.AddWithValue("@amt", payment.Amount);
        cmd.Parameters.AddWithValue("@pd", payment.PaymentDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@m", payment.Method);
        cmd.Parameters.AddWithValue("@notes", payment.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@jeid", payment.JournalEntryId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", payment.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@uid", payment.CreatedByUserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ik", payment.IdempotencyKey ?? (object)DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<List<SalesPayment>> GetPaymentsAsync(SqliteConnection conn, int orderId)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM SalesPayments WHERE SalesOrderId = @id;";
        cmd.Parameters.AddWithValue("@id", orderId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<SalesPayment>();
        while (r.Read()) list.Add(MapPayment(r));
        return list;
    }

    private static SalesOrder MapOrder(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        LegacyId = r.IsDBNull(r.GetOrdinal("LegacyId")) ? null : r.GetInt32(r.GetOrdinal("LegacyId")),
        OrderNumber = r.IsDBNull(r.GetOrdinal("OrderNumber")) ? null : r.GetString(r.GetOrdinal("OrderNumber")),
        CustomerId = r.IsDBNull(r.GetOrdinal("CustomerId")) ? null : r.GetInt32(r.GetOrdinal("CustomerId")),
        ProductTemplateId = r.IsDBNull(r.GetOrdinal("ProductTemplateId")) ? null : r.GetInt32(r.GetOrdinal("ProductTemplateId")),
        CustomerNameSnapshot = r.GetString(r.GetOrdinal("CustomerNameSnapshot")),
        PhoneSnapshot = r.IsDBNull(r.GetOrdinal("PhoneSnapshot")) ? null : r.GetString(r.GetOrdinal("PhoneSnapshot")),
        Description = r.GetString(r.GetOrdinal("Description")),
        PickupDate = DateTime.Parse(r.GetString(r.GetOrdinal("PickupDate"))),
        TotalAmount = r.GetInt64(r.GetOrdinal("TotalAmount")),
        DownPayment = r.GetInt64(r.GetOrdinal("DownPayment")),
        Quantity = Convert.ToDecimal(r.GetValue(r.GetOrdinal("Quantity"))),
        Status = Enum.Parse<OrderStatus>(r.GetString(r.GetOrdinal("Status"))),
        StockDeducted = r.GetInt32(r.GetOrdinal("StockDeducted")) == 1,
        Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
        CancelledAt = r.IsDBNull(r.GetOrdinal("CancelledAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("CancelledAt"))),
        CancelReason = r.IsDBNull(r.GetOrdinal("CancelReason")) ? null : r.GetString(r.GetOrdinal("CancelReason")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        CreatedByUserId = r.IsDBNull(r.GetOrdinal("CreatedByUserId")) ? null : r.GetInt32(r.GetOrdinal("CreatedByUserId"))
    };

    private static SalesPayment MapPayment(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        SalesOrderId = r.GetInt32(r.GetOrdinal("SalesOrderId")),
        Amount = r.GetInt64(r.GetOrdinal("Amount")),
        PaymentDate = DateTime.Parse(r.GetString(r.GetOrdinal("PaymentDate"))),
        Method = r.GetString(r.GetOrdinal("Method")),
        Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
        JournalEntryId = r.IsDBNull(r.GetOrdinal("JournalEntryId")) ? null : r.GetInt32(r.GetOrdinal("JournalEntryId")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        CreatedByUserId = r.IsDBNull(r.GetOrdinal("CreatedByUserId")) ? null : r.GetInt32(r.GetOrdinal("CreatedByUserId")),
        IdempotencyKey = r.IsDBNull(r.GetOrdinal("IdempotencyKey")) ? null : r.GetString(r.GetOrdinal("IdempotencyKey"))
    };
}
