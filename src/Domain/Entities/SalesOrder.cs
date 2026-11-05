namespace Seruni22Bakery.Domain.Entities;

public enum OrderStatus { Pending, Processing, Ready, Paid, Cancelled }

public class SalesOrder
{
    public int Id { get; set; }
    public int? LegacyId { get; set; }
    public string? OrderNumber { get; set; }
    public int? CustomerId { get; set; }
    public int? ProductTemplateId { get; set; }
    public string CustomerNameSnapshot { get; set; } = string.Empty;
    public string? PhoneSnapshot { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime PickupDate { get; set; }
    /// <summary>Nilai barang sebelum diskon dan sebelum PPN ditambah (unit minor).</summary>
    public long SubtotalAmount { get; set; } = 0;
    /// <summary>Persen diskon yang dipakai (snapshot): null bila diskonnya nominal rupiah.</summary>
    public double? DiscountPercent { get; set; }
    public long DiscountAmount { get; set; } = 0;
    /// <summary>Tarif PPN yang berlaku saat pesanan dibuat; null bila pesanan ini tidak ber-PPN.</summary>
    public double? TaxRatePercent { get; set; }
    /// <summary>true bila harga katalog sudah dianggap termasuk PPN, sehingga PPN tidak menambah total.</summary>
    public bool TaxInclusive { get; set; } = false;
    public long TaxAmount { get; set; } = 0;
    public long TotalAmount { get; set; } = 0;    // minor units
    public long DownPayment { get; set; } = 0;    // minor units
    /// <summary>Jumlah unit produk jadi yang dipesan; selalu lebih dari nol (ditegakkan pemicu basis data).</summary>
    public decimal Quantity { get; set; } = 1;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public bool StockDeducted { get; set; } = false;
    /// <summary>Waktu barang diserahkan ke pelanggan; piutang usaha lahir di titik ini.</summary>
    public DateTime? DeliveredAt { get; set; }
    /// <summary>Waktu pendapatan pesanan ini dibukukan — satu pesanan hanya boleh sekali diakui.</summary>
    public DateTime? RevenueRecognizedAt { get; set; }
    public string? Notes { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public List<SalesPayment> Payments { get; set; } = new();

    public long TotalPaid() => DownPayment + Payments.Sum(p => p.Amount);
    public long RemainingAmount() => TotalAmount - TotalPaid();
    public bool IsFullyPaid() => RemainingAmount() <= 0;

    /// <summary>
    /// Nilai yang dikredit ke Pendapatan Penjualan: total tagihan tanpa PPN ditambah diskonnya,
    /// karena diskon baru terlihat sebagai pengurang saat 4-002 ikut dibukukan. Untuk harga
    /// termasuk PPN angka ini lebih kecil daripada harga katalog — selisihnya memang pajak.
    /// </summary>
    public long GrossRevenue() => TotalAmount - TaxAmount + DiscountAmount;
}

public class SalesPayment
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public long Amount { get; set; }             // minor units
    /// <summary>Uang yang benar-benar diserahkan pelanggan; null bila sama dengan Amount.</summary>
    public long? TenderAmount { get; set; }
    /// <summary>Uang kembalian yang keluar dari laci: TenderAmount - Amount.</summary>
    public long ChangeAmount { get; set; } = 0;
    public DateTime PaymentDate { get; set; }
    public string Method { get; set; } = "Cash";
    public string? Notes { get; set; }
    public int? JournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }
}
