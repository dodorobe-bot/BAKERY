using System.Net;
using System.Net.Sockets;

namespace Seruni22Bakery.Application.Devices;

/// <summary>
/// Penjaga alamat printer jaringan. Isi struk memuat nama dan telepon pelanggan, jadi alamat
/// yang bisa dicapai dari luar jaringan toko tidak boleh diterima tanpa sengaja: satu kunci
/// pengaturan yang salah tidak boleh berubah menjadi kiriman data pelanggan ke internet.
///
/// Host yang bukan angka diperiksa saat menyimpan (sintaks saja) dan diperiksa lagi terhadap
/// hasil DNS saat mencetak (supaya nama yang menunjuk ke alamat publik tetap ditolak).
/// </summary>
public static class EscPosAlamat
{
    public const int MaksPanjangHost = 105;
    public const int PortMin = 1;
    public const int PortMaks = 65_535;

    /// <summary>Null bila diterima, pesan galat bila ditolak.</summary>
    public static string? ValidasiHostLengkap(string host, int port)
    {
        var galatHost = ValidasiHost(host);
        if (galatHost != null) return galatHost;
        if (port is < PortMin or > PortMaks)
            return $"Port ESC/POS harus {PortMin}-{PortMaks}, bukan {port}.";
        return null;
    }

    public static string? ValidasiHost(string? host)
    {
        var h = (host ?? string.Empty).Trim();
        if (h.Length == 0)
            return "Alamat printer ESC/POS belum diisi. Contoh: 192.168.1.50 atau printer-toko.local.";
        if (h.Length > MaksPanjangHost)
            return "Alamat printer ESC/POS terlalu panjang.";
        if (h.Contains("://", StringComparison.Ordinal) || h.Contains('/') || h.Contains('\\') || h.Contains('@') || h.Contains(' '))
            return "Alamat printer ESC/POS harus berupa hostname atau angka IP saja, tanpa skema, jalur, atau spasi.";

        // Tidak ada huruf berarti yang ditulis adalah angka IP. Bentuk sebagian tidak diterima:
        // .NET dengan senang hati membaca "192.168.1" sebagai 192.168.0.1, dan alamat yang
        // ditafsirkan diam-diam tidak boleh mencetak struk berisi data pelanggan.
        if (!AdaHuruf(h) && !h.Contains(':'))
        {
            var bagian = h.Split('.');
            var bentukSah = bagian.Length == 4
                            && bagian.All(b => b.Length is >= 1 and <= 3 && b.All(char.IsAsciiDigit));
            if (!bentukSah)
                return $"Angka IP '{h}' harus empat bagian 0-255, contoh 192.168.1.50.";
            if (!IPAddress.TryParse(h, out var ipv4))
                return $"Angka IP '{h}' di luar 0-255.";
            if (!Boleh(ipv4))
                return $"Hanya alamat loopback atau jaringan privat yang diterima; '{h}' berada di luar itu.";
            return null;
        }

        if (IPAddress.TryParse(h, out var angka))
        {
            if (!Boleh(addr: angka))
                return $"Hanya alamat loopback atau jaringan privat yang diterima; '{h}' berada di luar itu.";
            return null;
        }

        // Label hostname: huruf, angka, titik, tanda hubung; tidak diawali/diakhiri titik.
        if (h.StartsWith('.') || h.EndsWith('.') || h.Contains("..", StringComparison.Ordinal))
            return $"Hostname '{h}' tidak sah.";
        foreach (var c in h)
            if (!(char.IsAsciiLetter(c) || char.IsAsciiDigit(c) || c == '.' || c == '-'))
                return $"Hostname '{h}' memuat karakter yang tidak diizinkan.";
        return null;
    }

    private static bool AdaHuruf(string h)
    {
        foreach (var c in h)
            if (char.IsAsciiLetter(c)) return true;
        return false;
    }

    /// <summary>Loopback, RFC1918, link-local, IPv6 loopback/ULA/link-local. Alamat lain ditolak.</summary>
    public static bool Boleh(IPAddress addr)
    {
        if (addr.AddressFamily == AddressFamily.InterNetworkV6 && addr.IsIPv4MappedToIPv6)
            addr = addr.MapToIPv4();

        if (IPAddress.IsLoopback(addr)) return true;
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = addr.GetAddressBytes();
            return b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        }

        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = addr.GetAddressBytes();
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return true;   // fe80::/10 link-local
            if ((b[0] & 0xFE) == 0xFC) return true;                   // fc00::/7 unique-local
        }
        return false;
    }
}
