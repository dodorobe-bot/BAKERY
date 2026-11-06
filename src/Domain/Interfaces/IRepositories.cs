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
    /// <param name="onlyForUserId">
    /// Bila diisi, hanya pelanggan yang punya pesanan dari pengguna itu. Kasir tidak boleh
    /// membaca seluruh buku pelanggan toko; yang ia butuhkan hanya pelanggan yang pernah ia layani.
    /// </param>
    Task<IReadOnlyList<Customer>> GetPageAsync(string? search, int limit, int offset, int? onlyForUserId = null);
    Task<int> CountActiveAsync(string? search, int? onlyForUserId = null);
    Task<int> CreateAsync(Customer customer);
    Task UpdateAsync(Customer customer);
}

public interface IIngredientRepository
{
    Task<Ingredient?> GetByIdAsync(int id);
    Task<IReadOnlyList<Ingredient>> GetAllActiveAsync();
    /// <summary>
    /// Bahan aktif plus bahan nonaktif yang stoknya belum habis. Nilainya masih dihitung di
    /// akun persediaan, jadi laporan stok wajib memuatnya supaya laporan, layar, dan neraca
    /// menunjuk barang yang sama.
    /// </summary>
    Task<IReadOnlyList<Ingredient>> GetAllCountedAsync();
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
    Task<IReadOnlyList<SalesOrder>> GetAllAsync(int limit = 100, int? createdByUserId = null, int offset = 0);
    /// <summary>Jumlah seluruh pesanan (dengan pembatas kasir yang sama) untuk "Muat lagi".</summary>
    Task<int> CountAsync(int? createdByUserId = null);
    Task<IReadOnlyList<SalesOrder>> GetUpcomingAsync(DateTime from);
    /// <summary>Pesanan dengan jadwal ambil pada rentang [from, to] tanggal, tanpa batas jumlah.</summary>
    Task<IReadOnlyList<SalesOrder>> GetByPickupDateRangeAsync(DateTime from, DateTime to);
    /// <summary>Jumlah dan sisa bayar seluruh pesanan belum lunas, dihitung di SQL.</summary>
    Task<OutstandingSummary> GetOutstandingSummaryAsync();
    /// <summary>
    /// Jumlah pesanan belum lunas pada dua keranjang tenggat, dihitung di SQL.
    /// <paramref name="createdByUserId"/> diisi untuk cakupan per kasir; null berarti seluruh toko.
    /// </summary>
    Task<DeadlineCounts> GetOutstandingBreakdownAsync(DateTime today, int? createdByUserId = null);
    /// <summary>
    /// Pesanan belum lunas yang jadwal ambilnya sebelum <paramref name="today"/>, paling dekat lebih dulu.
    /// <paramref name="createdByUserId"/> diisi untuk cakupan per kasir; null berarti seluruh toko.
    /// </summary>
    Task<IReadOnlyList<SalesOrder>> GetOutstandingOverdueAsync(DateTime today, int limit, int? createdByUserId = null);
    /// <summary>
    /// Pesanan belum lunas yang jadwal ambilnya hari ini atau sesudahnya, paling dekat lebih dulu.
    /// <paramref name="createdByUserId"/> diisi untuk cakupan per kasir; null berarti seluruh toko.
    /// </summary>
    Task<IReadOnlyList<SalesOrder>> GetOutstandingUpcomingAsync(DateTime today, int limit, int? createdByUserId = null);
    /// <summary>
    /// Jumlah pesanan belum lunas (sisa bayar masih positif) untuk pagination layar keuangan.
    /// <paramref name="createdByUserId"/> diisi untuk cakupan per kasir; null berarti seluruh toko.
    /// </summary>
    Task<int> CountOutstandingAsync(int? createdByUserId = null);
    /// <summary>
    /// Halaman pesanan belum lunas, jadwal ambil terdekat lebih dulu. Dipakai layar keuangan
    /// supaya daftar piutang tidak berhenti diam-diam pada seratus baris pertama.
    /// </summary>
    Task<IReadOnlyList<SalesOrder>> GetOutstandingListAsync(int limit, int offset, int? createdByUserId = null);
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

