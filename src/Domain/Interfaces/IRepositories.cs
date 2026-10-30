using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id);
    Task<User?> GetByUsernameAsync(string username);
    Task<IReadOnlyList<User>> GetAllAsync();
    Task<int> CreateAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(int id);
}

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(int id);
    Task<Customer?> GetByPhoneAsync(string phone);
    Task<IReadOnlyList<Customer>> GetAllActiveAsync();
    /// <summary>Satu halaman pelanggan aktif (urut nama) dengan pencarian nama/telepon.</summary>
    Task<IReadOnlyList<Customer>> GetPageAsync(string? search, int limit, int offset);
    Task<int> CountActiveAsync(string? search);
    Task<int> CreateAsync(Customer customer);
    Task UpdateAsync(Customer customer);
}

public interface IIngredientRepository
{
    Task<Ingredient?> GetByIdAsync(int id);
    Task<IReadOnlyList<Ingredient>> GetAllActiveAsync();
    Task<IReadOnlyList<Ingredient>> GetBelowMinStockAsync();
    /// <summary>Nama bahan aktif yang sama (abaikan spasi ujung dan huruf besar-kecil).</summary>
    Task<bool> IsNameTakenAsync(string name, int? exceptId = null);
    /// <summary>Sisipkan bila nama belum dipakai; null berarti nama sudah dipakai (balapan aman).</summary>
    Task<int?> TryCreateAsync(Ingredient ingredient);
    Task UpdateAsync(Ingredient ingredient);
    Task DeleteAsync(int id);
}

public interface IProductTemplateRepository
{
    Task<ProductTemplate?> GetByIdWithRecipesAsync(int id);
    Task<IReadOnlyList<ProductTemplate>> GetAllActiveAsync();
    Task<int> CreateAsync(ProductTemplate template, IEnumerable<RecipeLine> lines);
    Task UpdateAsync(ProductTemplate template, IEnumerable<RecipeLine> lines);
    Task DeleteAsync(int id);
}

public interface ISalesOrderRepository
{
    Task<SalesOrder?> GetByIdAsync(int id);
    /// <param name="createdByUserId">Bila diisi, hanya pesanan kasir tersebut (cakupan per kasir).</param>
    Task<IReadOnlyList<SalesOrder>> GetAllAsync(int limit = 100, int? createdByUserId = null);
    Task<IReadOnlyList<SalesOrder>> GetUpcomingAsync(DateTime from);
    /// <summary>Pesanan dengan jadwal ambil pada rentang [from, to] tanggal, tanpa batas jumlah.</summary>
    Task<IReadOnlyList<SalesOrder>> GetByPickupDateRangeAsync(DateTime from, DateTime to);
    /// <summary>Jumlah dan sisa bayar seluruh pesanan belum lunas, dihitung di SQL.</summary>
    Task<OutstandingSummary> GetOutstandingSummaryAsync();
    /// <summary>Jumlah pesanan belum lunas pada dua keranjang tenggat, dihitung di SQL.</summary>
    Task<DeadlineCounts> GetOutstandingBreakdownAsync(DateTime today);
    /// <summary>Pesanan belum lunas yang jadwal ambilnya sebelum <paramref name="today"/>, paling dekat lebih dulu.</summary>
    Task<IReadOnlyList<SalesOrder>> GetOutstandingOverdueAsync(DateTime today, int limit);
    /// <summary>Pesanan belum lunas yang jadwal ambilnya hari ini atau sesudahnya, paling dekat lebih dulu.</summary>
    Task<IReadOnlyList<SalesOrder>> GetOutstandingUpcomingAsync(DateTime today, int limit);
    /// <summary>Rekap per hari untuk grafik analitik (rentang setengah terbuka [from, to)).</summary>
    Task<IReadOnlyList<DailySalesSummary>> GetDailySalesAsync(DateTime from, DateTime to);
    /// <summary>Produk dengan nilai pesanan terbesar pada rentang, dihitung di SQL.</summary>
    Task<IReadOnlyList<ProductSalesSummary>> GetTopProductSalesAsync(DateTime from, DateTime to, int take);
    Task<SalesPayment?> GetPaymentByIdempotencyKeyAsync(string key);

    /// <summary>Pesanan + jurnal + mutasi stok awal tercatat bersama: tidak ada pesanan tanpa jejak uang.</summary>
    Task<int> CreateWithJournalAsync(
        SalesOrder order, IReadOnlyList<JournalEntry> journals, IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges);

    /// <summary>Bayar + jurnal + status lunas tercatat bersama; JournalEntryId pembayaran ikut tersimpan.</summary>
    Task RecordPaymentWithJournalAsync(
        SalesPayment payment, IReadOnlyList<JournalEntry> journals, OrderStatus? newOrderStatus,
        IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges);

