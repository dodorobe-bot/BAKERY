using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BakeryDesktop.UI.Tests.Helpers;

/// <summary>
/// Host + basis data sementara milik tes UI.
///
/// Alasan ada: tes ini benar-benar menulis pesanan, bahan, dan jurnal. Kalau host dijalankan
/// tangan dengan basis data pemakaian, satu kali "dotnet test" mengubah data usaha asli.
/// Fixture ini membangun DB sementara hasil migrasi, kunci JWT sementara, dan port bebas,
/// lalu menghapus berkasnya saat proses tes selesai, sehingga tidak ada lagi alasan menunjuk
/// tes ke data produksi.
///
/// BAKERY_TEST_URL tetap dihormati: kalau diisi, tes memakai host milik pengguna dan fixture
/// ini tidak berjalan sama sekali.
/// </summary>
public static class TestHost
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly StringBuilder Log = new();
    private static Process? _process;
    private static string? _baseUrl;
    private static string? _dbPath;
    private static string? _keyPath;
    private static bool _cleanupRegistered;

    /// <summary>Starts the throwaway host once per test run and returns its base URL.</summary>
    public static async Task<string> StartAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_baseUrl is not null && _process is not null && !_process.HasExited) return _baseUrl;
            if (_baseUrl is not null) HapusSisa();   // host lama sudah mati: mulai dari DB bersih lagi

            var root = FindRepositoryRoot();
            var hostDll = ResolveHostDll(root);
            var stamp = Guid.NewGuid().ToString("N")[..8];
            _dbPath = Path.Combine(Path.GetTempPath(), $"seruni-ui-{stamp}.db");
            _keyPath = Path.Combine(Path.GetTempPath(), $"seruni-ui-{stamp}.key");

            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(hostDll);
            psi.ArgumentList.Add($"--Database:Path={_dbPath}");
            psi.ArgumentList.Add($"--Desktop:StaticFilesPath={Path.Combine(root, "desktop")}");
            psi.ArgumentList.Add($"--Jwt:SigningKeyFile={_keyPath}");
            psi.ArgumentList.Add($"--urls=http://127.0.0.1:{FreePort()}");

            _process = Process.Start(psi)
                ?? throw new InvalidOperationException("Host uji gagal dijalankan.");
            // Log harus tetap diserap: pipe yang penuh membuat host menunggu dan tes menggantung.
            _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Log.AppendLine(e.Data); };
            _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log.AppendLine(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _baseUrl = $"http://127.0.0.1:{ExtractPort(psi.ArgumentList[^1])}";
            if (!_cleanupRegistered)
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
                _cleanupRegistered = true;
            }

            await WaitUntilHealthyAsync(_baseUrl);
            return _baseUrl;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>True when the URL belongs to a database this fixture created and may safely mutate.</summary>
    public static bool Manages(string baseUrl) =>
        string.Equals(_baseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lokasi basis data sementara milik host ini. Dipakai tes MCP yang menjalankan proses kedua
    /// pada basis data yang sama; null bila tes memakai host milik pengguna.
    /// </summary>
    public static string? DatabasePath => _dbPath;

    public static void Stop()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        catch { /* proses sudah selesai sendiri */ }
        finally
        {
            if (_process is not null)
            {
                _process.Dispose();
                _process = null;
            }
            HapusSisa();
        }
    }

    /// <summary>Buang berkas DB/kunci dari host yang sudah mati sebelum host baru dipakai.</summary>
    private static void HapusSisa()
    {
        Hapus(_dbPath);
        Hapus(_keyPath);
        _dbPath = null;
        _keyPath = null;
    }

    private static void Hapus(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(candidate)) File.Delete(candidate); }
            catch { /* berkas masih tergenggam: Windows membersihkan temp sendiri */ }
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BakeryDesktop.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException(
                "BakeryDesktop.slnx tidak ditemukan di atas direktori tes; jalankan dari dalam repositori.");
    }

    /// <summary>Akar repositori, dipakai tes yang memeriksa berkas yang benar-benar dikirim ke pengguna.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string ResolveHostDll(string root)
    {
        // Ambil host dari konfigurasi yang sama dengan majelis tes. Dulu urutannya selalu Debug
        // lebih dulu, sehingga "dotnet test -c Release" diam-diam menguji host Debug yang
        // tertinggal dari build lama — hasil hijau/merahnya bukan tentang kode yang diuji.
        var konfigurasiTes = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        var urutan = konfigurasiTes is "Debug" or "Release"
            ? new[] { konfigurasiTes, konfigurasiTes == "Debug" ? "Release" : "Debug" }
            : new[] { "Debug", "Release" };
        foreach (var configuration in urutan)
        {
            var candidate = Path.Combine(root, "src", "Host.Service", "bin", configuration,
                "net8.0", "Seruni22Bakery.Host.dll");
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException(
            $"Seruni22Bakery.Host.dll belum ada. Jalankan: dotnet build BakeryDesktop.slnx -c {urutan[0]} (dari {root})");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int ExtractPort(string urlsArgument) =>
        int.Parse(urlsArgument[(urlsArgument.LastIndexOf(':') + 1)..]);

    private static async Task WaitUntilHealthyAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
                throw new InvalidOperationException(
                    $"Host uji berhenti lebih dulu (exit {_process.ExitCode}). Log:{Environment.NewLine}{Log}");
            try
            {
                var response = await http.GetAsync("/health");
                if (response.IsSuccessStatusCode) return;
            }
            catch { /* host belum mendengarkan */ }
            await Task.Delay(250);
        }
        throw new InvalidOperationException($"Host uji tidak siap di {baseUrl}. Log:{Environment.NewLine}{Log}");
    }
}
