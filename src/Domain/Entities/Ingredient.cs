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
    /// <summary>Satuan turunan yang boleh dipakai baris resep; semua stok tetap disimpan dalam Unit.</summary>
    public List<IngredientUnit> AdditionalUnits { get; set; } = new();

    public bool IsBelowMinStock() => CurrentStock <= MinStockAlert;

    /// <summary>
    /// Faktor dari satuan yang diketik di resep ke satuan stok bahan. Kosong berarti satuan stok
    /// itu sendiri (faktor 1). Satuan asing dilempar, bukan dibaca sebagai 1: salah ketik "grm"
    /// tidak boleh diam-diam dinilai seperti kilogram.
    /// </summary>
    public decimal FactorToBase(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return 1m;
        var diminta = unit.Trim();
        if (string.Equals(diminta, Unit.Trim(), StringComparison.OrdinalIgnoreCase)) return 1m;
        var turunan = AdditionalUnits.FirstOrDefault(u =>
            string.Equals(u.Unit.Trim(), diminta, StringComparison.OrdinalIgnoreCase));
        if (turunan is null)
            throw new InvalidOperationException(
                $"Satuan \"{diminta}\" belum terdaftar untuk bahan \"{Name}\" (satuan stok {Unit}).");
        return turunan.FactorToBase;
    }

    public bool KnowsUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return true;
        var diminta = unit.Trim();
        return string.Equals(diminta, Unit.Trim(), StringComparison.OrdinalIgnoreCase)
            || AdditionalUnits.Any(u => string.Equals(u.Unit.Trim(), diminta, StringComparison.OrdinalIgnoreCase));
    }
}

public class IngredientUnit
{
    public int Id { get; set; }
    public int IngredientId { get; set; }
    public string Unit { get; set; } = string.Empty;
    /// <summary>1 satuan ini = berapa satuan stok (mis. 1 g = 0,001 kg).</summary>
    public decimal FactorToBase { get; set; } = 1m;
}