    /// <summary>Status + mutasi stok + jurnal pembalik tercatat bersama pada satu transaksi.</summary>
    Task SaveStatusChangeWithJournalAsync(
        SalesOrder order, IReadOnlyList<StockChange> stockChanges,
        IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges, IReadOnlyList<JournalEntry> journals);
}

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(int id);
    Task<Account?> GetByCodeAsync(string code);
    Task<IReadOnlyList<Account>> GetAllActiveAsync();
    Task<int> CreateAsync(Account account);
}

public interface IJournalRepository
{
    Task<JournalEntry?> GetByIdAsync(int id);
    Task<JournalEntry?> GetByIdempotencyKeyAsync(string key);
    /// <summary>Entri beserta barisnya (termasuk akun) — tampilan jurnal dan ekspor bergantung padanya.</summary>
    Task<IReadOnlyList<JournalEntry>> GetRecentAsync(int limit = 30);
    Task<IReadOnlyList<JournalEntry>> GetByDateRangeAsync(DateTime from, DateTime to);
    /// <summary>
    /// Satu halaman entri pada rentang [from, to], terbaru lebih dulu. <paramref name="beforeDate"/>/
    /// <paramref name="beforeId"/> adalah kunci keyset entri terakhir yang sudah tampil, sehingga
    /// memuat halaman berikutnya tidak menggeser baris saat ada entri baru masuk.
    /// </summary>
    Task<IReadOnlyList<JournalEntry>> GetByDateRangePageAsync(
        DateTime from, DateTime to, DateTime? beforeDate, int? beforeId, int limit);
    /// <summary>Total per akun untuk seluruh rentang, tanpa memuat entri ke memori.</summary>
    Task<JournalRangeSummary> GetRangeSummaryAsync(DateTime from, DateTime to, string? sourceType);
    Task<int> CreateAsync(JournalEntry entry);
    /// <summary>Semua entri yang bertaut ke satu dokumen sumber — bahan pembalik jurnal saat dokumen dibatalkan.</summary>
    Task<IReadOnlyList<JournalEntry>> GetBySourceDocumentAsync(string sourceDocumentType, int sourceDocumentId);
    Task UpdateStatusAsync(int id, JournalStatus status);
    Task<IReadOnlyList<AccountBalance>> GetBalancesAsync(DateTime? asOf = null);
    /// <summary>Saldo satu akun pada arah normalnya, tanpa memuat seluruh bagan akun.</summary>
    Task<long> GetAccountBalanceAsync(string code);
    /// <summary>Kas masuk &amp; keluar satu hari bisnis pada satu akun, dihitung di SQL.</summary>
    Task<CashFlowSummary> GetCashFlowAsync(DateTime day, string accountCode);
    /// <summary>Pendapatan dan beban per hari (rentang setengah terbuka [from, to)).</summary>
    Task<IReadOnlyList<DailyJournalTotals>> GetDailyTotalsAsync(DateTime from, DateTime to);
    /// <summary>Total beban per akun pada satu rentang (setengah terbuka [from, to)).</summary>
    Task<IReadOnlyList<AccountExpenseTotal>> GetExpenseTotalsAsync(DateTime from, DateTime to);
}

public interface IStockMovementRepository
{
    Task<IReadOnlyList<StockMovement>> GetByIngredientAsync(int ingredientId);
}

public interface ISettingRepository
{
    Task<string?> GetAsync(string key);
    Task<Dictionary<string, string>> GetAllAsync();
    Task SetAsync(string key, string value, int? userId = null);
}

/// <summary>Satu baris jejak audit siap tampil: pelaksananya sudah ikut terbaca nama.</summary>
public record AuditEntryView(
    int Id, int? UserId, string? Username, string Action, string EntityType, int? EntityId,
    string? OldValues, string? NewValues, DateTime OccurredAt, string? SessionId);

public record AuditFilter(
    string? EntityType = null, int? EntityId = null, int? UserId = null,
    DateTime? From = null, DateTime? To = null, int Limit = 100);

public interface IAuditLogRepository
{
    Task LogAsync(AuditLog entry);

    /// <summary>Entri terbaru lebih dulu. "Siapa mengubah apa" harus bisa dibaca balik tanpa alat luar.</summary>
    Task<IReadOnlyList<AuditEntryView>> SearchAsync(AuditFilter filter);
}

