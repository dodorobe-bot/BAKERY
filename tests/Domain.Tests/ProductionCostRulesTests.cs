using FluentAssertions;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Xunit;

namespace BakeryDesktop.Domain.Tests;

public class ProductionCostRulesTests
{
    private static WorkOrderScrap Scrap(int qty, string action) =>
        new() { Quantity = qty, ReasonCategory = "Gosong", ActionTaken = action };

    [Fact]
    public void LossUnits_MenghitungHanyaScrapYangDibuang()
    {
        // Keputusan bisnis: unit rework masih akan diproduksi lagi, jadi bukan kerugian.
        var scraps = new[] { Scrap(2, "Scrap_Loss"), Scrap(3, "Rework") };

        ProductionCostRules.LossUnits(scraps).Should().Be(2);
    }

    [Theory]
    [InlineData(200_000, 1, 4, 50_000)]
    [InlineData(200_000, 0, 4, 0)]
    [InlineData(0, 1, 4, 0)]
    [InlineData(200_000, 4, 4, 200_000)]
    // Kerugian tidak mungkin melebihi biaya bahan meskipun angka unit menyalahkan.
    [InlineData(100_000, 9, 4, 100_000)]
    public void LossCost_ProporsionalDanDibatasiBiayaBahan(
        long material, int lossUnits, int totalUnits, long expected)
    {
        ProductionCostRules.LossCost(material, lossUnits, totalUnits).Should().Be(expected);
    }

    [Fact]
    public void LossCost_MembulatPembagianKeSenTerdekat()
    {
        // 100.000 / 3 unit = 33.333,33 → dibulatkan, bukan dipotong, supaya jurnal tetap seimbang
        // dengan nilai stok yang memakai angka yang sama.
        ProductionCostRules.LossCost(100_000, 1, 3).Should().Be(33_333);
    }

    [Fact]
    public void CostPerGoodUnit_MemasukkanTenagaKerjaDanOverheadTapiTidakTermasukKerugian()
    {
        // bahan 200.000 - rugi 50.000 + gaji 40.000 + overhead 20.000 = 210.000 untuk 3 unit
        ProductionCostRules.CostPerGoodUnit(200_000, 40_000, 20_000, 50_000, 3).Should().Be(70_000);
    }

    [Fact]
    public void CostPerGoodUnit_TanpaUnitBaik_BernilaiNol()
    {
        ProductionCostRules.CostPerGoodUnit(200_000, 40_000, 20_000, 0, 0).Should().Be(0);
    }

    [Fact]
    public void CostPerGoodUnit_KerugianLebihBesarDariBahan_TidakMenjadiNilaiNegatif()
    {
        ProductionCostRules.CostPerGoodUnit(100_000, 0, 0, 250_000, 2).Should().Be(0);
    }
}
