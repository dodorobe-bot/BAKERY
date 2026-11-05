using System.Globalization;
using FluentAssertions;
using Seruni22Bakery.Domain.Common;

namespace BakeryDesktop.Domain.Tests;

/// <summary>
/// Kasus B2a-B2d, B3a-B3d, B6a: potongan harga, PPN, dan bagian retur.
///
/// Semua angka di bawah diambil dari lembar uji angka Seruni 22 (paket Rp 130.000, diskon 10%,
/// PPN 11%). Angka Acuan-nya ditulis lengkap supaya kalau suatu hari aturan pembulatan atau
/// letak diskon berubah, tes ini yang lebih dulu menyala merah — bukan laporan pajak toko.
/// </summary>
public class SalesPricingTests
{
    private const long NilaiPaket = 13_000_000;      // Rp 130.000
    private const long DiskonPaket = 1_300_000;      // Rp 13.000
    private const long DasarPpn = 11_700_000;        // Rp 117.000
    private const long PpnEksklusif = 1_287_000;     // Rp 12.870
    private const long TagihanEksklusif = 12_987_000; // Rp 129.870

    [Fact]
    public void DiskonPersenDanNominal_MenghasilkanTagihanYangSama()
    {
        var persen = SalesPricing.Hitung(NilaiPaket, null, 10, 11, false);
        var nominal = SalesPricing.Hitung(NilaiPaket, DiskonPaket, null, 11, false);

        persen.Should().Be(nominal,
            "kasir boleh memilih persen atau rupiah, hasil di struk harus angka yang sama");
        persen.Should().Be(new OrderPricing(
            NilaiPaket, DiskonPaket, PpnEksklusif, DasarPpn, TagihanEksklusif));
    }

    [Fact]
    public void PpnEksklusif_DitambahkanDiAtasTagihan()
    {
        var hasil = SalesPricing.Hitung(NilaiPaket, null, 10, 11, false);

        hasil.TaxBasis.Should().Be(DasarPpn, "yang dikenai PPN adalah nilai SETELAH diskon");
        hasil.Tax.Should().Be(PpnEksklusif);
        hasil.Total.Should().Be(DasarPpn + PpnEksklusif);
    }

    [Fact]
    public void PpnInklusif_HanyaMemindahkanSebagianTagihanKeKewajiban()
    {
        var hasil = SalesPricing.Hitung(NilaiPaket, null, 10, 11, true);

        hasil.Total.Should().Be(DasarPpn,
            "harga katalog sudah memuat PPN, jadi tagihan tidak bertambah");
        hasil.TaxBasis.Should().Be(10_540_500, "DPP = 117.000 / 1,11 dibulatkan ke Rupiah penuh");
        hasil.Tax.Should().Be(1_159_500, "PPN adalah selisih DPP terhadap tagihan, bukan 11% x DPP yang dibulatkan sendiri");
        (hasil.TaxBasis + hasil.Tax).Should().Be(hasil.Total);
    }

    [Fact]
    public void TanpaPajak_TagihanSamaDenganNilaiSetelahDiskon()
    {
        // null berarti "pesanan ini tidak ber-PPN", 0 berarti "ber-PPN dengan tarif nol".
        // Keduanya menghasilkan angka yang sama, tetapi hanya null yang boleh disimpan sebagai
        // snapshot TaxRatePercent, supaya pesanan lama tidak terbaca seolah-olah pernah dikenai pajak.
        SalesPricing.Hitung(NilaiPaket, null, 10, null, false).Should().Be(
            new OrderPricing(NilaiPaket, DiskonPaket, 0, DasarPpn, DasarPpn));
        SalesPricing.Hitung(NilaiPaket, null, 10, 0, false).Total.Should().Be(DasarPpn);
    }

    [Theory]
    [InlineData(50, 100)]
    [InlineData(49, 0)]
    [InlineData(149, 100)]
    [InlineData(150, 200)]
    [InlineData(199, 200)]
    [InlineData(0, 0)]
    public void BulatRupiah_MembulatSetengahKeAtas(long sen, long diharapkan) =>
        SalesPricing.BulatRupiah(sen).Should().Be(diharapkan);

    [Fact]
    public void DiskonLebihBesarDariNilaiBarang_DibatasiKeSubtotal()
    {
        var hasil = SalesPricing.Hitung(1_000_000, 2_000_000, null, null, false);

        hasil.Discount.Should().Be(1_000_000, "diskon tidak pernah membuat tagihan jadi negatif");
        hasil.Total.Should().Be(0);
        hasil.Tax.Should().Be(0);
    }

    [Theory]
    [InlineData(-1_000)]
    [InlineData(-100_000_000)]
    public void DiskonNegatif_DijadikanNol(long diskon)
    {
        // Server menolak diskon negatif lebih dulu; ini jaring kedua supaya tidak ada jalur
        // pemanggilan yang lupa lalu menghasilkan tagihan yang lebih besar dari harga katalog.
        SalesPricing.Hitung(NilaiPaket, diskon, null, null, false).Discount.Should().Be(0);
    }

    [Fact]
    public void PersenNol_DipakaiSebagaiTidakAdaDiskonNominal()
    {
        // diskonPersen 0 bukan "diskon 0%" yang menimpa diskon nominal: cabang persen hanya aktif
        // di atas nol, jadi pemanggilan (persen: 0, nominal: 13.000) tetap memakai diskon nominal.
        SalesPricing.Hitung(NilaiPaket, DiskonPaket, 0, null, false).Discount.Should().Be(DiskonPaket);
    }

