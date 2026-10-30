using FluentAssertions;
using Seruni22Bakery.Domain.Common;

namespace BakeryDesktop.Domain.Tests;

/// <summary>
/// Nama, slogan, dan logo toko disimpan di Pengaturan dan dipakai di layar masuk, menu, struk,
/// serta ekspor. Tes ini menjaga dua hal yang tidak boleh longgar: nilai kosong selalu jatuh ke
/// nama bawaan (bukan kotak merek kosong), dan logo yang masuk benar-benar gambar sesuai
/// jenisnya — bukan berkas apa pun yang diberi label PNG.
/// </summary>
public class StoreBrandTests
{
    // PNG 1x1 piksel yang sah: 89 50 4E 47 0D 0A 1A 0A ...
    private const string PngSatuPiksel =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static string JpegSah() => Convert.ToBase64String(
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ]);

    private static string WebpSah() => Convert.ToBase64String(
    [
        .. "RIFF"u8.ToArray(), 0x1A, 0x00, 0x00, 0x00,
        .. "WEBPVP8 "u8.ToArray(), 0x0E, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NamaKosong_JatuhKeNamaBawaan(string? tersimpan) =>
        StoreBrand.Name(tersimpan).Should().Be(StoreBrand.DefaultName);

    [Fact]
    public void NamaTersimpan_DipangkasTanpaMengubahIsi()
    {
        StoreBrand.Name("  Toko Roti Bu Ratna  ").Should().Be("Toko Roti Bu Ratna");
        StoreBrand.Tagline("  Roti hangat setiap hari  ").Should().Be("Roti hangat setiap hari");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void SloganKosong_JatuhKeSloganBawaan(string? tersimpan) =>
        StoreBrand.Tagline(tersimpan).Should().Be(StoreBrand.DefaultTagline);

    [Fact]
    public void NamaSah_Diterima()
    {
        StoreBrand.Validate(StoreBrand.NameKey, "Toko Roti Bu Ratna").Should().BeNull();
        StoreBrand.Validate(StoreBrand.NameKey, new string('A', StoreBrand.MaxNameLength)).Should().BeNull();
    }

    [Fact]
    public void NamaKosong_DitolakKarenaKotakMerekTidakBolehKosong() =>
        StoreBrand.Validate(StoreBrand.NameKey, "   ").Should().Contain("tidak boleh kosong");

    [Fact]
    public void NamaTerlaluPanjang_DitolakDenganBatasJelas() =>
        StoreBrand.Validate(StoreBrand.NameKey, new string('A', StoreBrand.MaxNameLength + 1))
            .Should().Contain($"{StoreBrand.MaxNameLength} karakter");

    [Fact]
    public void KarakterKendali_Ditolak()
    {
        StoreBrand.Validate(StoreBrand.NameKey, "Toko\u0007Roti").Should().Contain("karakter kendali");
        StoreBrand.Validate(StoreBrand.TaglineKey, "Roti\u0000Segar").Should().Contain("karakter kendali");
    }

    [Fact]
    public void SloganBolehKosong() =>
        StoreBrand.Validate(StoreBrand.TaglineKey, "").Should().BeNull("slogan hanya pelengkap");

    [Fact]
    public void SloganTerlaluPanjang_Ditolak() =>
        StoreBrand.Validate(StoreBrand.TaglineKey, new string('B', StoreBrand.MaxTaglineLength + 1))
            .Should().Contain($"{StoreBrand.MaxTaglineLength} karakter");

    [Fact]
    public void KunciLain_TidakDiperiksaDiSini() =>
        StoreBrand.Validate("printer_name", new string('X', 5000)).Should().BeNull();

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public void LogoSah_Diterima(string jenis)
    {
        var isi = jenis switch
        {
            "png" => PngSatuPiksel,
            "jpeg" => JpegSah(),
            _ => WebpSah(),
        };

        StoreBrand.Validate(StoreBrand.LogoKey, $"data:image/{jenis};base64,{isi}").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LogoKosong_BerartiKembaliKeInisial(string nilai) =>
        StoreBrand.Validate(StoreBrand.LogoKey, nilai).Should().BeNull();

    [Fact]
    public void LogoBukanDataUrl_Ditolak() =>
        StoreBrand.Validate(StoreBrand.LogoKey, "https://contoh.id/logo.png")
            .Should().Contain("PNG, JPEG, atau WebP");

    [Fact]
    public void LogoJenisTidakDidukung_Ditolak() =>
        StoreBrand.Validate(StoreBrand.LogoKey, $"data:image/gif;base64,{PngSatuPiksel}")
            .Should().Contain("PNG, JPEG, atau WebP");

    [Fact]
    public void LogoMenyamarSebagaiPng_DitolakKarenaByteAwalTidakCocok() =>
        StoreBrand.Validate(StoreBrand.LogoKey,
                $"data:image/png;base64,{Convert.ToBase64String("bukan gambar"u8.ToArray())}")
            .Should().Contain("tidak cocok");

    [Fact]
    public void LogoBase64Rusak_Ditolak() =>
        StoreBrand.Validate(StoreBrand.LogoKey, "data:image/png;base64,%%%bukanbase64%%%")
            .Should().Contain("PNG, JPEG, atau WebP");

    [Fact]
    public void LogoTerlaluBesar_DitolakDenganBatasKb()
    {
        var panjangTerlaluBesar = (StoreBrand.MaxLogoBytes + 3) / 3 * 4 + 200;
        var nilai = "data:image/png;base64," + new string('A', panjangTerlaluBesar);

        StoreBrand.Validate(StoreBrand.LogoKey, nilai)
            .Should().Contain($"{StoreBrand.MaxLogoBytes / 1024} KB");
    }
}
