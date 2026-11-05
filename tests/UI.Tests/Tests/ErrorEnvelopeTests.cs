using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Kesalahan tak terduga harus berakhir sebagai amplop JSON yang bisa ditampilkan layar, bukan
/// halaman HTML atau jejak tumpukan berisi nama tabel dan jalur berkas. Nomor permintaan di
/// amplop itulah yang memungkinkan pengguna melapor tanpa membuka log.
/// </summary>
public sealed class ErrorEnvelopeTests
{
    [Fact]
    public async Task GalatServer_MenjawabAmplopJsonTanpaJejakTumpukan()
    {
        var root = TestHost.RepositoryRoot;
        var hostDll = TestHost.ResolveHostDll(root);
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var dbPath = Path.Combine(Path.GetTempPath(), $"seruni-envelope-{stamp}.db");
        var keyPath = Path.Combine(Path.GetTempPath(), $"seruni-envelope-{stamp}.key");
        var port = FreePort();
        var baseUrl = $"http://127.0.0.1:{port}";

        Process? process = null;
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
            psi.ArgumentList.Add($"--Database:Path={dbPath}");
            psi.ArgumentList.Add($"--Desktop:StaticFilesPath={Path.Combine(root, "desktop")}");
            psi.ArgumentList.Add($"--Jwt:SigningKeyFile={keyPath}");
            psi.ArgumentList.Add($"--urls={baseUrl}");

            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Host uji gagal dijalankan.");
            var log = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await WaitHealthyAsync(baseUrl, process, log);

            // Tabel yang dibaca /api/brand dihapus dari salinan sementara ini saja. Endpoint itu
            // anonim, jadi galat terjadi sebelum otorisasi dan tanpa perlu membuat sesi.
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DROP TABLE AppSettings;";
                await cmd.ExecuteNonQueryAsync();
            }

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var respons = await http.GetAsync("/api/brand");
            var isi = await respons.Content.ReadAsStringAsync();

            respons.StatusCode.Should().Be(HttpStatusCode.InternalServerError, isi);
            respons.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
            isi.Should().NotContain("SqliteException").And.NotContain("no such table")
                .And.NotContain("AppSettings").And.NotContain("   at ");

            using var dok = JsonDocument.Parse(isi);
            var akar = dok.RootElement;
            akar.GetProperty("errorCode").GetString().Should().Be("INTERNAL_ERROR");
            akar.GetProperty("errorMessage").GetString().Should().NotBeNullOrWhiteSpace();
            akar.GetProperty("requestId").GetString().Should().NotBeNullOrWhiteSpace(
                "nomor permintaan dipakai pengguna untuk melapor dan dicocokkan ke log server");
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch { /* proses sudah selesai sendiri */ }
                process.Dispose();
            }
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                TryDelete(dbPath + suffix);
            TryDelete(keyPath);
        }
    }

    private static async Task WaitHealthyAsync(string baseUrl, Process process, System.Text.StringBuilder log)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"Host uji berhenti lebih dulu (exit {process.ExitCode}). Log:{Environment.NewLine}{log}");
            try
            {
                using var respons = await http.GetAsync("/health");
                if (respons.IsSuccessStatusCode) return;
            }
            catch { /* host belum mendengarkan */ }
            await Task.Delay(250);
        }
        throw new InvalidOperationException($"Host uji tidak siap di {baseUrl}. Log:{Environment.NewLine}{log}");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* berkas masih tergenggam: Windows membersihkan temp sendiri */ }
    }
}
