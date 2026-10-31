using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

public record CreateOrderRequest(
    string CustomerName, string? Phone, string Description,
    DateTime PickupDate, long TotalAmount, long DownPayment,
    int? ProductTemplateId, string? Notes, int CreatedByUserId,
    string? IdempotencyKey = null,
    // Jumlah unit: ditambahkan paling belakang agar permintaan lama tanpa field ini tetap sah
    // dan terbaca sebagai satu unit, sama seperti baris lama di basis data.
    decimal Quantity = 1);

public record AddPaymentRequest(
    int OrderId, long Amount, string Method, int CreatedByUserId,
    string? Notes = null, string? IdempotencyKey = null);

public class OrderService
{
    /// <summary>Atas jumlah satu pesanan; di atas ini hampir pasti salah ketik, bukan pesanan nyata.</summary>
    public const decimal MaxOrderQuantity = 100_000;

    // Metode pembayaran hanya dua jalur kas yang benar-benar ada di toko. Teks bebas membuat
    // rekap kas per metode tidak pernah cocok dengan uang yang diterima.
    private static readonly string[] MetodeSah = ["Tunai", "Transfer"];

    private readonly ISalesOrderRepository _orders;
    private readonly ILocationStockRepository _locationStock;
    private readonly IJournalRepository _journals;
    private readonly IDocumentSequenceRepository _documents;
    private readonly AccountingService _accounting;
    private readonly ISalesReturnRepository _returns;

    public OrderService(
        ISalesOrderRepository orders,
        ILocationStockRepository locationStock,
        IJournalRepository journals, IDocumentSequenceRepository documents,
        AccountingService accounting, ISalesReturnRepository returns)
    {
        _orders = orders;
        _locationStock = locationStock;
        _journals = journals;
        _documents = documents;
        _accounting = accounting;
        _returns = returns;
    }

    public async Task<Result<int>> CreateOrderAsync(CreateOrderRequest req)
    {
        var moneyError = ValidateOrderAmounts(req.TotalAmount, req.DownPayment);
        if (moneyError is not null)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, moneyError);

        if (string.IsNullOrWhiteSpace(req.CustomerName))
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Nama pelanggan wajib diisi.");

        // Sama seperti pagaran nominal: jumlah di luar ini hampir selalu salah ketik, dan
        // stok etalase akan terpotong sebanyak angka tersebut saat status berubah.
        if (req.Quantity <= 0)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Jumlah pesanan harus lebih dari nol.");
        if (req.Quantity > MaxOrderQuantity)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Jumlah pesanan melebihi batas {MaxOrderQuantity:N0} unit.");

        // Pelanggan ditautkan dan poin loyaltinya ditambahkan di dalam transaksi pesanan
        // (SalesOrderRepository), sehingga pesanan kembar dengan nomor baru tidak menabrak
        // keunikan nomor telepon dan poin dari dua pesanan bersamaan tidak hilang.
        var order = new SalesOrder
        {
            // Tanggal di nomor dokumen adalah tanggal kalender toko, bukan UTC: pesanan pukul
            // 01:00 WIB tetap bernomor hari itu, bukan hari kemarin.
            OrderNumber = await _documents.NextNumberAsync(DocumentKind.Order, BusinessTime.Now()),
            ProductTemplateId = req.ProductTemplateId,
            CustomerNameSnapshot = req.CustomerName,
            PhoneSnapshot = req.Phone,
            Description = req.Description,
            PickupDate = req.PickupDate,
            TotalAmount = req.TotalAmount,
            DownPayment = req.DownPayment,
            Quantity = req.Quantity,
            Status = req.DownPayment >= req.TotalAmount ? OrderStatus.Paid : OrderStatus.Pending,
            Notes = req.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = req.CreatedByUserId
        };

        // Id pesanan belum ada, jadi tautan dokumen diisi repository di dalam transaksi yang sama.
        var journals = new List<JournalEntry>();
        if (req.DownPayment > 0)
        {
            journals.Add(await _accounting.BuildDownPaymentAsync(
                null, req.CustomerName, req.DownPayment, req.CreatedByUserId, req.IdempotencyKey));

            if (order.Status == OrderStatus.Paid)
                journals.Add(await _accounting.BuildRevenueRecognitionAsync(
                    null, req.CustomerName, req.TotalAmount, req.CreatedByUserId));
        }

        // Pesanan yang langsung lunas saat dibuat tidak pernah lewat perubahan status, jadi
        // pengurangan stok produk jadi harus terjadi di sini juga.
        var createdChanges = new List<FinishedGoodsChange>();
        if (order.Status == OrderStatus.Paid && order.ProductTemplateId.HasValue)
        {
            int displayId = await _locationStock.GetLocationIdByTypeAsync("Display");
            createdChanges.Add(new FinishedGoodsChange(
                displayId, order.ProductTemplateId.Value, -order.Quantity,
                FinishedGoodsMovementType.Sale, "SalesOrder", null, req.CreatedByUserId,
                $"Penjualan langsung {order.OrderNumber} ({order.Quantity} unit)"));
            order.StockDeducted = true;
        }

