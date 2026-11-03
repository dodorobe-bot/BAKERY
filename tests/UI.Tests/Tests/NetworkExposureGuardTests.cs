using FluentAssertions;
using Seruni22Bakery.Host;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Pagar ini menggantikan asumsi "firewall akan menutupnya": loopback adalah satu-satunya
/// alamat yang sah kecuali pemilik menyatakan lain secara eksplisit.
/// </summary>
public sealed class NetworkExposureGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://127.0.0.2:5000")]
    [InlineData("http://[::1]:5000")]
    [InlineData("http://127.0.0.1:5000;https://localhost:5001")]
    public void AlamatLoopback_Diterima(string? urls)
    {
        var tindakan = () => NetworkExposureGuard.PastikanLoopback(urls, izinkanLan: false);

        tindakan.Should().NotThrow();
    }

    [Theory]
    [InlineData("http://0.0.0.0:5000")]
    [InlineData("http://[::]:5000")]
    [InlineData("http://192.168.1.10:5000")]
    [InlineData("http://bakery-pc:5000")]
    [InlineData("http://*:5000")]
    [InlineData("http://+:5000")]
    [InlineData("http://:5000")]
    [InlineData("ftp://127.0.0.1:5000")]
    [InlineData("bukan-url")]
    public void AlamatLuarLoopback_Ditolak(string urls)
    {
        var tindakan = () => NetworkExposureGuard.PastikanLoopback(urls, izinkanLan: false);

        tindakan.Should().Throw<InvalidOperationException>(
            "start harus berhenti, bukan membuka API ke jaringan");
    }

    [Fact]
    public void Wildcard_MenyebutAlasanDanCaraMemperbaiki()
    {
        var tindakan = () => NetworkExposureGuard.PastikanLoopback("http://0.0.0.0:5000", izinkanLan: false);

        tindakan.Should().Throw<InvalidOperationException>()
            .WithMessage("*hanya untuk PC ini*")
            .WithMessage("*127.0.0.1:5000*");
    }

    [Fact]
    public void IzinLanEksplisit_MembolehkanWildcard()
    {
        var tindakan = () => NetworkExposureGuard.PastikanLoopback("http://0.0.0.0:5000", izinkanLan: true);

        tindakan.Should().NotThrow("membuka LAN harus keputusan sadar, bukan efek samping");
    }
}