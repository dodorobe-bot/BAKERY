namespace Seruni22Bakery.Domain.Entities;

public enum MovementType { Deduction, Restock, Adjustment, CancelRestore }

public class StockMovement
{
    public int Id { get; set; }
    public int IngredientId { get; set; }
    public MovementType MovementType { get; set; }
    public decimal Quantity { get; set; }  // bertanda: pengurangan bernilai negatif
    public int? LocationId { get; set; }
    public string? SourceType { get; set; }
    public int? SourceId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
}

/// <summary>Mutasi produk jadi. Terpisah dari MovementType bahan karena rujutannya Id produk, bukan Id bahan.</summary>
public enum FinishedGoodsMovementType { Production, Sale, CancelRestore, Adjustment, Return }

public class FinishedGoodsMovement
{
    public int Id { get; set; }
    public int ProductTemplateId { get; set; }
    public int LocationId { get; set; }
    public FinishedGoodsMovementType MovementType { get; set; }
    public decimal Quantity { get; set; }  // bertanda: penjualan bernilai negatif
    public long UnitCostMinorUnits { get; set; }
    public string? SourceType { get; set; }
    public int? SourceId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
}

public class FinishedGoodsStockItem
{
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public int ProductTemplateId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    /// <summary>false bila produknya sudah dinonaktifkan di master tetapi stoknya masih ada.</summary>
    public bool ProductIsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? SessionId { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
}

public class FiscalPeriod
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "Open"; // Open, Closed, Locked
    public DateTime? ClosedAt { get; set; }
    public int? ClosedByUserId { get; set; }
}
