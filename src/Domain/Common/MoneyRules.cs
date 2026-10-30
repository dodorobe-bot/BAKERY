namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Pagar nilai uang di sisi server. Seluruh nominal aplikasi ini disimpan dan dikirim
/// dalam satuan minor (rupiah × 100), jadi batas pun dinyatakan dalam satuan yang sama.
/// </summary>
public static class MoneyRules
{
    /// <summary>Atas satu nominal: Rp 10 miliar. Di atas angka ini hampir selalu salah ketik, bukan transaksi.</summary>
    public const long MaxAmountMinorUnits = 1_000_000_000_000;

    /// <summary>Nominal transaksi: harus positif dan tidak menembus atap. Null berarti lulus.</summary>
    public static string? AmountError(long amountMinorUnits, string label)
    {
        if (amountMinorUnits <= 0) return $"{label} harus lebih besar dari nol.";
        if (amountMinorUnits > MaxAmountMinorUnits)
            return $"{label} melebihi batas {MaxAmountMinorUnits / 100:N0} rupiah.";
        return null;
    }

    /// <summary>Biaya boleh nol, tidak boleh negatif dan tidak boleh menembus atap.</summary>
    public static string? CostError(long amountMinorUnits, string label)
    {
        if (amountMinorUnits < 0) return $"{label} tidak boleh negatif.";
        if (amountMinorUnits > MaxAmountMinorUnits)
            return $"{label} melebihi batas {MaxAmountMinorUnits / 100:N0} rupiah.";
        return null;
    }
}
