using FluentAssertions;
using Seruni22Bakery.Domain.Common;

namespace BakeryDesktop.Domain.Tests;

/// <summary>
/// Kasus G7: transaksi pukul 23:59 dan 00:01 harus masuk tanggal yang benar.
///
/// Jam sistem tidak bisa dipindah dari API, jadi batasnya diuji dengan cap waktu tetap pada
/// jalur konversi yang sama dengan Now(): 23:59 WIB masih 16:59 UTC, 00:01 WIB sudah 17:01 UTC
/// di hari UTC yang sama. Kalau zona toko bergeser atau batas hari kembali memakai UTC, tes ini
/// yang lebih dulu menyala merah.
/// </summary>
public class BusinessTimeTests
{
    [Fact]
    public void BatasTengahMalam_MemakaiZonaToko_BukanUtc()
    {
        BusinessTime.KonfigurasikanZona(BusinessTime.ZoneIdBawaan).Should().BeNull();
        BusinessTime.Zone.Id.Should().Be("Asia/Jakarta");

        var pukul2359 = BusinessTime.DariUtc(new DateTime(2026, 11, 5, 16, 59, 0, DateTimeKind.Utc));
        var pukul0001 = BusinessTime.DariUtc(new DateTime(2026, 11, 5, 17, 1, 0, DateTimeKind.Utc));

        pukul2359.ToString("yyyy-MM-dd HH:mm").Should().Be("2026-11-05 23:59");
        pukul0001.ToString("yyyy-MM-dd HH:mm").Should().Be("2026-11-06 00:01");
        pukul2359.Date.Should().Be(new DateTime(2026, 11, 5));
        pukul0001.Date.Should().Be(new DateTime(2026, 11, 6));
    }

    [Fact]
    public void AwalHariToko_DiterjemahkanKeBatasUtcYangTepat()
    {
        BusinessTime.AwalHariUtc(new DateTime(2026, 11, 5)).Should()
            .Be(new DateTime(2026, 11, 4, 17, 0, 0, DateTimeKind.Utc));
        BusinessTime.AwalHariUtc(new DateTime(2026, 11, 6)).Should()
            .Be(new DateTime(2026, 11, 5, 17, 0, 0, DateTimeKind.Utc));
    }
}
