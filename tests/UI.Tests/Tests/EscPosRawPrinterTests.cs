using System.Collections.Generic;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Seruni22Bakery.Application.Devices;
using Seruni22Bakery.Host.Devices;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Bukti byte-per-byte jalur ESC/POS langsung. Semua ditulis terhadap emulator loopback, jadi
/// yang diuji adalah urutan byte yang benar-benar tiba di socket - sama seperti yang akan
/// dilihat kepala cetak printer kasir sungguhan. Tidak ada perangkat keras, tidak ada hak admin,
/// dan tidak ada unduhan pihak ketiga yang dipakai sebagai dalih "sudah didukung".
///
/// Yang masih menunggu perangkat nyata: apakah tiap merek printer menerima GS V untuk memotong,
/// dan seberapa cepat alat sungguhan menghabiskan antreannya.
/// </summary>
public sealed class EscPosRawPrinterTests
{
    private static readonly TimeSpan BatasTunggu = TimeSpan.FromSeconds(5);
    private const string Struk = "SERUNI 22 BAKERY\nJl. Melati 22 Bandung\n\nORD-2026-000011\n2 x Rp 10.000\nTotal Rp 20.000\n";

    private static PrintTarget Tujuan(LoopbackEscPosEmulator em, bool potong = false) =>
        new(DeviceSettings.ModeEscPosRaw, "", em.Host, em.Port, 80, potong);

    private static PrintTarget Tujuan(string host, int port, bool potong = false) =>
        new(DeviceSettings.ModeEscPosRaw, "", host, port, 80, potong);

    [Fact]
    public async Task ByteYangTibaPersisSamaDenganHasilEncoder()
    {
        await using var em = LoopbackEscPosEmulator.Mulai();
        var harapan = EscPosEncoder.Encode(Struk, potong: false);

        await new EscPosTcpPrinter().CetakAsync(Tujuan(em), Struk, CancellationToken.None);
        var tiba = await em.TungguByteAsync(harapan.Byte.Length, BatasTunggu);

        tiba.Should().Equal((IEnumerable<byte>)harapan.Byte, "socket printer harus menerima byte yang sama, tidak kurang dan tidak lebih");
        tiba.Should().StartWith(new byte[] { 0x1B, 0x40 }, "ESC @ pembuka harus menjadi byte pertama");
        em.Sambungan.Should().Be(1, "satu pekerjaan cetak memakai satu sambungan");
    }

    [Fact]
    public async Task PotongMenambahTigaByteGsVDiEkor()
    {
        await using var em = LoopbackEscPosEmulator.Mulai();
        var tanpa = EscPosEncoder.Encode(Struk, potong: false);
        var dengan = EscPosEncoder.Encode(Struk, potong: true);

        await new EscPosTcpPrinter().CetakAsync(Tujuan(em, potong: true), Struk, CancellationToken.None);
        var tiba = await em.TungguByteAsync(dengan.Byte.Length, BatasTunggu);

        tiba.Should().Equal((IEnumerable<byte>)dengan.Byte);
        tiba.Length.Should().Be(tanpa.Byte.Length + 3);
        tiba[^3..].Should().Equal(new byte[] { 0x1D, 0x56, 0x00 });
    }

    [Fact]
    public async Task TanpaPotong_SamaSekaliTidakAdaPerintahPemotong()
    {
        await using var em = LoopbackEscPosEmulator.Mulai();
        var harapan = EscPosEncoder.Encode(Struk, potong: false);

        await new EscPosTcpPrinter().CetakAsync(Tujuan(em), Struk, CancellationToken.None);
        var tiba = await em.TungguByteAsync(harapan.Byte.Length, BatasTunggu);

        tiba.Should().NotContain((byte)0x1D, "GS adalah awalan perintah pemotong/barcode; tanpa permintaan potong tidak boleh muncul");
    }

    [Fact]
    public void PerataanTebalCodePageDanLaciTetapTidakPernahDikirim()
    {
        // Deretan ini sengaja tidak diproduksi encoder: perataan akan menumpuk di atas spasi
        // ReceiptRenderer, huruf tebal melebar pada kertas 58 mm, code page berbeda antar vendor,
        // dan laci uang harus terbuka sebagai aksi sadar, bukan efek samping mencetak.
        var hasil = EscPosEncoder.Encode(Struk, potong: true);
        var urutanTerlarang = new[]
        {
            new byte[] { 0x1B, 0x61 }, // ESC a perataan
            new byte[] { 0x1B, 0x45 }, // ESC E tebal
            new byte[] { 0x1B, 0x74 }, // ESC t code page
            new byte[] { 0x1B, 0x70 }, // ESC p laci uang
        };

        foreach (var perintah in urutanTerlarang)
            Mengandung(hasil.Byte, perintah).Should().BeFalse(
                $"{Convert.ToHexString(perintah)} tidak boleh muncul pada struk biasa");
    }

