using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <summary>
/// Satu baris HPP: standar dari resep hari ini, dan aktual dari seluruh SPK yang sudah selesai.
/// Selisih positif berarti produksi memakan biaya lebih besar daripada resep — itu yang
/// harus terlihat, bukan disembunyikan di rata-rata seluruh produk.
/// </summary>
public record ProductCostRow(
    int ProductId, string Name, string? Category, bool IsActive,
    long SellingPriceMinorUnits, int RecipeLineCount,
    long StandardHppMinorUnits, decimal StandardMarginPercent,
    int CompletedWorkOrders, int GoodUnits, long ScrapLossMinorUnits,
    long ActualHppMinorUnits, long VarianceMinorUnits, decimal? VariancePercent,
    bool HasActual, bool BelowStandardCost);

public record CostSummaryView(
    IReadOnlyList<ProductCostRow> Products,
    int ProductCount, int ProductsWithRecipe, int ProductsWithoutRecipe,
    int ProductsBelowCost, int CompletedWorkOrders, int GoodUnits,
    long MaterialCostMinorUnits, long LaborCostMinorUnits, long OverheadCostMinorUnits,
    long ScrapLossMinorUnits, long WeightedActualHppMinorUnits,
    DateTime? FirstCompletedAt, DateTime? LastCompletedAt);

/// <summary>
/// Ringkasan HPP dihitung dari dua sumber yang sudah ada: resep (harga bahan saat ini) dan
/// seluruh SPK selesai (bahan aktual + tenaga kerja + overhead). Perhitungannya memakai
/// <see cref="ProductionCostRules"/> yang sama dengan penyelesaian SPK, sehingga HPP per unit
/// di layar ini selalu cocok dengan biaya yang benar-benar dibebankan ke stok produk jadi.
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

    public async Task<CostSummaryView> GetAsync()
    {
        var products = await _products.GetAllActiveAsync();
        var selesai = await _production.GetCompletedCostLinesAsync();

        // Kerugian bahan dihitung per SPK dengan aturan yang sama seperti saat penyelesaian:
        // hanya scrap yang dibuang, proporsional terhadap seluruh unit yang diolah. SPK tanpa
        // unit baik tidak memodalkan apa pun — seluruh bahannya menjadi kerugian.
        var perSpk = selesai.Select(w =>
        {
            var loss = w.GoodUnits == 0
                ? w.MaterialCostMinorUnits
                : ProductionCostRules.LossCost(
                    w.MaterialCostMinorUnits, w.LossUnits, w.GoodUnits + w.FailedUnits);
            var capitalized = w.GoodUnits == 0
                ? 0
                : Math.Max(0, w.MaterialCostMinorUnits - loss)
                  + w.LaborCostMinorUnits + w.OverheadCostMinorUnits;
            return new
            {
                w.ProductTemplateId,
                Unit = Math.Max(0, w.GoodUnits),
                w.MaterialCostMinorUnits,
                w.LaborCostMinorUnits,
                w.OverheadCostMinorUnits,
                Loss = loss,
                Capitalized = capitalized,
            };
        }).ToList();

        var perProduk = perSpk
            .GroupBy(w => w.ProductTemplateId)
            .ToDictionary(g => g.Key, g => new
            {
                Spk = g.Count(),
                Unit = g.Sum(w => w.Unit),
                RugiBahan = g.Sum(w => w.Loss),
                Dimodalkan = g.Sum(w => w.Capitalized),
            });

        var rows = new List<ProductCostRow>(products.Count);
        foreach (var p in products)
        {
            var hppStandar = p.CalculateHpp();
            perProduk.TryGetValue(p.Id, out var aktual);
            var unit = aktual?.Unit ?? 0;
            var hppAktual = unit > 0
                ? (long)Math.Round(aktual!.Dimodalkan / (decimal)unit, MidpointRounding.AwayFromZero)
                : 0;
            var selisih = unit > 0 ? hppAktual - hppStandar : 0;

            rows.Add(new ProductCostRow(
                p.Id, p.Name, p.Category, p.IsActive,
                p.SellingPrice, p.RecipeLines.Count,
                hppStandar, p.MarginPercent(),
                aktual?.Spk ?? 0, unit, aktual?.RugiBahan ?? 0,
                hppAktual, selisih,
                unit > 0 && hppStandar > 0 ? Math.Round((decimal)selisih / hppStandar * 100, 1) : null,
                unit > 0,
                p.SellingPrice > 0 && hppStandar > p.SellingPrice));
        }

        var semuaUnit = perSpk.Sum(w => w.Unit);
        var semuaDimodalkan = perSpk.Sum(w => w.Capitalized);

        return new CostSummaryView(
            rows,
            rows.Count,
            rows.Count(r => r.RecipeLineCount > 0),
            rows.Count(r => r.RecipeLineCount == 0),
            rows.Count(r => r.BelowStandardCost),
            selesai.Count,
            semuaUnit,
            selesai.Sum(w => w.MaterialCostMinorUnits),
            selesai.Sum(w => w.LaborCostMinorUnits),
            selesai.Sum(w => w.OverheadCostMinorUnits),
            perSpk.Sum(w => w.Loss),
            semuaUnit > 0
                ? (long)Math.Round(semuaDimodalkan / (decimal)semuaUnit, MidpointRounding.AwayFromZero)
                : 0,
            selesai.Min(w => w.CompletedAt),
            selesai.Max(w => w.CompletedAt));
    }
}
