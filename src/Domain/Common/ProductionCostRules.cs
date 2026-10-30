using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Aturan penilaian hasil produksi. Dipisah ke Domain supaya angka yang sama dipakai layar
/// laporan, jurnal, dan nilai stok produk jadi — bukan diulang di tiap pemanggil.
/// </summary>
public static class ProductionCostRules
{
    /// <summary>
    /// Unit yang benar-benar hilang. Rework bukan kerugian: unitnya masih akan dihasilkan
    /// kembali, jadi biayanya tetap menempel pada produksi, tidak dikeluarkan sebagai beban.
    /// </summary>
    public static int LossUnits(IEnumerable<WorkOrderScrap> scraps) =>
        scraps.Where(s => s.ActionTaken != "Rework").Sum(s => s.Quantity);

    /// <summary>
    /// Nilai bahan yang ikut hilang bersama produk rusak, proporsional terhadap jumlah unit.
    /// Dasar pembaginya total unit yang diolah (baik + rusak) karena biaya bahan dibayar untuk
    /// seluruh adonan yang masuk, bukan hanya yang lolos.
    /// </summary>
    public static long LossCost(long totalMaterialCost, int lossUnits, int totalUnits)
    {
        if (totalMaterialCost <= 0 || lossUnits <= 0 || totalUnits <= 0) return 0;
        long loss = (long)Math.Round(totalMaterialCost * (decimal)lossUnits / totalUnits,
                                    MidpointRounding.AwayFromZero);
        return Math.Clamp(loss, 0, totalMaterialCost);
    }

    /// <summary>
    /// Harga pokok produksi per unit produk jadi: bahan yang lolos (setelah dikurangi kerugian),
    /// tenaga kerja, dan overhead. Kerugian bahan tidak ikut diperhitungkan karena sudah
    /// dikeluarkan sebagai beban produk rusak pada jurnal yang sama.
    /// </summary>
    public static long CostPerGoodUnit(
        long totalMaterialCost, long laborCost, long overheadCost, long lossCost, int goodUnits)
    {
        if (goodUnits <= 0) return 0;
        long capitalized = Math.Max(0, totalMaterialCost - lossCost) + laborCost + overheadCost;
        return (long)Math.Round(capitalized / (decimal)goodUnits, MidpointRounding.AwayFromZero);
    }
}