    [Fact]
    public void SubtotalNegatif_DitolakDenganGalat()
    {
        var galat = Record.Exception(() => SalesPricing.Hitung(-100, null, null, null, false));

        galat.Should().BeOfType<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ProporsionalRetur_MembagiNilaiMenurutJumlahUnit()
    {
        // Pesanan 3 roti, tagihan Rp 59.940 = 5.994.000 sen; diskon 600.000; PPN 594.000.
        SalesPricing.Proporsional(5_994_000, 1, 3).Should().Be(1_998_000, "Rp 19.980 = nilai 1 roti");
        SalesPricing.Proporsional(600_000, 1, 3).Should().Be(200_000);
        SalesPricing.Proporsional(594_000, 1, 3).Should().Be(198_000);
    }

    [Fact]
    public void ProporsionalReturSeluruhnya_MengembalikanNilaiPenuh()
    {
        SalesPricing.Proporsional(5_994_000, 3, 3).Should().Be(5_994_000);
        SalesPricing.Proporsional(5_994_000, 4, 3).Should().Be(5_994_000);
    }

    [Fact]
    public void Proporsional_ReturBertigaMenutupSisaSetelahPembulatan()
    {
        // 100.000 / 3 = 33.333,33 -> dua retur pertama 33.333. Retur terakhir di layanan memakai
        // SISA nilai (33.334), bukan hasil perkalian ulang, sehingga total retur menutup pesanan
        // persis tanpa sisa pembulatan yang menggantung di 4-002 atau 2-003.
        var pertama = SalesPricing.Proporsional(100_000, 1, 3);
        var kedua = SalesPricing.Proporsional(100_000, 1, 3);
        var sisa = 100_000 - pertama - kedua;

        pertama.Should().Be(33_333);
        sisa.Should().Be(33_334);
        (pertama + kedua + sisa).Should().Be(100_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Proporsional_JumlahPesananTidakSah_MenghasilkanNol(int jumlahPesanan) =>
        SalesPricing.Proporsional(1_000_000, 1, jumlahPesanan).Should().Be(0);

    [Fact]
    public void Proporsional_NilaiNolAtauMinus_MenghasilkanNol()
    {
        SalesPricing.Proporsional(0, 1, 3).Should().Be(0);
        SalesPricing.Proporsional(-1_000, 1, 3).Should().Be(0);
    }

    [Fact]
    public void TaxSettings_TanpaKunci_PajakMatiDanTarifBawaanHanyaUsulan()
    {
        var config = TaxSettings.Dari(new Dictionary<string, string>());

        config.Enabled.Should().BeFalse(
            "tarif bawaan tidak boleh dipakai diam-diam; toko harus mengaktifkan PPN sendiri");
        config.RatePercent.Should().Be(TaxSettings.DefaultRatePercent);
        config.Inclusive.Should().BeFalse();
    }

    [Fact]
    public void TaxSettings_MembacaKetigaKunciPengaturan()
    {
        var config = TaxSettings.Dari(new Dictionary<string, string>
        {
            [TaxSettings.EnabledKey] = "1",
            [TaxSettings.RateKey] = "12",
            [TaxSettings.InclusiveKey] = "true"
        });

        config.Should().Be(new TaxSettings.TaxConfig(true, 12, true));
    }

    [Fact]
    public void TaxSettings_TarifTidakSah_JatuhKeBawaanBukanKeNol()
    {
        var config = TaxSettings.Dari(new Dictionary<string, string>
        {
            [TaxSettings.EnabledKey] = "true",
            [TaxSettings.RateKey] = "sebelas"
        });

        config.RatePercent.Should().Be(TaxSettings.DefaultRatePercent);
        config.Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("11")]
    [InlineData("0")]
    [InlineData("11.5")]
    [InlineData("100")]
    public void ValidasiTarif_MenerimaAngkaPersenDalamBahasaInvariant(string nilai) =>
        TaxSettings.Validate(TaxSettings.RateKey, nilai).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("150")]
    [InlineData("-1")]
    [InlineData("11,5")]
    [InlineData(" 11 ")]
    public void ValidasiTarif_MenolakNilaiDiLuarJangkauanAtauBukanAngkaPolos(string nilai)
    {
        // "11,5" dan " 11 " salah ketik lokal/ hasil salin. Menolaknya menjaga snapshot pesanan
        // sama dengan angka yang dilihat pemilik di layar Pengaturan, berapa pun bahasanya.
        TaxSettings.Validate(TaxSettings.RateKey, nilai).Should().NotBeNull();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("False")]
    [InlineData(null)]
    [InlineData("")]
    public void ValidasiSaklarPajak_MenerimaNilaiBenarSalah(string? nilai) =>
        TaxSettings.Validate(TaxSettings.EnabledKey, nilai).Should().BeNull();

    [Theory]
    [InlineData("ya")]
    [InlineData("mungkin")]
    [InlineData("2")]
    public void ValidasiSaklarPajak_MenolakTeksBebas(string nilai) =>
        TaxSettings.Validate(TaxSettings.InclusiveKey, nilai)
            .Should().Be("Penggunaan PPN harus memilih Ya atau Tidak.");

    [Fact]
    public void Validasi_KunciLainTidakDinilai() =>
        TaxSettings.Validate("store_name", "Seruni 22").Should().BeNull();

    [Fact]
    public void TarifDiatasSeratusDitolakDanBatasnyaDisebutkan()
    {
        var galat = TaxSettings.Validate(TaxSettings.RateKey,
            (TaxSettings.MaxRatePercent + 0.1).ToString(CultureInfo.InvariantCulture));

        galat.Should().NotBeNull();
        galat.Should().Contain("Tarif PPN");
    }
}