public interface IProductionRepository
{
    Task<WorkOrder?> GetByIdAsync(int id);
    Task<IReadOnlyList<WorkOrder>> GetAllAsync(int limit = 50);
    /// <summary>SPK yang belum selesai/dibatalkan, tenggat terdekat lebih dulu.</summary>
    Task<IReadOnlyList<WorkOrder>> GetOpenAsync(int limit);
    Task<DeadlineCounts> GetOpenBreakdownAsync(DateTime today);
    Task<int> CreateAsync(WorkOrder order, IEnumerable<WorkOrderMaterial> materials);
    Task UpdateAsync(WorkOrder order);
    Task CompleteWorkOrderAsync(
        int id, int actualGood, int actualFailed,
        IEnumerable<WorkOrderMaterial> actualMaterials, IEnumerable<WorkOrderScrap> scraps,
        JournalEntry? journal, long costPerGoodUnitMinorUnits);
    Task CancelWorkOrderAsync(int id, string? reason);
}

/// <summary>Satu hasil QC siap tampil: nomor SPK, produk, dan pemeriksanya sudah ikut terbaca.</summary>
public record QualityCheckView(
    int Id, int WorkOrderId, string OrderNumber, string? ProductName,
    int SampleQuantity, int PassedQuantity, int FailedQuantity, string Decision,
    string? AppearanceNotes, string? TasteNotes, string? Notes,
    DateTime CheckedAt, string? CheckerUsername);

public interface IQualityCheckRepository
{
    Task<IReadOnlyList<QualityCheckView>> GetAllAsync(int limit = 200);
    Task<QualityCheck?> GetByWorkOrderAsync(int workOrderId);
    Task<int> CreateAsync(QualityCheck check);
}

/// <summary>Retur siap tampil; nama pelanggan dan nomor pesanan dibaca dari pesanan asalnya.</summary>
public record SalesReturnView(
    int Id, string ReturnNumber, int SalesOrderId, string? OrderNumber, string CustomerName,
    int? ProductTemplateId, string? ProductName, decimal Quantity, long Amount,
    string Reason, string RefundMethod, DateTime ReturnedAt, string? CreatedByUsername);

public interface ISalesReturnRepository
{
    Task<IReadOnlyList<SalesReturnView>> GetAllAsync(int limit = 200);
    /// <summary>Jumlah dan nilai yang sudah diretur untuk satu pesanan, untuk sisa yang boleh diretur.</summary>
    Task<(decimal Quantity, long Amount)> GetReturnedTotalsAsync(int salesOrderId);
    /// <summary>Retur + jurnal + pengembalian stok produk jadi tercatat bersama atau batal bersama.</summary>
    Task<int> CreateWithJournalAsync(
        SalesReturn ret, JournalEntry? journal, IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges);
}

/// <summary>Satu kaki mutasi stok. Hanya StockLedger yang boleh mengubah LocationStock.</summary>
public record StockChange(
    int LocationId, int IngredientId, decimal Delta, MovementType Type,
    string? SourceType, int? SourceId, int? UserId, string? Notes);

/// <summary>Satu kaki mutasi produk jadi, pasangannya FinishedGoodsLedger.</summary>
public record FinishedGoodsChange(
    int LocationId, int ProductTemplateId, decimal Delta, FinishedGoodsMovementType Type,
    string? SourceType, int? SourceId, int? UserId, string? Notes);

/// <summary>Baris buku mutasi siap tampil: nama bahan & lokasi sudah ikut terbaca.</summary>
public record StockMovementView(
    int Id, int IngredientId, string IngredientName, string Unit, string MovementType,
    decimal Quantity, int? LocationId, string? LocationName,
    string? SourceType, int? SourceId, string? Notes, DateTime CreatedAt);

public interface ILocationStockRepository
{
    Task<IReadOnlyList<StorageLocation>> GetLocationsAsync();
    Task<IReadOnlyList<LocationStock>> GetStockByLocationAsync(int locationId);
    Task<decimal> GetTotalStockAsync(int ingredientId);
    Task<int> GetLocationIdByTypeAsync(string type);
    /// <summary>Mutasi terbaru; locationId null berarti seluruh lokasi.</summary>
    Task<IReadOnlyList<StockMovementView>> GetMovementsAsync(int? locationId, int limit = 50);
    /// <summary>Menerapkan seluruh kaki dalam satu transaksi: sebagian gagal berarti semuanya gagal.</summary>
    Task ApplyAsync(IReadOnlyList<StockChange> changes);
    Task TransferStockAsync(int fromLocationId, int toLocationId, int ingredientId, decimal quantity, int? userId, string? notes);
    Task AdjustStockAsync(int locationId, int ingredientId, decimal newQuantity, int? userId, string? reason);
    /// <summary>Saldo produk jadi per lokasi. locationId null berarti seluruh lokasi.</summary>
    Task<IReadOnlyList<FinishedGoodsStockItem>> GetFinishedGoodsStockAsync(int? locationId = null);
    Task<decimal> GetFinishedGoodsTotalAsync(int productTemplateId);
}