        int orderId;
        try
        {
            orderId = await _orders.CreateWithJournalAsync(order, journals, createdChanges);
        }
        catch (InvalidOperationException ex)
        {
            return Result<int>.Fail(ErrorCodes.ValidationFailed, ex.Message);
        }

        return Result<int>.Ok(orderId);
    }

    public async Task<Result> AddPaymentAsync(AddPaymentRequest req)
    {
        var moneyError = MoneyRules.AmountError(req.Amount, "Pembayaran");
        if (moneyError is not null)
            return Result.Fail(ErrorCodes.ValidationFailed, moneyError);

        var metode = req.Method?.Trim();
        if (string.IsNullOrEmpty(metode)
            || !MetodeSah.Contains(metode, StringComparer.OrdinalIgnoreCase))
            return Result.Fail(ErrorCodes.ValidationFailed,
                $"Metode pembayaran harus salah satu dari: {string.Join(", ", MetodeSah)}.");
        metode = MetodeSah.First(m => m.Equals(metode, StringComparison.OrdinalIgnoreCase));

        // Idempotency check
        if (req.IdempotencyKey is not null)
        {
            var existing = await _orders.GetPaymentByIdempotencyKeyAsync(req.IdempotencyKey);
            if (existing is not null)
                return Result.Ok(); // already processed
        }

        var order = await _orders.GetByIdAsync(req.OrderId);
        if (order is null)
            return Result.Fail(ErrorCodes.NotFound, "Pesanan tidak ditemukan.");
        if (order.Status == OrderStatus.Cancelled)
            return Result.Fail(ErrorCodes.InvalidOrderStatus, "Pesanan sudah dibatalkan.");

        var remaining = order.RemainingAmount();
        if (remaining <= 0)
            return Result.Fail(ErrorCodes.InvalidOrderStatus, "Pesanan sudah lunas.");
        if (req.Amount > remaining)
            return Result.Fail(ErrorCodes.ValidationFailed,
                $"Pembayaran {Money.Format(req.Amount)} melebihi sisa bayar {Money.Format(remaining)}.");

        var fullyPaid = req.Amount == remaining;

        var journals = new List<JournalEntry>
        {
            await _accounting.BuildPaymentAsync(order.Id, order.CustomerNameSnapshot, req.Amount, req.CreatedByUserId)
        };
        if (fullyPaid)
            journals.Add(await _accounting.BuildRevenueRecognitionAsync(
                order.Id, order.CustomerNameSnapshot, order.TotalAmount, req.CreatedByUserId));

        var payment = new SalesPayment
        {
            SalesOrderId = req.OrderId,
            Amount = req.Amount,
            PaymentDate = BusinessTime.Now(),
            Method = metode,
            Notes = req.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = req.CreatedByUserId,
            IdempotencyKey = req.IdempotencyKey
        };

        // Pelunasan lewat pembayaran juga memindahkan stok: kalau tidak, pesanan yang dibayar
        // penuh tanpa ubah status tidak pernah mengurangi etalase.
        var paymentChanges = new List<FinishedGoodsChange>();
        if (fullyPaid && !order.StockDeducted && order.ProductTemplateId.HasValue)
        {
            int displayId = await _locationStock.GetLocationIdByTypeAsync("Display");
            paymentChanges.Add(new FinishedGoodsChange(
                displayId, order.ProductTemplateId.Value, -order.Quantity,
                FinishedGoodsMovementType.Sale, "SalesOrder", order.Id, req.CreatedByUserId,
                $"Penjualan lunas {order.OrderNumber} ({order.Quantity} unit)"));
        }

        try
        {
            await _orders.RecordPaymentWithJournalAsync(
                payment, journals, fullyPaid ? OrderStatus.Paid : (OrderStatus?)null, paymentChanges);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(ErrorCodes.ValidationFailed, ex.Message);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateStatusAsync(int orderId, OrderStatus newStatus, int userId, string? cancelReason = null)
    {
        var order = await _orders.GetByIdAsync(orderId);
        if (order is null)
            return Result.Fail(ErrorCodes.NotFound, "Pesanan tidak ditemukan.");
        if (order.Status == OrderStatus.Cancelled)
            return Result.Fail(ErrorCodes.InvalidOrderStatus, "Pesanan sudah dibatalkan.");
        if (order.Status == newStatus)
            return Result.Ok();

        // Lunas adalah status akhir: pendapatan sudah diakui dan stok sudah keluar, jadi status
        // tidak boleh mundur ke Pending/Processing/Ready tanpa pembalik jurnal. Jalur yang sah
        // untuk membatalkan uang yang sudah masuk adalah retur atau pembatalan pesanan.
        if (order.Status == OrderStatus.Paid && newStatus != OrderStatus.Cancelled)
            return Result.Fail(ErrorCodes.InvalidOrderStatus,
                "Pesanan sudah lunas; status tidak bisa mundur. Gunakan retur atau pembatalan pesanan.");
        if (newStatus == OrderStatus.Paid && !order.IsFullyPaid())
            return Result.Fail(ErrorCodes.ValidationFailed,
                $"Pesanan ini masih kurang {Money.Format(order.RemainingAmount())}. "
                + "Pelunasan dicatat lewat pembayaran, bukan dengan mengubah status langsung.");

        // Pembatalan mengembalikan seluruh jumlah pesanan dan membalik jurnal penjualan penuh.
        // Kalau sebagian sudah diretur, stok yang diretur sudah kembali dan uangnya sudah
        // dikembalikan; mengembalikan seluruh jumlah lagi membuat stok hantu dan jurnal retur
        // tidak ikut terbalik. Sisa unit tetap bisa dikembalikan lewat retur.
        if (newStatus == OrderStatus.Cancelled && order.ProductTemplateId.HasValue)
        {
            var sudahRetur = await _returns.GetReturnedTotalsAsync(order.Id);
            if (sudahRetur.Quantity > 0)
                return Result.Fail(ErrorCodes.InvalidOrderStatus,
                    $"Pesanan {order.OrderNumber} sudah memiliki retur {sudahRetur.Quantity:0.###} unit, "
                    + "jadi tidak bisa dibatalkan. Kembalikan sisa unit lewat retur penjualan supaya "
                    + "stok dan dana kembali seimbang.");
        }

        var changes = new List<StockChange>();
        var fgChanges = new List<FinishedGoodsChange>();

        // Nilai lama diserahkan ke repository untuk dibandingkan di dalam transaksi. Bila
        // pesanan berubah di antara pembacaan ini dan penulisan, perubahan ditolak, bukan
        // menimpa keputusan orang lain (stok bisa terpotong dua kali).
        var expectedStatus = order.Status;
        var expectedStockDeducted = order.StockDeducted;
        var expectedTotalPaid = order.TotalPaid();

        var shouldDeductStock = newStatus is OrderStatus.Processing or OrderStatus.Ready or OrderStatus.Paid
                                && !order.StockDeducted && order.ProductTemplateId.HasValue;

        var shouldRestoreStock = newStatus == OrderStatus.Cancelled
                                 && order.StockDeducted && order.ProductTemplateId.HasValue;

        if (shouldDeductStock || shouldRestoreStock)
        {
            // Produk jadi, bukan bahan mentah: bahan sudah dikurangi ketika SPK diselesaikan,
            // jadi menjual hasil produksi hanya menggeser saldo etalase.
            int displayId = await _locationStock.GetLocationIdByTypeAsync("Display");
            decimal jumlah = order.Quantity;
            // Pembatalan mengembalikan nilai persediaan sebesar HPP saat barang keluar, sehingga
            // saldo Persediaan Produk Jadi kembali ke angka sebelum penjualan.
            long hpp = shouldRestoreStock
                ? await _locationStock.GetFinishedGoodsSaleUnitCostAsync(orderId)
                : 0;
            fgChanges.Add(new FinishedGoodsChange(
                displayId, order.ProductTemplateId!.Value,
                shouldDeductStock ? -jumlah : jumlah,
                shouldDeductStock ? FinishedGoodsMovementType.Sale : FinishedGoodsMovementType.CancelRestore,
                "SalesOrder", orderId, userId,
                shouldDeductStock
                    ? $"Penjualan {order.OrderNumber} ({jumlah} unit)"
                    : $"Pengembalian stok pesanan dibatalkan {order.OrderNumber} ({jumlah} unit)",
                hpp));
            order.StockDeducted = shouldDeductStock;
        }

        var journals = new List<JournalEntry>();
        if (newStatus == OrderStatus.Cancelled)
        {
            // Setiap entri yang sudah terbit untuk pesanan ini dibalik, bukan hanya uang mukanya.
            var originals = await _journals.GetBySourceDocumentAsync("SalesOrder", orderId);
            journals.AddRange(AccountingService.BuildReversals(originals, userId));
        }

        order.Status = newStatus;
        if (newStatus == OrderStatus.Cancelled)
        {
            order.CancelledAt = DateTime.UtcNow;
            order.CancelReason = cancelReason;
        }

        try
        {
            await _orders.SaveStatusChangeWithJournalAsync(
                order, expectedStatus, expectedStockDeducted, expectedTotalPaid,
                changes, fgChanges, journals);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(ErrorCodes.ValidationFailed, ex.Message);
        }

        return Result.Ok();
    }

    private static string? ValidateOrderAmounts(long totalAmount, long downPayment)
        => MoneyRules.AmountError(totalAmount, "Nilai pesanan")
           ?? MoneyRules.CostError(downPayment, "Uang muka")
           ?? (downPayment > totalAmount ? "Uang muka melebihi nilai pesanan." : null);
}
