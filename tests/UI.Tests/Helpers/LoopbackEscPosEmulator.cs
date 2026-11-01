using System.Net;
using System.Net.Sockets;

namespace BakeryDesktop.UI.Tests.Helpers;

/// <summary>
/// Pengganti printer ESC/POS jaringan: TCP listener di 127.0.0.1 yang menampung byte apa adanya.
/// Dipakai supaya klaim "printer kasir ESC/POS bisa dipakai" bisa dibuktikan byte-per-byte tanpa
/// perangkat keras, tanpa hak admin, dan tanpa mengunduh emulator pihak ketiga.
///
/// Emulator ini sengaja tidak menafsirkan perintah: yang dicatat adalah byte yang benar-benar
/// diterima socket, sama seperti yang akan dilihat kepala cetak printer sungguhan.
/// </summary>
public sealed class LoopbackEscPosEmulator : IAsyncDisposable
{
    private readonly TcpListener _pendengar;
    private readonly CancellationTokenSource _berhenti = new();
    private readonly Task _pekerja;
    private readonly List<byte> _diterima = [];
    private readonly object _kunci = new();
    private int _sambungan;

    private LoopbackEscPosEmulator(TcpListener pendengar, bool abaikanData)
    {
        _pendengar = pendengar;
        AbaikanData = abaikanData;
        Port = ((IPEndPoint)pendengar.LocalEndpoint).Port;
        _pekerja = Task.Run(JagaAsync);
    }

    /// <summary>Port terpilih Windows; angka ini dipakai sebagai device.escpos.port saat uji.</summary>
    public int Port { get; }

    public string Host => "127.0.0.1";

    /// <summary>Sambungan diterima sejak emulator menyala, termasuk yang tidak dibaca.</summary>
    public int Sambungan
    {
        get { lock (_kunci) return _sambungan; }
    }

    /// <summary>Semua byte dari semua sambungan, berurutan, tanpa diubah.</summary>
    public byte[] Diterima
    {
        get { lock (_kunci) return _diterima.ToArray(); }
    }

    private bool AbaikanData { get; }

    /// <summary>Printer hidup: terima sambungan dan simpan byte yang masuk.</summary>
    public static LoopbackEscPosEmulator Mulai() => Mulai(abaikanData: false);

    /// <summary>
    /// Printer macet: sambungan diterima tetapi datanya tidak pernah dibaca, meniru alat yang
    /// menyala namun tidak menghabiskan antreannya.
    /// </summary>
    public static LoopbackEscPosEmulator MulaiMacet() => Mulai(abaikanData: true);

    private static LoopbackEscPosEmulator Mulai(bool abaikanData)
    {
        // Port 0 = Windows yang memilih angka bebas, jadi tidak menabrak printer asli di 9100.
        var pendengar = new TcpListener(IPAddress.Loopback, 0);
        pendengar.Start();
        return new LoopbackEscPosEmulator(pendengar, abaikanData);
    }

    /// <summary>Tunggu sampai jumlah byte tertentu tiba; mengembalikan apa yang sudah ada saat batas habis.</summary>
    public async Task<byte[]> TungguByteAsync(int jumlah, TimeSpan batas)
    {
        var akhir = DateTime.UtcNow + batas;
        while (DateTime.UtcNow < akhir)
        {
            if (Diterima.Length >= jumlah) return Diterima;
            await Task.Delay(20);
        }
        return Diterima;
    }

    private async Task JagaAsync()
    {
        try
        {
            while (!_berhenti.IsCancellationRequested)
            {
                var koneksi = await _pendengar.AcceptTcpClientAsync(_berhenti.Token);
                lock (_kunci) _sambungan++;
                if (AbaikanData)
                {
                    koneksi.Dispose();
                    continue;
                }
                // Kepemilikan socket berpindah ke pembaca; tanpa ini koneksi ditutup sebelum datanya tiba.
                _ = Task.Run(() => BacaAsync(koneksi));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task BacaAsync(TcpClient koneksi)
    {
        using (koneksi)
        {
            try
            {
                var stream = koneksi.GetStream();
                var penampung = new byte[8192];
                int dibaca;
                while ((dibaca = await stream.ReadAsync(penampung, _berhenti.Token)) > 0)
                {
                    lock (_kunci)
                        for (var i = 0; i < dibaca; i++)
                            _diterima.Add(penampung[i]);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _berhenti.Cancel();
        _pendengar.Stop();
        try { await _pekerja; } catch { /* penutupan tidak boleh menyamarkan hasil tes */ }
        _berhenti.Dispose();
    }
}
