using FluentAssertions;
using Seruni22Bakery.Application.Devices;

namespace BakeryDesktop.Application.Tests;

/// <summary>
/// Encoder ini satu-satunya tempat urutan byte ESC/POS dibentuk, jadi byte-nya diuji sebagai
/// angka, bukan sebagai teks: printer kasir tidak peduli pada penjelasan, ia hanya melihat byte.
/// </summary>
public class EscPosEncoderTests
{
    private static readonly byte[] Inisialisasi = [0x1B, 0x40];
    private static readonly byte[] Umpan = [0x1B, 0x64, 0x03];
    private static readonly byte[] Potong = [0x1D, 0x56, 0x00];

    [Fact]
    public void BukaSelaluDenganInisialisasiDanTutupDenganUmpan()
    {
        var hasil = EscPosEncoder.Encode("Halo", potong: false);

        hasil.Byte.Take(2).Should().Equal(Inisialisasi);
        hasil.Byte.Skip(hasil.Byte.Length - Umpan.Length).Should().Equal(Umpan);
    }

    [Fact]
    public void TeksMasukByteDemiByteDanTiapBarisDiAkhiriLF()
    {
        var hasil = EscPosEncoder.Encode("AB\r\nC", potong: false);

        hasil.Byte.Skip(2).Should().Equal(new byte[] { 0x41, 0x42, 0x0A, 0x43, 0x0A, 0x1B, 0x64, 0x03 },
            "CRLF dan LF harus menghasilkan urutan byte yang sama persis");
        hasil.Baris.Should().Be(2);
        hasil.Ganti.Should().Be(0);
    }

    [Fact]
    public void BarisKosongTetapMenjadiSatuLF()
    {
        var hasil = EscPosEncoder.Encode("A\n\nB", potong: false);

        hasil.Byte.Skip(2).Should().Equal(new byte[] { 0x41, 0x0A, 0x0A, 0x42, 0x0A, 0x1B, 0x64, 0x03 });
        hasil.Baris.Should().Be(3);
    }

    [Fact]
    public void IsiTigaBarisSesuaiStrukPanjangBaku()
    {
        var hasil = EscPosEncoder.Encode("SERUNI 22\nTotal Rp 20.000\nTerima kasih", potong: true);

        var harapan = new List<byte>(Inisialisasi);
        foreach (var baris in new[] { "SERUNI 22", "Total Rp 20.000", "Terima kasih" })
        {
            harapan.AddRange(System.Text.Encoding.ASCII.GetBytes(baris));
            harapan.Add(0x0A);
        }
        harapan.AddRange(Umpan);
        harapan.AddRange(Potong);

        hasil.Byte.Should().Equal(harapan);
    }

    [Theory]
    [InlineData("é", 1)]
    [InlineData("••", 2)]
    [InlineData("Rp 1.000", 0)]
    [InlineData("\t", 1)]
    public void KarakterDiLuarAsciiCetakDigantiTanyaDanDihitung(string isi, int jumlahGanti)
    {
        var hasil = EscPosEncoder.Encode(isi, potong: false);

        hasil.Ganti.Should().Be(jumlahGanti);
        hasil.Byte.Should().NotContain((byte)0x00, "byte nol bukan karakter yang bisa dicetak");
        foreach (var b in hasil.Byte.Skip(2).Take(hasil.Byte.Length - 2 - Umpan.Length))
            (b == 0x0A || (b >= 0x20 && b <= 0x7E)).Should().BeTrue(
                $"0x{b:X2} berada di luar isi teks yang diizinkan");
    }

    [Fact]
    public void TanpaPotongTidakAdaByteGsSamaSekali()
    {
        var hasil = EscPosEncoder.Encode("A", potong: false);

        hasil.Byte.Should().NotContain((byte)0x1D);
        hasil.Byte.Should().NotContain((byte)0x56);
    }

    [Fact]
    public void PotongMenambahTigaByteTerakhir()
    {
        var hasil = EscPosEncoder.Encode("A", potong: true);

        hasil.Byte[^3..].Should().Equal(Potong);
    }

    [Fact]
    public void IsiKosongTetapPaketSahUntukPrinter()
    {
        var hasil = EscPosEncoder.Encode("", potong: false);

        hasil.Byte.Should().Equal(Inisialisasi.Concat(new byte[] { 0x0A }).Concat(Umpan));
        hasil.Baris.Should().Be(1);
    }

    [Fact]
    public void IsiKosongEksplicitTetapPaketSah()
    {
        var hasil = EscPosEncoder.Encode(null, potong: false);

        hasil.Byte.Should().Equal(EscPosEncoder.Encode("", potong: false).Byte);
    }

    [Fact]
    public void IsiDiAtasBatasEnamPuluhEmpatKb_DitolakDenganJumlahByte()
    {
        var terlalu = string.Join("\n", Enumerable.Repeat(new string('A', 200), 340));

        var act = () => EscPosEncoder.Encode(terlalu, potong: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*65536*");
    }

    [Fact]
    public void IsiTepatDiBatasMasihDiterima()
    {
        // 65536 - 2 (init) - 3 (umpan) - 1 (LF) = 65530 karakter ASCII pada satu baris.
        var pas = new string('A', EscPosEncoder.MaksByte - 6);

        var hasil = EscPosEncoder.Encode(pas, potong: false);

        hasil.Byte.Length.Should().Be(EscPosEncoder.MaksByte);
    }

    [Fact]
    public void StrukLengkapDelapanPuluhKolomMuatJauhDiBatasBatasByte()
    {
        var baris = string.Join("\n", Enumerable.Repeat(new string('-', 48), 40));

        var hasil = EscPosEncoder.Encode(baris, potong: true);

        hasil.Byte.Length.Should().BeLessThan(EscPosEncoder.MaksByte / 4);
    }

    [Fact]
    public void PerintahYangSengajaTidakDikirimTidakMunculDiManapun()
    {
        var hasil = EscPosEncoder.Encode("Total Rp 20.000\nTerima kasih", potong: true);

        Ada(hasil.Byte, [0x1B, 0x61]).Should().BeFalse("ESC a perataan: ReceiptRenderer sudah mengatur spasi");
        Ada(hasil.Byte, [0x1B, 0x45]).Should().BeFalse("ESC E tebal: huruf tebal melebar di kertas 58 mm");
        Ada(hasil.Byte, [0x1B, 0x74]).Should().BeFalse("ESC t code page: berbeda antar vendor");
        Ada(hasil.Byte, [0x1B, 0x70]).Should().BeFalse("ESC p laci uang: harus aksi sadar, bukan efek samping cetak");
    }

    private static bool Ada(byte[] data, byte[] urutan)
    {
        for (var i = 0; i <= data.Length - urutan.Length; i++)
        {
            var cocok = true;
            for (var j = 0; j < urutan.Length; j++)
                if (data[i + j] != urutan[j]) { cocok = false; break; }
            if (cocok) return true;
        }
        return false;
    }
}
