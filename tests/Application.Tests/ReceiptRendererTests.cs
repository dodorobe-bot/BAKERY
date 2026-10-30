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
}
