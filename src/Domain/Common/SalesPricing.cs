namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Kunci PPN di AppSettings. Nilainya dibaca server saat pesanan dibuat dan disimpan sebagai
/// snapshot di pesanan itu, jadi mengubah tarif bulan depan tidak mengubah pajak pesanan lama
/// — dan transaksi baru memakai tarif yang baru.
/// </summary>
public static class TaxSettings
{
    public const string EnabledKey = "tax_enabled";
    public const string RateKey = "tax_rate_percent";
    public const string InclusiveKey = "tax_inclusive";

    /// <summary>Tarif bawaan yang ditawarkan ke pemilik toko, tetapi tidak dipakai diam-diam.</summary>
    public const double DefaultRatePercent = 11;

    public const double MaxRatePercent = 100;

    public sealed record TaxConfig(bool Enabled, double RatePercent, bool Inclusive);

    public static TaxConfig Dari(IReadOnlyDictionary<string, string> settings)
    {
        var aktif = BacaBoolean(Ambil(settings, EnabledKey), false);
        var tarif = BacaTarif(Ambil(settings, RateKey));
        var inklusif = BacaBoolean(Ambil(settings, InclusiveKey), false);
        return new TaxConfig(aktif, tarif ?? DefaultRatePercent, inklusif);
    }

    private static string? Ambil(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var nilai) ? nilai : null;

    /// <summary>Pesan galat untuk nilai pengaturan pajak, atau null bila sah.</summary>
    public static string? Validate(string key, string? value)
    {
        switch (key)
        {
            case EnabledKey:
            case InclusiveKey:
                if (value is null or "" or "0" or "1" or "true" or "false" or "True" or "False") return null;
                return "Penggunaan PPN harus memilih Ya atau Tidak.";
            case RateKey:
                if (string.IsNullOrWhiteSpace(value)) return "Tarif PPN harus diisi dengan angka persen.";
                if (!double.TryParse(value, System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture, out var tarif)
                    || tarif < 0 || tarif > MaxRatePercent)
                    return $"Tarif PPN harus antara 0 dan {MaxRatePercent:N0} persen.";
                return null;
            default:
                return null;
        }
    }

    private static double? BacaTarif(string? nilai) =>
        double.TryParse(nilai, System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var tarif)
            ? tarif
            : null;

    private static bool BacaBoolean(string? nilai, bool bawaan)
    {
        if (string.IsNullOrWhiteSpace(nilai)) return bawaan;
        return nilai.Trim() == "1" || nilai.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Hasil perhitungan harga satu pesanan, seluruhnya dalam satuan minor (sen).</summary>
/// <param name="Subtotal">Nilai barang sebelum diskon dan sebelum PPN ditambah.</param>
/// <param name="Discount">Nilai diskon yang dipakai.</param>
/// <param name="Tax">Nilai PPN.</param>
/// <param name="TaxBasis">Dasar Pengenaan Pajak — nilai yang dikenai PPN.</param>
/// <param name="Total">Tagihan ke pelanggan.</param>
public readonly record struct OrderPricing(long Subtotal, long Discount, long Tax, long TaxBasis, long Total);

/// <summary>
/// Aturan perhitungan harga pesanan: diskon, PPN, dan total tagihan.
///
/// Semua angka dibulatkan ke Rupiah penuh (100 sen), bukan ke sen, karena struk, laci uang, dan
/// laporan pajak toko tidak mengenal pecahan Rupiah. Aturan pembagiannya:
///  - PPN eksklusif: DPP = setelah diskon; PPN ditambahkan di atas tagihan.
///  - PPN inklusif: harga katalog sudah memuat PPN, sehingga DPP = tagihan / (1 + tarif) dan
///    PPN adalah selisihnya. Diskon tetap dipotong dari harga katalog sebelum pemisahan itu.
/// Karena itu total tagihan hanya bertambah oleh PPN pada mode eksklusif; mode inklusif hanya
/// memindahkan sebagian tagihan dari pendapatan ke kewajiban.
/// </summary>
public static class SalesPricing
{
    /// <summary>Membulatkan sen ke Rupiah penuh terdekat (0,5 ke atas dibulati ke besar).</summary>
    public static long BulatRupiah(long sen) =>
        (long)Math.Round(sen / 100m, 0, MidpointRounding.AwayFromZero) * 100;

    /// <summary>
    /// Menghitung satu pesanan. tarifPpn null berarti pesanan ini tidak ber-PPN sama sekali,
    /// bukan ber-PPN nol, supaya snapshot di pesanan bisa dibedakan dari pesanan tanpa pajak.
    /// </summary>
    public static OrderPricing Hitung(
        long subtotal, long? diskonNominal, double? diskonPersen, double? tarifPpn, bool inklusif)
    {
        if (subtotal < 0)
            throw new ArgumentOutOfRangeException(nameof(subtotal), "Subtotal pesanan tidak boleh negatif.");

        var diskon = diskonPersen is > 0
            ? BulatRupiah((long)Math.Round(subtotal * (decimal)diskonPersen.Value / 100m,
                MidpointRounding.AwayFromZero))
            : BulatRupiah(diskonNominal ?? 0);
        diskon = Math.Clamp(diskon, 0, subtotal);

        var tagihanSebelumPpn = subtotal - diskon;

        if (tarifPpn is not > 0)
            return new OrderPricing(subtotal, diskon, 0, tagihanSebelumPpn, tagihanSebelumPpn);

        var tarif = (decimal)tarifPpn.Value;
        if (inklusif)
        {
            var dpp = BulatRupiah((long)Math.Round(tagihanSebelumPpn * 100m / (100m + tarif),
                MidpointRounding.AwayFromZero));
            var ppn = tagihanSebelumPpn - dpp;
            return new OrderPricing(subtotal, diskon, ppn, dpp, tagihanSebelumPpn);
        }

        var ppnEksklusif = BulatRupiah((long)Math.Round(tagihanSebelumPpn * tarif / 100m,
            MidpointRounding.AwayFromZero));
        return new OrderPricing(subtotal, diskon, ppnEksklusif, tagihanSebelumPpn,
            tagihanSebelumPpn + ppnEksklusif);
    }

    /// <summary>
    /// Bagian dari satu nominal pesanan untuk sejumlah unit yang diretur. Sisa nilai ditentukan
    /// oleh pengurangannya, bukan oleh perkalian ulang, supaya total seluruh retur menutup
    /// pesanan persis tanpa sisa pembulatan.
    /// </summary>
    public static long Proporsional(long nilai, decimal jumlahRetur, decimal jumlahPesanan)
    {
        if (jumlahPesanan <= 0 || nilai <= 0) return 0;
        if (jumlahRetur >= jumlahPesanan) return nilai;
        return (long)Math.Round(nilai * jumlahRetur / jumlahPesanan, MidpointRounding.AwayFromZero);
    }
}