    /// <summary>
    /// Status + mutasi stok + jurnal pembalik tercatat bersama pada satu transaksi. Nilai lama
    /// (status, penanda stok, total bayar) dibandingkan di dalam transaksi; bila pesanan sudah
    /// berubah, perubahan ditolak alih-alih menimpa keputusan orang lain.
    /// </summary>
    Task SaveStatusChangeWithJournalAsync(
        SalesOrder order, OrderStatus expectedStatus, bool expectedStockDeducted, long expectedTotalPaid,
        IReadOnlyList<StockChange> stockChanges,
        IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges, IReadOnlyList<JournalEntry> journals);

    /// <summary>
    /// Penanda "diserahkan" + pengakuan pendapatan + mutasi stok etalase tercatat bersama.
    /// <paramref name="expectRevenueRecognition"/> true berarti servis sudah menyusun jurnal
    /// pengakuannya: bila pesanan ternyata sudah diakui atau sudah pernah diserahkan di jalur
    /// lain, perubahan ditolak supaya pendapatan dan piutang tidak lahir dua kali.
    /// </summary>
    Task MarkDeliveredWithJournalAsync(
        int orderId, DateTime deliveredAt, bool expectRevenueRecognition, long expectedTotalPaid,
        bool expectedStockDeducted, IReadOnlyList<JournalEntry> journals,
        IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges);
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
    /// <summary>Menyimpan banyak kunci dalam satu transaksi: gagal satu, tidak ada yang tersimpan.</summary>
    Task SaveManyAsync(IReadOnlyDictionary<string, string> values, int? userId = null);
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

/// <summary>
/// Satu SPK selesai dalam bentuk yang dibutuhkan penilaian HPP: unit, kerugian, dan biaya
/// komponennya. Kerugian hanya menghitung scrap yang benar-benar dibuang (bukan rework).
/// </summary>
public record CompletedWorkOrderCost(
    int WorkOrderId, int ProductTemplateId, DateTime? CompletedAt,
    int GoodUnits, int FailedUnits, int LossUnits,
    long MaterialCostMinorUnits, long LaborCostMinorUnits, long OverheadCostMinorUnits);

public interface IProductionRepository
{
    Task<WorkOrder?> GetByIdAsync(int id);
    Task<IReadOnlyList<WorkOrder>> GetAllAsync(int limit = 50);
    /// <summary>
    /// Seluruh SPK selesai (tanpa batas jumlah) beserta jumlah kerugian, dihitung di SQL —
    /// ringkasan HPP tidak boleh berubah hanya karena ada SPK baru yang menggeser jendela muat.
    /// </summary>
    Task<IReadOnlyList<CompletedWorkOrderCost>> GetCompletedCostLinesAsync();
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
    Task<(decimal Quantity, long Amount, long Discount, long Tax)> GetReturnedTotalsAsync(int salesOrderId);
    /// <summary>Retur + jurnal + pengembalian stok produk jadi tercatat bersama atau batal bersama.</summary>
    Task<int> CreateWithJournalAsync(
        SalesReturn ret, JournalEntry? journal, IReadOnlyList<FinishedGoodsChange> finishedGoodsChanges);
}

/// <summary>
/// Satu kaki mutasi stok. Hanya StockLedger yang boleh mengubah LocationStock.
/// UnitCostMinorUnits dipakai untuk barang masuk yang membawa harganya sendiri (penerimaan PO);
/// kosong berarti lapisan barunya diberi harga master bahan yang sedang terpasang.
/// </summary>
public record StockChange(
    int LocationId, int IngredientId, decimal Delta, MovementType Type,
    string? SourceType, int? SourceId, int? UserId, string? Notes,
    long? UnitCostMinorUnits = null);

/// <summary>
/// Satu kaki mutasi produk jadi, pasangannya FinishedGoodsLedger. UnitCostMinorUnits dipakai
/// untuk mutasi masuk yang membawa biaya sendiri (retur/pembatalan); nol berarti pakai
/// biaya rata-rata yang berlaku.
/// </summary>
public record FinishedGoodsChange(
    int LocationId, int ProductTemplateId, decimal Delta, FinishedGoodsMovementType Type,
    string? SourceType, int? SourceId, int? UserId, string? Notes,
    long UnitCostMinorUnits = 0);

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
    /// <summary>
    /// Mutasi stok dari layar Bahan Baku plus jurnalnya dalam satu transaksi. stokAwal true berarti
    /// nilai bahan melawan Modal (barang yang sudah ada saat toko mulai dicatat); false berarti
    /// koreksi yang diperlakukan seperti opname, melawan Beban Selisih Persediaan.
    /// </summary>
    Task ApplyIngredientMasterStockAsync(int locationId, int ingredientId, decimal delta, bool stokAwal, int? userId, string? notes);
    /// <summary>Saldo produk jadi per lokasi. locationId null berarti seluruh lokasi.</summary>
    Task<IReadOnlyList<FinishedGoodsStockItem>> GetFinishedGoodsStockAsync(int? locationId = null);
    Task<decimal> GetFinishedGoodsTotalAsync(int productTemplateId);
    /// <summary>Biaya per unit yang tercatat saat produk keluar untuk pesanan ini (0 bila tidak ada).</summary>
    Task<long> GetFinishedGoodsSaleUnitCostAsync(int salesOrderId);
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
    /// <summary>
    /// Batalkan PO yang belum menerima barang. Ditolak bila PO sudah selesai/dibatalkan atau
    /// sudah pernah menerima sebagian barang, karena stok dan jurnalnya akan menjadi yatim.
    /// </summary>
    Task CancelAsync(int id);

