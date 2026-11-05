using FluentAssertions;
using Seruni22Bakery.Application.Devices;
using Seruni22Bakery.Domain.Entities;

namespace BakeryDesktop.Application.Tests;

/// <summary>
/// Struk adalah bukti transaksi yang dipegang pelanggan. Tes ini menuntut isinya berasal dari
/// pesanan tersimpan (nomor nota, jumlah, total, rupiah) dan muat pada lebar kertas yang dipilih,
/// karena struk yang terpotong di 58 mm tidak bisa dipakai sebagai bukti.
/// </summary>
public class ReceiptRendererTests
{
    private static SalesOrder Pesanan(long total = 4_500_000, decimal qty = 3, long dp = 0)
    {
        return new SalesOrder
        {
            Id = 7,
            OrderNumber = "ORD-2026-000042",
            CustomerNameSnapshot = "Ibu Ratna",
            PhoneSnapshot = "081234567890",
            Description = "Nastar klasik isi 3 toples dan bolu lapis legit ukuran sedang",
            PickupDate = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Unspecified),
            TotalAmount = total,
            DownPayment = dp,
            Quantity = qty,
            CreatedAt = new DateTime(2026, 10, 27, 8, 15, 0, DateTimeKind.Utc),
        };
    }

    [Theory]
    [InlineData(58, 32)]
    [InlineData(80, 48)]
    public void Kolom_MengikutiLebarKertas(int mm, int kolom) =>
        ReceiptRenderer.Kolom(mm).Should().Be(kolom);

    [Fact]
    public void Render_MemuatNomorNotaJumlahDanTotalRupiah()
    {
        var struk = ReceiptRenderer.Render(Pesanan(), "Seruni 22 Bakery Cabang Kota", 80);

        struk.Should().Contain("ORD-2026-000042");
        struk.Should().Contain("3 x Rp 15.000");
        struk.Should().Contain("Total");
        struk.Should().Contain("Rp 45.000");
    }

    [Theory]
    [InlineData(58)]
    [InlineData(80)]
    public void Render_TidakAdaBarisMelebihiLebarKertas(int mm)
    {
        var lebar = ReceiptRenderer.Kolom(mm);

        var baris = ReceiptRenderer.Render(Pesanan(), "Outlet", mm)
            .Split('\n')
            .Select(b => b.TrimEnd('\r'))
            .ToList();

        baris.Should().OnlyContain(b => b.Length <= lebar,
            $"struk {mm} mm hanya menampung {lebar} kolom");
    }

    [Fact]
    public void Render_DeskripsiPanjang_DibungkusBukanDipotong()
    {
        var struk = ReceiptRenderer.Render(Pesanan(), "Outlet", 58);

        struk.Should().Contain("lapis legit");
        struk.Should().Contain("ukuran sedang", "kata terakhir deskripsi tidak boleh hilang");
    }

    [Fact]
    public void Render_Lunas_MemuatPenandaLunasDanTanpaSisa()
    {
        var order = Pesanan();
        order.Payments.Add(new SalesPayment { Amount = order.TotalAmount, Method = "Cash" });

        var struk = ReceiptRenderer.Render(order, "Outlet", 80);

        struk.Should().Contain("LUNAS");
        struk.Should().Contain("Pembayaran");
        struk.Should().Contain("Sisa Tagihan");
        struk.Should().Contain("Rp 0");
    }

    [Fact]
    public void Render_UangMuka_TampilSebagaiDpDanSisa()
    {
        var struk = ReceiptRenderer.Render(Pesanan(dp: 1_500_000), "Outlet", 80);

        struk.Should().Contain("DP / Uang Muka");
        struk.Should().Contain("Rp 15.000");
        struk.Should().Contain("Rp 30.000", "sisa tagihan = total - DP");
        struk.Should().NotContain("LUNAS");
    }

    [Fact]
    public void Render_TanpaCetakUlang_TidakMemuatPenanda()
    {
        ReceiptRenderer.Render(Pesanan(), "Outlet", 80).Should().NotContain("CETAK ULANG");
    }

    // Acuan Batch B: paket Rp 130.000 diskon 10% (Rp 13.000), PPN 11% eksklusif (Rp 12.870),
    // tagihan Rp 129.870, dibayar tunai Rp 150.000 dan kembalian Rp 20.130. Angka-angka ini sama
    // dengan yang dihitung server dan yang diuji lewat probe hidup, jadi struk harus menampilkan
    // semuanya — pelanggan yang memegang struk harus bisa menjumlah sendiri.
    private static SalesOrder PesananBerpajak(
        long subtotal = 13_000_000,
        long diskon = 1_300_000,
        long ppn = 1_287_000,
        long total = 12_987_000,
        double tarif = 11,
        bool inklusif = false,
        long dp = 0,
        decimal qty = 3)
    {
        var order = Pesanan(total: total, qty: qty, dp: dp);
        order.SubtotalAmount = subtotal;
        order.DiscountAmount = diskon;
        order.DiscountPercent = 10;
        order.TaxRatePercent = tarif;
        order.TaxInclusive = inklusif;
        order.TaxAmount = ppn;
        return order;
    }

    private static string BarisLabel(string struk, string label) =>
        struk.Split('\n').Select(b => b.TrimEnd('\r')).Single(b => b.StartsWith(label, StringComparison.Ordinal));

    [Fact]
    public void Render_DiskonDanPpn_MencetakRincianYangBisaDijumlahSendiri()
    {
        var struk = ReceiptRenderer.Render(PesananBerpajak(), "Outlet", 80);

        BarisLabel(struk, "3 x Rp 43.333").Should().EndWith("Rp 130.000",
            "harga per unit adalah harga katalog, bukan tagihan yang sudah diubah diskon dan PPN");
        BarisLabel(struk, "Nilai Barang").Should().EndWith("Rp 130.000");
        BarisLabel(struk, "Diskon").Should().EndWith("- Rp 13.000");
        BarisLabel(struk, "PPN 11%").Should().EndWith("Rp 12.870");
        BarisLabel(struk, "Total").Should().EndWith("Rp 129.870");
        (13_000_000 - 1_300_000 + 1_287_000).Should().Be(12_987_000);
    }

    [Fact]
    public void Render_KembalianTunai_MencetakUangDiserahkanDanKembaliannya()
    {
        var order = PesananBerpajak();
        order.Payments.Add(new SalesPayment
        {
            Amount = order.TotalAmount,
            TenderAmount = 15_000_000,
            ChangeAmount = 15_000_000 - order.TotalAmount,
            Method = "Tunai",
        });

        var struk = ReceiptRenderer.Render(order, "Outlet", 80);

        BarisLabel(struk, "Tunai Diterima").Should().EndWith("Rp 150.000");
        BarisLabel(struk, "Kembalian").Should().EndWith("Rp 20.130");
        BarisLabel(struk, "Sisa Tagihan").Should().EndWith("Rp 0");
        struk.Should().Contain("LUNAS");
        struk.Should().NotContain("Pembayaran",
            "uang yang dibukukan sudah tercetak sebagai Tunai Diterima; baris ganda membingungkan");
    }

    [Fact]
    public void Render_PpnInklusif_LabelMenandaiBahwaPajakTidakMenambahTagihan()
    {
        var order = PesananBerpajak(
            subtotal: 13_000_000, diskon: 1_300_000, ppn: 1_159_500, total: 11_700_000, inklusif: true);

        var struk = ReceiptRenderer.Render(order, "Outlet", 80);

        BarisLabel(struk, "PPN 11% (termasuk)").Should().EndWith("Rp 11.595");
        BarisLabel(struk, "Total").Should().EndWith("Rp 117.000");
    }

    [Fact]
    public void Render_DpDanPembayaranBerbeda_BarisnyaBerbedaDanTidakMengganda()
    {
        var order = PesananBerpajak(dp: 5_000_000);
        order.Payments.Add(new SalesPayment { Amount = 7_987_000, Method = "Transfer" });

        var struk = ReceiptRenderer.Render(order, "Outlet", 80);

        BarisLabel(struk, "DP / Uang Muka").Should().EndWith("Rp 50.000");
        BarisLabel(struk, "Pembayaran").Should().EndWith("Rp 79.870");
        BarisLabel(struk, "Sisa Tagihan").Should().EndWith("Rp 0");
        struk.Should().NotContain("Tunai Diterima", "transfer tidak punya uang yang diserahkan ke kasir");
        struk.Should().NotContain("Kembalian");
    }

    [Fact]
    public void Render_TanpaDiskonDanPpn_HanyaNilaiBarangDanTotal()
    {
        var struk = ReceiptRenderer.Render(PesananBerpajak(diskon: 0, ppn: 0, total: 13_000_000), "Outlet", 80);

        BarisLabel(struk, "Nilai Barang").Should().EndWith("Rp 130.000");
        BarisLabel(struk, "Total").Should().EndWith("Rp 130.000");
        struk.Should().NotContain("Diskon");
        struk.Should().NotContain("PPN");
    }

    [Fact]
    public void Render_PesananLama_TanpaNilaiBarang_TetapTerbacaDanTidakMenampilkanNol()
    {
        var struk = ReceiptRenderer.Render(Pesanan(total: 4_500_000, qty: 3), "Outlet", 80);

        var nilaiBarang = BarisLabel(struk, "Nilai Barang");
        nilaiBarang.Should().EndWith("Rp 45.000");
        nilaiBarang.Should().NotMatchRegex(@"Rp 0\s*$",
            "pesanan lama memakai tagihan, bukan nol, supaya struk tidak terbaca barangnya gratis");
        struk.Should().NotContain("PPN");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_RincianDiskonPpnKembalian_MuatDiKertas58mm(bool inklusif)
    {
        var order = PesananBerpajak(
            subtotal: 99_990_000, diskon: 9_990_000, ppn: inklusif ? 8_910_000 : 9_900_000,
            total: inklusif ? 90_000_000 : 99_900_000, inklusif: inklusif);
        order.Payments.Add(new SalesPayment
        {
            Amount = order.TotalAmount,
            TenderAmount = 100_000_000,
            ChangeAmount = 100_000_000 - order.TotalAmount,
            Method = "Tunai",
        });

        var lebar = ReceiptRenderer.Kolom(58);
        ReceiptRenderer.Render(order, "Outlet", 58)
            .Split('\n').Select(b => b.TrimEnd('\r'))
            .Should().OnlyContain(b => b.Length <= lebar,
                $"label diskon/PPN/kembalian terpanjang tetap harus muat di {lebar} kolom");
    }

    [Fact]
    public void Render_PpnTermasukTarifPecahan_MencetakTarifSnapshotPesanan()
    {
        var struk = ReceiptRenderer.Render(
            PesananBerpajak(diskon: 0, ppn: 1_150_000, total: 11_150_000, tarif: 11.5, inklusif: true),
            "Outlet", 80);

        BarisLabel(struk, "PPN 11.5% (termasuk)").Should().EndWith("Rp 11.500");
    }

    [Fact]
    public void Render_PesananTanpaTarifTetapiBerppn_LabelTidakMengarangPersen()
    {
        var order = PesananBerpajak(diskon: 0, ppn: 1_287_000, total: 12_987_000);
        order.TaxRatePercent = null;

        BarisLabel(ReceiptRenderer.Render(order, "Outlet", 80), "PPN").Should().EndWith("Rp 12.870");
    }

    [Fact]
    public void Render_CetakUlang_MemuatPenandaJelas()
    {
        ReceiptRenderer.Render(Pesanan(), "Outlet", 80, cetakUlang: true)
            .Should().Contain("*** CETAK ULANG ***");
    }

    [Fact]
    public void Render_NamaOutletKosong_MemakaiNamaToko()
    {
        ReceiptRenderer.Render(Pesanan(), "   ", 80).Should().Contain("Seruni 22 Bakery");
    }

    [Fact]
    public void Render_MemakaiMerekDariPengaturan_BukanTeksTetapDiKode()
    {
        var struk = ReceiptRenderer.Render(Pesanan(), "Cabang Kota", 80,
            brandName: "Toko Roti Bu Ratna", brandTagline: "Roti hangat setiap hari");

        struk.Should().Contain("Toko Roti Bu Ratna");
        struk.Should().Contain("Roti hangat setiap hari");
        struk.Should().Contain("Cabang Kota", "nama tempat kerja tetap dicetak sebagai baris tersendiri");
        struk.Should().NotContain("Seruni 22 Bakery", "merek lama tidak boleh tertinggal di struk");
        struk.Should().NotContain("Toko Roti & Katering");
    }

    [Fact]
    public void Render_MerekSamaDenganOutlet_TidakMencetakBarisGanda()
    {
        var struk = ReceiptRenderer.Render(Pesanan(), "Toko Roti Bu Ratna", 80,
            brandName: "Toko Roti Bu Ratna");

        struk.Split('\n').Count(b => b.Contains("Toko Roti Bu Ratna")).Should().Be(1);
    }

    [Fact]
    public void RenderTest_MemakaiMerekDariPengaturan()
    {
        var isi = ReceiptRenderer.RenderTest("Cabang Kota", "EPSON TM-T82", 80,
            new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Local),
            brandName: "Toko Roti Bu Ratna", brandTagline: "Roti hangat setiap hari");

        isi.Should().Contain("Toko Roti Bu Ratna").And.Contain("Roti hangat setiap hari");
        isi.Should().NotContain("Seruni 22 Bakery");
    }

    [Theory]
    [InlineData(58)]
    [InlineData(80)]
    public void RenderTest_MemuatLebarDanPenandaUji(int mm)
    {
        var isi = ReceiptRenderer.RenderTest("Outlet", "EPSON TM-T82", mm, new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Local));

        isi.Should().Contain("UJI CETAK PERANGKAT");
        isi.Should().Contain("EPSON TM-T82");
        isi.Should().Contain($"{mm} mm");
        isi.Split('\n').Select(b => b.TrimEnd('\r')).Should().OnlyContain(b => b.Length <= ReceiptRenderer.Kolom(mm));
    }

    [Fact]
    public void RenderTest_ContohTransaksiKonsisten()
    {
        // Uang disimpan sebagai unit minor: 2 x Rp 15.000 = 3.000.000 minor, bukan 30.000.
        // Struk uji pernah mencetak "Rp 300" di samping "2 x Rp 15.000" dan tidak ada tes
        // yang memeriksa baris contoh ini, jadi kesalahan itu lolos sampai kertas.
        var isi = ReceiptRenderer.RenderTest("Outlet", "EPSON TM-T82", 80,
            new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Local));

        var baris = isi.Split('\n').Select(b => b.TrimEnd('\r')).Single(b => b.StartsWith("2 x Rp 15.000", StringComparison.Ordinal));
        baris.Should().EndWith("Rp 30.000");
        isi.Should().NotContain("Rp 300");
    }
}
