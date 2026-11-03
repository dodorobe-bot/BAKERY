namespace Seruni22Bakery.Host;

/// <summary>
/// Aplikasi ini dirancang untuk satu PC: peramban dan API hanya lewat loopback.
/// Firewall bukan pengganti pagar aplikasi — satu salah ketik
/// "Urls": "http://0.0.0.0:5000" akan membuka API ke seluruh LAN tanpa aturan
/// firewall yang ditinjau. Start harus berhenti dengan alasan yang terbaca.
/// </summary>
public static class NetworkExposureGuard
{
    public const string IzinLanEnv = "SERUNI_ALLOW_LAN";

    public static void PastikanLoopback(string? urls, bool? izinkanLan = null)
    {
        if (string.IsNullOrWhiteSpace(urls)) return;
        if (izinkanLan ?? Environment.GetEnvironmentVariable(IzinLanEnv) == "1") return;

        foreach (var mentah in urls.Split(';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(mentah, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Alamat dengar '{mentah}' tidak sah. Gunakan http://127.0.0.1:5000 atau http://localhost:5000.");
            }

            if (!IsLoopback(uri.Host.Trim('[', ']')))
            {
                throw new InvalidOperationException(
                    $"Alamat dengar '{mentah}' ditolak: aplikasi ini hanya untuk PC ini. " +
                    "Gunakan http://127.0.0.1:5000 atau http://localhost:5000. " +
                    $"Bila LAN memang disengaja, set {IzinLanEnv}=1 dan tinjau aturan firewall secara manual.");
            }
        }
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip));
}