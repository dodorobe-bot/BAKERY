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

    // Quantity ditulis dalam satuan baris (mis. 250 g); BaseQuantity menerjemahkannya ke satuan
    // stok sebelum dikalikan harga, karena PricePerUnit selalu harga per satuan stok.
    public long CalculateHpp() =>
        RecipeLines.Sum(r => (long)(r.BaseQuantity * (r.Ingredient?.PricePerUnit ?? 0)));

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
    /// <summary>Jumlah dalam Unit; kosong berarti satuan stok bahan (baris resep lama).</summary>
    public decimal Quantity { get; set; }
    /// <summary>Satuan yang dipilih di resep, mis. "g". Null/kosong = satuan stok.</summary>
    public string? Unit { get; set; }
    public Ingredient? Ingredient { get; set; }

    public decimal BaseQuantity =>
        Quantity * (Ingredient is null ? 1m : Ingredient.FactorToBase(Unit));
}
