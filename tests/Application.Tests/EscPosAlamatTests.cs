using System.Net;
using FluentAssertions;
using Seruni22Bakery.Application.Devices;

namespace BakeryDesktop.Application.Tests;

/// <summary>
/// Isi struk memuat nama dan telepon pelanggan. Penjaga alamat ini yang memastikan satu kunci
/// pengaturan yang salah ketik tidak berubah menjadi kiriman data pelanggan ke luar jaringan toko.
/// </summary>
public class EscPosAlamatTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.50")]
    [InlineData("169.254.1.1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::22")]
    [InlineData("printer-toko.local")]
    [InlineData("POS-22")]
    public void AlamatLoopbackDanPrivatDiterima(string host)
    {
        EscPosAlamat.ValidasiHostLengkap(host, 9100).Should().BeNull(host);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("192.169.1.1")]
    [InlineData("168.254.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("2001:4860:4860::8888")]
    public void AlamatPublikDitolakDenganAlasan(string host)
    {
        var galat = EscPosAlamat.ValidasiHost(host);

        galat.Should().NotBeNull(host);
        galat.Should().Contain("privat");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AlamatKosongDitolakKarenaBelumDiisi(string host)
    {
        EscPosAlamat.ValidasiHost(host).Should().Contain("belum diisi");
    }

    [Theory]
    [InlineData("http://192.168.1.50")]
    [InlineData("https://192.168.1.50/path")]
    [InlineData("192.168.1.50:9100")]
    [InlineData("printer\\sambungan")]
    [InlineData("user@192.168.1.50")]
    [InlineData("192.168 1.50")]
    [InlineData(".192.168.1.50")]
    [InlineData("192.168.1.50.")]
    [InlineData("toko..local")]
    [InlineData("999.1.1.1")]
    [InlineData("192.168.1")]
    [InlineData("printer toko!")]
    public void BentukYangBukanHostnameAtauAngkaIPDitolak(string host)
    {
        EscPosAlamat.ValidasiHost(host).Should().NotBeNull(host);
    }

    [Fact]
    public void HostnamePanjangMelewatiBatasDitolak()
    {
        var host = new string('a', 100) + "." + new string('b', 10);

        EscPosAlamat.ValidasiHost(host).Should().Contain("terlalu panjang");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9100)]
    [InlineData(65535)]
    public void PortDiRentangSahDiterima(int port)
    {
        EscPosAlamat.ValidasiHostLengkap("192.168.1.50", port).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void PortDiLuarRentangDitolak(int port)
    {
        EscPosAlamat.ValidasiHostLengkap("192.168.1.50", port)
            .Should().NotBeNull().And.Contain("Port ESC/POS");
    }

    [Theory]
    [InlineData("::ffff:192.168.1.50", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void IPv4MappedDiDalamIPv6DiperlakukanSepertiAlamatAslinya(string host, bool diharapkanBoleh)
    {
        EscPosAlamat.Boleh(IPAddress.Parse(host)).Should().Be(diharapkanBoleh, host);
    }

    [Fact]
    public void BatasPanjangDanPortDieksporSebagaiKonstantaYangSama()
    {
        // Layar memakai batas yang sama; angka yang berbeda di dua tempat adalah bug menunggu waktu.
        EscPosAlamat.MaksPanjangHost.Should().Be(105);
        EscPosAlamat.PortMin.Should().Be(1);
        EscPosAlamat.PortMaks.Should().Be(65_535);
        DeviceSettings.PortEscPosBawaan.Should().Be(9100);
        DeviceSettings.LebarDidukung.Should().Equal(58, 80);
    }
}
