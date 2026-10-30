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
    public long TotalAmount { get; set; } = 0;    // minor units
    public long DownPayment { get; set; } = 0;    // minor units
    /// <summary>Jumlah unit produk jadi yang dipesan; selalu lebih dari nol (ditegakkan pemicu basis data).</summary>
    public decimal Quantity { get; set; } = 1;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public bool StockDeducted { get; set; } = false;
    public string? Notes { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public List<SalesPayment> Payments { get; set; } = new();

    public long TotalPaid() => DownPayment + Payments.Sum(p => p.Amount);
    public long RemainingAmount() => TotalAmount - TotalPaid();
    public bool IsFullyPaid() => RemainingAmount() <= 0;
}

public class SalesPayment
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public long Amount { get; set; }             // minor units
    public DateTime PaymentDate { get; set; }
    public string Method { get; set; } = "Cash";
    public string? Notes { get; set; }
    public int? JournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }
}
