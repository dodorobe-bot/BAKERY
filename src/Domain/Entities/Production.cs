namespace Seruni22Bakery.Domain.Entities;

public enum WorkOrderStatus
{
    Planned,
    In_Process,
    Completed,
    Cancelled
}

public class WorkOrder
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int ProductTemplateId { get; set; }
    public string? ProductName { get; set; }
    public int TargetQuantity { get; set; }
    public int ActualGoodQty { get; set; } = 0;
    public int ActualFailedQty { get; set; } = 0;
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Planned;
    public long TotalMaterialCost { get; set; } = 0; // minor units (sen)
    public long LaborCost { get; set; } = 0;
    public long OverheadCost { get; set; } = 0;
    public DateTime? StartDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<WorkOrderMaterial> Materials { get; set; } = new();
    public List<WorkOrderScrap> Scraps { get; set; } = new();

    public long TotalCost() => TotalMaterialCost + LaborCost + OverheadCost;

    public long CostPerGoodUnit()
    {
        if (ActualGoodQty <= 0) return 0;
        return TotalCost() / ActualGoodQty;
    }
}

public class WorkOrderMaterial
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public int IngredientId { get; set; }
    public decimal StandardQty { get; set; }
    public decimal ActualQty { get; set; }
    public long UnitCost { get; set; } = 0;
    public string? IngredientName { get; set; }
    public string? Unit { get; set; }
}

public class WorkOrderScrap
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public int Quantity { get; set; }
    public string ReasonCategory { get; set; } = "Gosong"; // Gosong, Adonan_Gagal, Salah_Resep, Mesin_Error, Lainnya
    public string ActionTaken { get; set; } = "Scrap_Loss"; // Scrap_Loss, Rework
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
}

public class StorageLocation
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Warehouse"; // Warehouse, Kitchen, Display
    public bool IsActive { get; set; } = true;
}

public class LocationStock
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public int IngredientId { get; set; }
    public decimal Quantity { get; set; }
    public string? LocationName { get; set; }
    public string? IngredientName { get; set; }
    public string? Unit { get; set; }
    public decimal MinStockAlert { get; set; }
}
