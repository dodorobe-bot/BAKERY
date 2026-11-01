using System.Net;
using System.Net.Sockets;
using Seruni22Bakery.Application.Devices;

namespace Seruni22Bakery.Host.Devices;

/// <summary>
/// Jalur ESC/POS langsung: susun byte di server, kirim ke port 9100 printer jaringan, tutup.
/// Tidak memakai driver Windows, jadi tetap bisa dipakai pada printer yang hanya punya driver
/// "Generic/Text Only" maupun pada PC tanpa driver vendor.
///
/// Batas yang dijaga: alamat harus loopback/privat (EscPosAlamat), hanya satu sambungan per
/// pekerjaan, balasan printer tidak pernah dibaca, dan isi struk tidak pernah dicetak ke log.
/// </summary>
public sealed class EscPosTcpPrinter : IReceiptPrinter
{
    public static readonly TimeSpan BatasSambungkan = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan BatasTulis = TimeSpan.FromSeconds(15);

    public bool Tersedia => true;
    public string NamaAdapter => "escpos-raw";

    public async Task CetakAsync(PrintTarget target, string isi, CancellationToken ct)
    {
        var galat = EscPosAlamat.ValidasiHostLengkap(target.Host, target.Port);
        if (galat != null) throw new InvalidOperationException(galat);

        // Disusun dan diukur sebelum ada socket dibuka: isi yang terlalu panjang tidak boleh
        // sampai terkirim separuh ke printer.
        var hasil = EscPosEncoder.Encode(isi, target.PotongKertas);

        Exception? terakhir = null;
        foreach (var alamat in await PilihAlamatAsync(target.Host, ct))
        {
            using var klien = new TcpClient(alamat.AddressFamily) { NoDelay = true };
            try
            {
                await SambungkanAsync(klien, alamat, target, ct);
            }
            catch (Exception ex)
            {
                // Baru sambungan yang gagal, jadi data belum menyentuh printer: kandidat alamat
                // berikutnya boleh dicoba. Setelah sambungan jadi, tidak ada percobaan ulang di
                // sini — mengirim dua kali berisiko mencetak struk rangkap.
                terakhir = ex;
                continue;
            }

            await TulisAsync(klien, hasil.Byte, target, ct);
            return;
        }

        throw terakhir == null
            ? new InvalidOperationException($"Printer {target.Host}:{target.Port} tidak bisa dihubungi.")
            : GalatSambungan(terakhir, target);
    }

    /// <summary>
    /// Kegagalan sambung diterjemahkan ke bahasa toko. Teks mentah <see cref="SocketException"/>
    /// berasal dari sistem operasi dan berbahasa Inggris, sedangkan pesan ini dibaca kasir di layar
    /// dan tersimpan permanen di riwayat pekerjaan cetak.
    /// </summary>
    private static Exception GalatSambungan(Exception ex, PrintTarget target) =>
        ex switch
        {
            SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.AccessDenied } =>
                new InvalidOperationException(
                    $"Printer {target.Host}:{target.Port} menolak sambungan. Pastikan printer menyala dan " +
                    $"port {target.Port} benar."),
            SocketException { SocketErrorCode: var kode } =>
                new InvalidOperationException(
                    $"Printer {target.Host}:{target.Port} tidak bisa dihubungi ({kode})."),
            _ => ex
        };

    private static async Task SambungkanAsync(TcpClient klien, IPAddress alamat, PrintTarget target, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(BatasSambungkan);
        try
        {
            await klien.ConnectAsync(alamat, target.Port, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Printer {target.Host}:{target.Port} tidak menerima sambungan dalam " +
                $"{BatasSambungkan.TotalSeconds:0} detik. Periksa kabel, status menyala, dan alamat IP-nya.");
        }
    }

    private static async Task TulisAsync(TcpClient klien, byte[] isi, PrintTarget target, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(BatasTulis);
        try
        {
            var stream = klien.GetStream();
            await stream.WriteAsync(isi, cts.Token);
            await stream.FlushAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Printer {target.Host}:{target.Port} berhenti menerima data sebelum struk selesai dikirim.");
        }
    }

    /// <summary>
    /// Nama host diperiksa ulang lewat DNS di sini: penyimpanan pengaturan tidak boleh bergantung
    /// pada DNS yang sedang mati, dan nama yang menunjuk ke alamat publik harus tetap gagal saat
    /// mencetak, bukan saat disimpan. Semua kandidat yang sah dicoba berurutan supaya printer
    /// dengan catatan IPv4 dan IPv6 tetap tercetak pada keluarga alamat yang benar-benar menjawab.
    /// </summary>
    private static async Task<IReadOnlyList<IPAddress>> PilihAlamatAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var angka))
        {
            if (!EscPosAlamat.Boleh(angka))
                throw new InvalidOperationException($"Alamat '{host}' berada di luar jaringan privat.");
            return [angka];
        }

        IPAddress[] calon;
        try
        {
            calon = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException)
        {
            // Nama host yang tidak ter-resolution menghasilkan SocketException berbahasa Inggris;
            // kasir hanya perlu tahu bahwa alamatnya yang harus diganti.
            throw new InvalidOperationException(
                $"Nama '{host}' tidak dikenali jaringan saat mencetak. Isi struk memuat data pelanggan, " +
                "jadi gunakan alamat IP printer bila nama ini tidak bisa di-resolution.");
        }

        var diterima = calon.Where(EscPosAlamat.Boleh).ToList();
        if (diterima.Count == 0)
            throw new InvalidOperationException(
                $"Hostname '{host}' tidak menunjuk ke alamat loopback atau jaringan privat, " +
                "jadi tidak dicetak. Isi struk memuat data pelanggan.");
        return diterima;
    }
}
