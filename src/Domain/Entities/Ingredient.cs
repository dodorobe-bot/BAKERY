namespace Seruni22Bakery.Domain.Entities;

public class Ingredient
{
    public int Id { get; set; }
    public int? LegacyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string Unit { get; set; } = string.Empty;
    public long PricePerUnit { get; set; } = 0; // minor units (Rp x 100)
    public decimal CurrentStock { get; set; } = 0;
    public decimal MinStockAlert { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }

    public bool IsBelowMinStock() => CurrentStock <= MinStockAlert;
}