    /// <summary>
    /// Struk penerimaan +mutasi stok + jurnal + status PO tercatat pada satu transaksi:
    /// barang tidak pernah masuk gudang tanpa jejak uang, dan sebaliknya. Status PO dihitung
    /// ulang dari jumlah diterima yang tersimpan, bukan dari salinan yang dibaca pemanggil.
    /// </summary>
    Task ReceiveWithJournalAsync(
        GoodsReceipt receipt, IReadOnlyList<StockChange> stockChanges, JournalEntry? journal);
}

/// <summary>
/// Satu baris penerimaan dilihat dari sisi hutang: berapa yang masih bisa diretur dan berapa
/// nilai hutang yang lahir darinya. Layar retur dan sisi tulis mengambil angka yang sama dari
/// sini, jadi "sisa" tidak pernah dihitung dua kali dengan hasil berbeda.
/// </summary>
public record PayableReceiptLineView(
    int GoodsReceiptLineId, int IngredientId, string IngredientName, string Unit,
    int LocationId, string LocationName, decimal ReceivedQuantity, decimal ReturnedQuantity,
    long UnitCostMinorUnits, long ValueMinorUnits);

/// <summary>
/// Struk penerimaan beserta sisa hutangnya: nilai terima dikurangi seluruh retur lalu seluruh
/// pembayaran. Hutang lahir per struk (bukan per PO), jadi struk inilah satuan yang bisa
/// dijelaskan baris per baris di layar Utang Pemasok.
/// </summary>
public record SupplierPayableView(
    int GoodsReceiptId, int PurchaseOrderId, string? PoNumber, int? SupplierId, string? SupplierName,
    DateTime ReceivedAt, long ReceivedValueMinorUnits, long ReturnedValueMinorUnits,
    long PaidValueMinorUnits, long BalanceMinorUnits, IReadOnlyList<PayableReceiptLineView> Lines);

public record SupplierReturnLineView(
    int GoodsReceiptLineId, int IngredientId, string? IngredientName, string Unit,
    decimal Quantity, long UnitCostMinorUnits, long ValueMinorUnits);

