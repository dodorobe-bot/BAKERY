namespace Seruni22Bakery.Domain.Entities;

public enum PurchaseOrderStatus { Draft, Sent, PartialReceived, Received, Cancelled }

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PurchaseOrder
{
    public int Id { get; set; }
    public string PoNumber { get; set; } = "";
    public int SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDate { get; set; }
    public string? Notes { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PurchaseOrderLine> Lines { get; set; } = new();
}

public class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int IngredientId { get; set; }
    public string? IngredientName { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public long UnitCostMinorUnits { get; set; }
    public string Unit { get; set; } = "";
}

public class GoodsReceipt
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public DateTime ReceivedDate { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<GoodsReceiptLine> Lines { get; set; } = new();
}

public class GoodsReceiptLine
{
    public int Id { get; set; }
    public int GoodsReceiptId { get; set; }
    public int PurchaseOrderLineId { get; set; }
    public int IngredientId { get; set; }
    public string? IngredientName { get; set; }
    public decimal ReceivedQty { get; set; }
    public long UnitCostMinorUnits { get; set; }
    public int LocationId { get; set; }

    /// <summary>Opsional: nomor lot dan tanggal kedaluwarsa yang tercetak di kemasan saat barang diterima.</summary>
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
}
