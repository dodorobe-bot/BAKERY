using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <summary>
/// Satu baris HPP: standar dari resep hari ini, dan aktual dari SPK yang sudah selesai.
/// Selisih positif berarti produksi memakan biaya lebih besar daripada resep — itu yang
/// harus terlihat, bukan disembunyikan di rata-rata seluruh produk.
/// </summary>
public record ProductCostRow(
    int ProductId, string Name, string? Category, bool IsActive,
    long SellingPriceMinorUnits, int RecipeLineCount,
    long StandardHppMinorUnits, decimal StandardMarginPercent,
    int CompletedWorkOrders, int GoodUnits,
    long ActualHppMinorUnits, long VarianceMinorUnits, decimal? VariancePercent,
    bool HasActual, bool BelowStandardCost);

public record CostSummaryView(
    IReadOnlyList<ProductCostRow> Products,
    int ProductCount, int ProductsWithRecipe, int ProductsWithoutRecipe,
    int ProductsBelowCost, int CompletedWorkOrders, int GoodUnits,
    long MaterialCostMinorUnits, long LaborCostMinorUnits, long OverheadCostMinorUnits,
    long WeightedActualHppMinorUnits);

/// <summary>
/// Ringkasan HPP dihitung dari dua sumber yang sudah ada: resep (harga bahan saat ini) dan
/// SPK selesai (bahan aktual + tenaga kerja + overhead). Tidak ada angka contoh di sini.
/// </summary>
public class CostSummaryService
{
    private readonly IProductTemplateRepository _products;
    private readonly IProductionRepository _production;

    public CostSummaryService(IProductTemplateRepository products, IProductionRepository production)
    {
        _products = products;
        _production = production;
    }

    public async Task<CostSummaryView> GetAsync(int workOrderLimit = 200)
    {
        var products = await _products.GetAllActiveAsync();
        var selesai = (await _production.GetAllAsync(workOrderLimit))
            .Where(w => w.Status == WorkOrderStatus.Completed)
            .ToList();

        var perProduk = selesai
            .GroupBy(w => w.ProductTemplateId)
            .ToDictionary(g => g.Key, g => new
            {
                Spk = g.Count(),
                Unit = g.Sum(w => Math.Max(0, w.ActualGoodQty)),
                Biaya = g.Sum(w => w.TotalMaterialCost + w.LaborCost + w.OverheadCost),
            });

        var rows = new List<ProductCostRow>(products.Count);
        foreach (var p in products)
        {
            var hppStandar = p.CalculateHpp();
            perProduk.TryGetValue(p.Id, out var aktual);
            var unit = aktual?.Unit ?? 0;
            var biaya = aktual?.Biaya ?? 0;
            var hppAktual = unit > 0 ? biaya / unit : 0;
            var selisih = unit > 0 ? hppAktual - hppStandar : 0;

            rows.Add(new ProductCostRow(
                p.Id, p.Name, p.Category, p.IsActive,
                p.SellingPrice, p.RecipeLines.Count,
                hppStandar, p.MarginPercent(),
                aktual?.Spk ?? 0, unit,
                hppAktual, selisih,
                unit > 0 && hppStandar > 0 ? Math.Round((decimal)selisih / hppStandar * 100, 1) : null,
                unit > 0,
                p.SellingPrice > 0 && hppStandar > p.SellingPrice));
        }

        var semuaUnit = selesai.Sum(w => Math.Max(0, w.ActualGoodQty));
        var semuaBiaya = selesai.Sum(w => w.TotalMaterialCost + w.LaborCost + w.OverheadCost);

        return new CostSummaryView(
            rows,
            rows.Count,
            rows.Count(r => r.RecipeLineCount > 0),
            rows.Count(r => r.RecipeLineCount == 0),
            rows.Count(r => r.BelowStandardCost),
            selesai.Count,
            semuaUnit,
            selesai.Sum(w => w.TotalMaterialCost),
            selesai.Sum(w => w.LaborCost),
            selesai.Sum(w => w.OverheadCost),
            semuaUnit > 0 ? semuaBiaya / semuaUnit : 0);
    }
}