    [Fact]
    public async Task SambunganDitolak_PesanBahasaIndonesiaBukanTeksSistem()
    {
        // Port 1 tidak pernah mendengarkan di loopback: ini jalur printer mati/kabel lepas.
        var galat = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new EscPosTcpPrinter().CetakAsync(Tujuan("127.0.0.1", 1), Struk, CancellationToken.None));

        galat.Message.Should().Contain("127.0.0.1:1");
        galat.Message.Should().Contain("menolak sambungan");
        // Pesan ini tersimpan permanen di riwayat cetak dan dibaca kasir, bukan hanya di log.
        galat.Message.Should().NotContain("refused",
            "teks sistem berbahasa Inggris tidak boleh jadi satu-satunya penjelasan untuk kasir");
    }

    [Fact]
    public async Task NamaHostTidakTerResolution_PesanMenyuruhPakaiAlamatIp()
    {
        // .invalid dijamin tidak pernah.resolve (RFC 2606), jadi kegagalan DNS teruji tanpa jaringan.
        var galat = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new EscPosTcpPrinter().CetakAsync(
                Tujuan("printer-toko.invalid", 9100), Struk, CancellationToken.None));

        galat.Message.Should().Contain("printer-toko.invalid");
        galat.Message.Should().Contain("tidak dikenali jaringan");
        galat.Message.Should().NotContain("host is known",
            "kegagalan DNS juga harus dibaca kasir tanpa teks sistem berbahasa Inggris");
    }

    [Fact]
    public async Task IsiMelewatiBatas64Kb_DitolakSebelumSocketDibuka()
    {
        await using var em = LoopbackEscPosEmulator.Mulai();
        var terlaluPanjang = new string('A', 70_000);

        var act = () => new EscPosTcpPrinter().CetakAsync(Tujuan(em), terlaluPanjang, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*65536*");
        em.Sambungan.Should().Be(0, "isi yang terlalu panjang tidak boleh membuka sambungan printer");
        em.Diterima.Should().BeEmpty();
    }

    [Fact]
    public async Task HostPublik_DitolakSebelumAdaSambungan()
    {
        // Alamat publik akan mengirim nama dan telepon pelanggan keluar jaringan toko.
        var act = () => new EscPosTcpPrinter().CetakAsync(
            Tujuan("8.8.8.8", 9100), Struk, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*jaringan privat*");
    }

    [Fact]
    public async Task Hostname_DiperiksaUlangSetelahDnsDanHanyaYangPrivatDipakai()
    {
        await using var em = LoopbackEscPosEmulator.Mulai();
        var harapan = EscPosEncoder.Encode(Struk, potong: false);

        // "localhost" Resolve ke 127.0.0.1 dan/atau ::1: alamat dipilih ulang saat mencetak,
        // bukan dipercaya dari saat penyimpanan.
        await new EscPosTcpPrinter().CetakAsync(Tujuan("localhost", em.Port), Struk, CancellationToken.None);
        var tiba = await em.TungguByteAsync(harapan.Byte.Length, BatasTunggu);

        tiba.Should().Equal((IEnumerable<byte>)harapan.Byte);
    }

    [Fact]
    public async Task ModeDriverWindows_TetapLewatSpoolerDanTidakMenyentuhSocket()
    {
        await using var em = LoopbackEscPosEmulator.Mulai();
        var gabungan = new CompositeReceiptPrinter(new WindowsSpoolerPrinter(), new EscPosTcpPrinter());
        var targetDriver = new PrintTarget(DeviceSettings.ModeDriverWindows, "Printer Tidak Terpasang Uji",
            em.Host, em.Port, 80, false);

        var act = () => gabungan.CetakAsync(targetDriver, Struk, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tidak terpasang*");
        em.Sambungan.Should().Be(0, "mode driver Windows tidak boleh membuka socket ESC/POS");
    }

    [Fact]
    public async Task ModeEscPos_LewatGabunganSamaDenganLewatAdaptorLangsung()
    {
        await using var em = LoopbackEscPosEmulator.Mulai();
        var harapan = EscPosEncoder.Encode(Struk, potong: false);
        var gabungan = new CompositeReceiptPrinter(new WindowsSpoolerPrinter(), new EscPosTcpPrinter());

        await gabungan.CetakAsync(Tujuan(em), Struk, CancellationToken.None);
        var tiba = await em.TungguByteAsync(harapan.Byte.Length, BatasTunggu);

        tiba.Should().Equal((IEnumerable<byte>)harapan.Byte);
        gabungan.NamaAdapter.Should().Be("pilih-per-mode");
    }

    private static bool Mengandung(byte[] data, byte[] urutan)
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
