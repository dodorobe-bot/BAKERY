namespace Seruni22Bakery.Domain.Entities;

public class ProductTemplate
{
    public int Id { get; set; }
    public int? LegacyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    /// <summary>Kode yang dibaca pemindai barcode; boleh kosong untuk produk tanpa label.</summary>
    public string? Barcode { get; set; }
    public long SellingPrice { get; set; } = 0; // minor units
    public bool IsActive { get; set; } = true;
    public List<RecipeLine> RecipeLines { get; set; } = new();

    public long CalculateHpp() =>
        RecipeLines.Sum(r => (long)(r.Quantity * r.Ingredient?.PricePerUnit ?? 0));

    public decimal MarginPercent()
    {
        if (SellingPrice == 0) return 0;
        var hpp = CalculateHpp();
        return (decimal)(SellingPrice - hpp) / SellingPrice * 100;
    }
}

public class RecipeLine
{
    public int Id { get; set; }
    public int ProductTemplateId { get; set; }
    public int IngredientId { get; set; }
    public decimal Quantity { get; set; }
    public Ingredient? Ingredient { get; set; }
}
