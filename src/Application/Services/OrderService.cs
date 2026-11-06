using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

public record CreateOrderRequest(
    string CustomerName, string? Phone, string Description,
    DateTime PickupDate, long TotalAmount, long DownPayment,
    int? ProductTemplateId, string? Notes, int CreatedByUserId,
    string? IdempotencyKey = null,
    // Jumlah unit wajib dikirim. Nilai kosong tidak lagi dibaca sebagai satu unit secara diam-diam,
    // karena pesanan yang jumlahnya tidak diketahui bisa memotong stok produk jadi sebanyak 1.
    decimal? Quantity = null,
    // Diskon ditambahkan paling belakang untuk alasan yang sama. TotalAmount di atas berarti
    // nilai barang (harga × jumlah); total tagihan dihitung server dari diskon + pengaturan PPN.
    long? DiscountAmount = null,
    double? DiscountPercent = null);

public record AddPaymentRequest(
    int OrderId, long Amount, string Method, int CreatedByUserId,
    string? Notes = null, string? IdempotencyKey = null,
    // Uang yang diserahkan pelanggan. Bila lebih besar dari Amount, selisihnya kembalian yang
    // keluar dari laci dan tidak pernah masuk ke buku besar.
    long? TenderAmount = null);

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
    private readonly ISettingRepository _settings;

    public OrderService(
        ISalesOrderRepository orders,
        ILocationStockRepository locationStock,
        IJournalRepository journals, IDocumentSequenceRepository documents,
        AccountingService accounting, ISalesReturnRepository returns,
        ISettingRepository settings)
    {
        _orders = orders;
        _locationStock = locationStock;
        _journals = journals;
        _documents = documents;
        _accounting = accounting;
        _returns = returns;
        _settings = settings;
    }

    public async Task<Result<int>> CreateOrderAsync(CreateOrderRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerName))
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Nama pelanggan wajib diisi.");

        // Jumlah pesanan adalah satuan barang jadi, bukan berat. Pecahan seperti 0,5 unit tidak
        // pernah dijual dan membuat stok produk jadi bergerak setengah unit tanpa alasannya.
        if (req.Quantity is null)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Jumlah pesanan wajib diisi.");
        var jumlahPesanan = req.Quantity.Value;
        if (jumlahPesanan <= 0)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Jumlah pesanan harus lebih dari nol.");
        if (jumlahPesanan != decimal.Truncate(jumlahPesanan))
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                "Jumlah pesanan harus bilangan bulat (tidak boleh pecahan).");
        // Sama seperti pagaran nominal: jumlah di luar ini hampir selalu salah ketik, dan
        // stok etalase akan terpotong sebanyak angka tersebut saat status berubah.
        if (jumlahPesanan > MaxOrderQuantity)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Jumlah pesanan melebihi batas {MaxOrderQuantity:N0} unit.");

        var nilaiGalat = MoneyRules.AmountError(req.TotalAmount, "Nilai pesanan");
        if (nilaiGalat is not null)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, nilaiGalat);

        var diskonGalat = ValidateDiskon(req);
        if (diskonGalat is not null)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, diskonGalat);

        // Tarif pajak bukan masukan kasir: dibaca dari Pengaturan dan disimpan sebagai snapshot
        // pada pesanan, sehingga perubahan tarif nanti tidak menulis ulang pesanan lama.
        var pajak = TaxSettings.Dari(await _settings.GetAllAsync());
        var tarif = pajak.Enabled && pajak.RatePercent > 0 ? pajak.RatePercent : (double?)null;
        var harga = SalesPricing.Hitung(
            req.TotalAmount, req.DiscountAmount, req.DiscountPercent, tarif, pajak.Inclusive);

        var moneyError = ValidateOrderAmounts(harga.Total, req.DownPayment);
        if (moneyError is not null)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, moneyError);

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
            SubtotalAmount = harga.Subtotal,
            DiscountAmount = harga.Discount,
            DiscountPercent = req.DiscountPercent,
            TaxRatePercent = tarif,
            TaxInclusive = pajak.Inclusive,
            TaxAmount = harga.Tax,
            TotalAmount = harga.Total,
            DownPayment = req.DownPayment,
            Quantity = jumlahPesanan,
            Status = req.DownPayment >= harga.Total ? OrderStatus.Paid : OrderStatus.Pending,
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
            {
                journals.Add(await _accounting.BuildRevenueRecognitionAsync(
                    null, req.CustomerName, order.TotalAmount, req.DownPayment,
                    order.DiscountAmount, order.TaxAmount, req.CreatedByUserId));
                order.RevenueRecognizedAt = DateTime.UtcNow;
            }
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

        // Uang yang diserahkan pelanggan hanya ada pada pembayaran tunai. Transfer datang persis
        // sebesar yang ditransfer, sehingga "kembalian" tidak pernah bisa terjadi di jalur itu.
        long? kembalian = null;
        if (req.TenderAmount is { } tender)
        {
            if (metode != "Tunai")
                return Result.Fail(ErrorCodes.ValidationFailed,
                    "Uang yang diserahkan hanya diisi untuk pembayaran tunai; transfer dibukukan sebesar yang masuk.");
            var tenderGalat = MoneyRules.AmountError(tender, "Uang yang diserahkan");
            if (tenderGalat is not null)
                return Result.Fail(ErrorCodes.ValidationFailed, tenderGalat);
            if (tender < req.Amount)
                return Result.Fail(ErrorCodes.ValidationFailed,
                    $"Uang yang diserahkan {Money.Format(tender)} kurang dari pembayaran {Money.Format(req.Amount)}.");
            kembalian = tender - req.Amount;
        }

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

        // Pendapatan diakui sekali saja: saat barang diserahkan atau saat pesanan lunas, mana
        // yang terjadi lebih dulu. Setelah pengakuan itu uang yang masuk adalah pelunasan
        // piutang, bukan titipan pelanggan lagi.
        var sudahDiakui = order.RevenueRecognizedAt is not null || order.DeliveredAt is not null;

        var journals = new List<JournalEntry>
        {
            await _accounting.BuildPaymentAsync(
                order.Id, order.CustomerNameSnapshot, req.Amount, req.CreatedByUserId,
                melunasiPiutang: sudahDiakui)
        };
        if (fullyPaid && !sudahDiakui)
            journals.Add(await _accounting.BuildRevenueRecognitionAsync(
                order.Id, order.CustomerNameSnapshot, order.TotalAmount, order.TotalAmount,
                order.DiscountAmount, order.TaxAmount, req.CreatedByUserId));

        var payment = new SalesPayment
        {
            SalesOrderId = req.OrderId,
            Amount = req.Amount,
            TenderAmount = req.TenderAmount,
            ChangeAmount = kembalian ?? 0,
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

    /// <summary>
    /// Penyerahan barang ke pelanggan: titik di mana pendapatan boleh diakui dan sisa tagihan
    /// berdiri sebagai piutang. Sebelum ini uang pelanggan hanyalah titipan (2-001), walaupun
    /// pesanannya sudah dibayar penuh.
    /// </summary>
    public async Task<Result> MarkDeliveredAsync(int orderId, int userId)
    {
        var order = await _orders.GetByIdAsync(orderId);
        if (order is null)
            return Result.Fail(ErrorCodes.NotFound, "Pesanan tidak ditemukan.");
        if (order.Status == OrderStatus.Cancelled)
            return Result.Fail(ErrorCodes.InvalidOrderStatus, "Pesanan sudah dibatalkan.");
        if (order.DeliveredAt is not null)
            return Result.Fail(ErrorCodes.InvalidOrderStatus, "Pesanan ini sudah diserahkan.");

        var perluPengakuan = order.RevenueRecognizedAt is null;
        var journals = new List<JournalEntry>();
        if (perluPengakuan)
            journals.Add(await _accounting.BuildRevenueRecognitionAsync(
                order.Id, order.CustomerNameSnapshot, order.TotalAmount, order.TotalPaid(),
                order.DiscountAmount, order.TaxAmount, userId));

        // Barang yang diserahkan memang meninggalkan etalase. Pesanan yang sudah pernah
        // mengurangi stok (lunas lebih dulu) tidak dipotong dua kali.
        var changes = new List<FinishedGoodsChange>();
        if (!order.StockDeducted && order.ProductTemplateId.HasValue)
        {
            int displayId = await _locationStock.GetLocationIdByTypeAsync("Display");
            changes.Add(new FinishedGoodsChange(
                displayId, order.ProductTemplateId.Value, -order.Quantity,
                FinishedGoodsMovementType.Sale, "SalesOrder", orderId, userId,
                $"Penyerahan {order.OrderNumber} ({order.Quantity:0.###} unit)"));
        }

        try
        {
            await _orders.MarkDeliveredWithJournalAsync(
                orderId, DateTime.UtcNow, perluPengakuan, order.TotalPaid(), order.StockDeducted,
                journals, changes);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(ErrorCodes.ValidationFailed, ex.Message);
        }

        return Result.Ok();
    }

    private static string? ValidateDiskon(CreateOrderRequest req)
    {
        if (req.DiscountPercent is < 0 or > 100)
            return "Persen diskon harus antara 0 dan 100.";
        if (req.DiscountAmount < 0)
            return "Nilai diskon tidak boleh negatif.";
        // Dua diskon sekaligus tidak pernah berarti satu hal: dipakai keduanya akan salah, dan
        // salah satu dibuang diam-diam membuat kasir mengira diskonnya sudah terpasang.
        if (req.DiscountPercent > 0 && req.DiscountAmount > 0)
            return "Pilih salah satu diskon: persen atau nominal rupiah.";
        if (req.DiscountAmount > MoneyRules.MaxAmountMinorUnits)
            return $"Diskon melebihi batas {MoneyRules.MaxAmountMinorUnits / 100:N0} rupiah.";
        if (req.DiscountAmount > req.TotalAmount)
            return "Diskon melebihi nilai barang yang dipesan.";
        return null;
    }

    private static string? ValidateOrderAmounts(long totalAmount, long downPayment)
        => MoneyRules.AmountError(totalAmount, "Nilai pesanan")
           ?? MoneyRules.CostError(downPayment, "Uang muka")
           ?? (downPayment > totalAmount ? "Uang muka melebihi nilai pesanan." : null);
}