public record SupplierReturnView(
    int Id, string ReturnNumber, int GoodsReceiptId, int PurchaseOrderId, string? PoNumber,
    string? SupplierName, string Reason, string? Notes, long ValueMinorUnits, DateTime ReturnedAt,
    string? CreatedByUsername, IReadOnlyList<SupplierReturnLineView> Lines);

public record SupplierPaymentView(
    int Id, string PaymentNumber, int GoodsReceiptId, int PurchaseOrderId, string? PoNumber,
    string? SupplierName, long AmountMinorUnits, string Method, string? Notes, DateTime PaidAt,
    string? CreatedByUsername);

/// <summary>
/// Jalur baca sekaligus tulis retur dan pembayaran pemasok. Keduanya mengikat satu struk
/// penerimaan dan menulis dokumen + mutasi stok + jurnal dalam satu transaksi, sehingga hutang
/// tidak pernah turun tanpa barang atau uang yang ikut bergerak.
/// </summary>
public interface ISupplierSettlementRepository
{
    /// <param name="includeLunas">false menyembunyikan struk yang sisa hutangnya sudah nol.</param>
    Task<IReadOnlyList<SupplierPayableView>> GetPayablesAsync(bool includeLunas = false, int limit = 200);
    Task<SupplierPayableView?> GetPayableAsync(int goodsReceiptId);
    Task<IReadOnlyList<SupplierReturnView>> GetReturnsAsync(int? goodsReceiptId = null, int limit = 200);
    Task<IReadOnlyList<SupplierPaymentView>> GetPaymentsAsync(int? goodsReceiptId = null, int limit = 200);
    /// <summary>
    /// Retur + stok keluar + jurnal tercatat bersama. Baris retur adalah sumber mutasi stoknya, jadi
    /// tidak ada jalur lain yang boleh menggerakkan barang untuk dokumen ini. Nilai per baris dan
    /// sisa hutang dihitung ulang dari struk di dalam transaksi, sehingga dua retur bersamaan tidak
    /// bisa melepas hutang lebih besar daripada yang ada. Jurnalnya boleh kosong untuk retur barang
    /// yang harganya nol di struk — tidak ada hutang yang dilepas, hanya lapisan yang keluar.
    /// </summary>
    Task<int> CreateReturnWithJournalAsync(SupplierReturn ret, JournalEntry? journal);

    /// <summary>Pembayaran mengikat satu struk, sama seperti hutangnya lahir.</summary>
    Task<int> CreatePaymentWithJournalAsync(SupplierPayment payment, JournalEntry journal);
}

/// <summary>
/// Pagar periode buku. Daftar dibaca dari isi jurnal, bukan dari tabel FiscalPeriods saja, supaya
/// bulan yang sudah berisi transaksi tapi belum pernah disentuh tetap muncul dan bisa ditutup.
/// Menutup dan membuka kembali hanya mengubah status: tidak ada satu baris jurnal pun yang
/// dihapus atau dipindah, jadi riwayat tetap utuh apa pun keputusan pemilik.
/// </summary>
public interface IFiscalPeriodRepository
{
    Task<IReadOnlyList<FiscalPeriodSummary>> ListAsync();

    /// <summary>
    /// Menulis baris baru bila periodenya belum pernah ada, lalu mengubah statusnya. Rentang
    /// dikirim apa adanya karena bulan yang sudah punya baris bisa saja berisi rentang yang tidak
    /// sama dengan kalender; menutup ulang tidak boleh diam-diam memindah batas periode.
    /// </summary>
    Task UpsertStatusAsync(string name, string startDate, string endDate, string status, int userId);
}

public interface IDocumentSequenceRepository
{
    /// <summary>
    /// Nomor berikutnya untuk satu jenis dokumen pada satu hari kalender toko. Setiap panggilan
    /// menaikkan urutan, jadi dua dokumen yang dibuat berdekatan tidak pernah berbagi nomor.
    /// </summary>
    Task<string> NextNumberAsync(DocumentKind kind, DateTime hariToko);
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