/// <summary>
/// Satu lot bahan yang pernah diterima, dilihat dari sisi kedaluwarsa. Jumlah stok per lot
/// tidak dicatat terpisah (stok tercatat per lokasi), jadi baris ini menjawab "kapan bahan
/// ini harus dipakai lebih dulu", bukan "sisa lot berapa".
/// </summary>
public record ItemBatchView(
    int Id, int IngredientId, string IngredientName, string Unit, string BatchNumber,
    DateTime ReceivedDate, DateTime ExpiryDate, long UnitCostMinorUnits,
    int DaysToExpiry, string Status);

/// <summary>Lot baru yang dicatat; sumbernya bisa penerimaan PO atau catatan manual.</summary>
public record NewBatch(
    int IngredientId, string BatchNumber, DateTime ReceivedDate, DateTime ExpiryDate,
    long UnitCostMinorUnits, int? GoodsReceiptLineId);

public interface IBatchRepository
{
    /// <param name="status">aman | segera | kedaluwarsa, atau null untuk semuanya.</param>
    Task<IReadOnlyList<ItemBatchView>> GetAsync(string? status = null, int limit = 200);
    Task<int> CreateAsync(NewBatch batch);
}

public interface ISupplierRepository
{
    Task<IEnumerable<Supplier>> GetAllAsync();
    Task<Supplier?> GetByIdAsync(int id);
    Task<int> CreateAsync(Supplier supplier);
    Task UpdateAsync(Supplier supplier);
}

public record ReceiveGoodsLineDto(
    int PurchaseOrderLineId, int IngredientId, decimal ReceivedQty, long UnitCostMinorUnits, int LocationId,
    string? BatchNumber = null, DateTime? ExpiryDate = null);
public record ReceiveGoodsDto(string? Notes, int? UserId, List<ReceiveGoodsLineDto> Lines);

public interface IPurchaseOrderRepository
{
    Task<IEnumerable<PurchaseOrder>> GetAllAsync();
    /// <summary>Satu halaman PO terbaru (dengan barisnya), boleh disaring status atau "open".</summary>
    Task<IReadOnlyList<PurchaseOrder>> GetPageAsync(string? status, int limit, int offset);
    Task<int> CountAsync(string? status);
    /// <summary>PO yang belum diterima penuh (atau masih draft), tanggal terima terdekat lebih dulu.</summary>
    Task<IReadOnlyList<PurchaseOrder>> GetOpenAsync(int limit);
    Task<DeadlineCounts> GetOpenBreakdownAsync(DateTime today);
    Task<PurchaseOrder?> GetByIdAsync(int id); // dengan lines
    Task<int> CreateAsync(PurchaseOrder po);
    Task UpdateStatusAsync(int id, PurchaseOrderStatus status);

    /// <summary>
    /// Struk penerimaan +mutasi stok + jurnal + status PO tercatat pada satu transaksi:
    /// barang tidak pernah masuk gudang tanpa jejak uang, dan sebaliknya.
    /// </summary>
    Task ReceiveWithJournalAsync(
        GoodsReceipt receipt, IReadOnlyList<StockChange> stockChanges,
        JournalEntry? journal, PurchaseOrderStatus? newStatus);
}

public interface IDocumentSequenceRepository
{
    /// <summary>
    /// Nomor berikutnya untuk satu jenis dokumen pada satu hari (UTC). Setiap panggilan
    /// menaikkan urutan, jadi dua dokumen yang dibuat berdekatan tidak pernah berbagi nomor.
    /// </summary>
    Task<string> NextNumberAsync(DocumentKind kind, DateTime dayUtc);
}

/// <summary>Catatan satu kunci idempotensi: rute pemiliknya, hasil, dan kapan ditutup.</summary>
public record IdempotencyRecord(
    string Key, string Route, int? UserId, int? ResourceId,
    DateTime ReservedAt, DateTime? CompletedAt)
{
    public bool Selesai => CompletedAt is not null;
}

/// <summary>
/// Gerbang anti-duplikat untuk aksi perubahan. Kunci dipesan lebih dulu, ditutup saat aksi
/// berhasil, dan dilepas saat gagal supaya pengguna boleh mencoba lagi dengan kunci yang sama.
/// </summary>
public interface IIdempotencyRepository
{
    Task<IdempotencyRecord?> GetAsync(string key);

    /// <summary>False bila kunci sudah dipesan permintaan lain.</summary>
    Task<bool> ReserveAsync(string key, string route, int? userId);

    Task CompleteAsync(string key, int? resourceId);

    Task ReleaseAsync(string key);
}
