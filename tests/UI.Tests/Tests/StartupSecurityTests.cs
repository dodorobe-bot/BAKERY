using System.Diagnostics;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Kunci HS256 yang pendek bisa dipecah offline dari satu token yang bocor. Start harus berhenti
/// dengan alasan yang terbaca, bukan diam-diam menandatangani token dengan kunci lemah.
/// </summary>
public sealed class StartupSecurityTests
{
    [Fact]
    public async Task Host_MenolakJwtSecretKurangDari32Byte()
    {
        var hostDll = ResolveHostDll(TestHost.RepositoryRoot);
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var dbPath = Path.Combine(Path.GetTempPath(), $"seruni-jwt-{stamp}.db");
        var keyPath = Path.Combine(Path.GetTempPath(), $"seruni-jwt-{stamp}.key");

        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(hostDll);
            psi.ArgumentList.Add("--Jwt:Secret=abc");
            psi.ArgumentList.Add($"--Database:Path={dbPath}");
            psi.ArgumentList.Add($"--Jwt:SigningKeyFile={keyPath}");
            psi.ArgumentList.Add("--urls=http://127.0.0.1:0");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Host uji gagal dijalankan.");
            var keluaran = process.StandardOutput.ReadToEndAsync();
            var galat = process.StandardError.ReadToEndAsync();
            using var batas = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await process.WaitForExitAsync(batas.Token);
            var log = (await keluaran) + (await galat);

            process.ExitCode.Should().NotBe(0, "start harus gagal, bukan berjalan dengan kunci lemah");
            log.Should().Contain("minimal 32 byte",
                $"pesan log harus menyebut syaratnya. Log:{Environment.NewLine}{log}");
            File.Exists(dbPath).Should().BeFalse("pemeriksaan kunci terjadi sebelum basis data dibuat");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                TryDelete(dbPath + suffix);
            TryDelete(keyPath);
        }
    }

    /// <summary>
    /// Salah ketik alamat dengar tidak boleh menjadi "API terbuka di LAN". Pagar harus
    /// berhenti sebelum basis data dan kunci JWT dibuat, supaya kegagalan konfigurasi
    /// tidak meninggalkan berkas setengah jadi.
    /// </summary>
    [Fact]
    public async Task Host_MenolakAlamatLuarLoopbackSebelumMembuatBasisData()
    {
        var hostDll = ResolveHostDll(TestHost.RepositoryRoot);
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var dbPath = Path.Combine(Path.GetTempPath(), $"seruni-lan-{stamp}.db");
        var keyPath = Path.Combine(Path.GetTempPath(), $"seruni-lan-{stamp}.key");

        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(hostDll);
            // Dua sumber alamat: --urls (loopback, lolos) dan Kestrel:Endpoints (wildcard,
            // harus ditolak). Start harus berhenti sebelum Kestrel mengikat port apa pun.
            psi.ArgumentList.Add("--urls=http://127.0.0.1:0");
            psi.ArgumentList.Add("--Kestrel:Endpoints:Http:Url=http://0.0.0.0:0");
            psi.ArgumentList.Add($"--Database:Path={dbPath}");
            psi.ArgumentList.Add($"--Jwt:SigningKeyFile={keyPath}");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Host uji gagal dijalankan.");
            var keluaran = process.StandardOutput.ReadToEndAsync();
            var galat = process.StandardError.ReadToEndAsync();
            using var batas = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await process.WaitForExitAsync(batas.Token);
            var log = (await keluaran) + (await galat);

            process.ExitCode.Should().NotBe(0, "bind ke 0.0.0.0 harus menggagalkan start");
            log.Should().Contain("hanya untuk PC ini",
                $"log harus menjelaskan penolakan. Log:{Environment.NewLine}{log}");
            File.Exists(dbPath).Should().BeFalse("pagar berjalan sebelum basis data dibuat");
            File.Exists(keyPath).Should().BeFalse("pagar berjalan sebelum kunci JWT dibuat");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                TryDelete(dbPath + suffix);
            TryDelete(keyPath);
        }
    }

    private static string ResolveHostDll(string root)
    {
        // Sama dengan TestHost: uji memakai konfigurasi build yang sama, bukan selalu Debug.
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
            $"Seruni22Bakery.Host.dll belum ada. Jalankan: dotnet build BakeryDesktop.slnx (dari {root})");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* berkas masih tergenggam: Windows membersihkan temp sendiri */ }
    }
}
