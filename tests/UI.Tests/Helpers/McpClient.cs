using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BakeryDesktop.UI.Tests.Helpers;

/// <summary>
/// Menjalankan host dalam mode MCP stdio dan berbicara JSON-RPC satu baris per pesan.
/// Tes ini sengaja memakai proses kedua pada basis data yang sama dengan host UI: yang diuji
/// adalah transport dan izin yang sebenarnya, bukan tiruan kelas McpToolbox.
/// </summary>
public sealed class McpClient : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _log = new();
    private readonly string _keyPath;
    private int _idTerakhir;

    public McpClient(string databasePath, int? userId)
    {
        var root = TestHost.RepositoryRoot;
        _keyPath = Path.Combine(Path.GetTempPath(), $"mcp-uji-{Guid.NewGuid():N}.key");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add(TestHost.ResolveHostDll(root));
        psi.ArgumentList.Add("--mcp-stdio");
        psi.ArgumentList.Add($"--Database:Path={databasePath}");
        psi.ArgumentList.Add($"--Desktop:StaticFilesPath={Path.Combine(root, "desktop")}");
        psi.ArgumentList.Add($"--Jwt:SigningKeyFile={_keyPath}");
        if (userId is not null) psi.ArgumentList.Add($"--Mcp:UserId={userId}");

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Proses MCP gagal dijalankan.");
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _log.AppendLine(e.Data); };
        _process.BeginErrorReadLine();
    }

    public async Task<JsonDocument> InisialisasiAsync()
    {
        var jawaban = await KirimAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "uji-seruni22", version = "1.0.0" },
        });
        await KirimTanpaJawabanAsync("notifications/initialized");
        return jawaban;
    }

    public Task<JsonDocument> PanggilToolAsync(string nama, object? argumen = null) =>
        KirimAsync("tools/call", new { name = nama, arguments = argumen });

    public async Task<JsonDocument> KirimAsync(string metode, object? parameter)
    {
        var id = ++_idTerakhir;
        var pesan = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = metode, @params = parameter });
        await _process.StandardInput.WriteLineAsync(pesan);
        await _process.StandardInput.FlushAsync();

        var jawaban = JsonDocument.Parse(await BacaBarisAsync());
        var idBalasan = jawaban.RootElement.GetProperty("id").GetInt32();
        if (idBalasan != id)
        {
            jawaban.Dispose();
            throw new InvalidOperationException($"Id balasan MCP {idBalasan} tidak cocok dengan permintaan {id}.");
        }
        return jawaban;
    }

    public async Task KirimTanpaJawabanAsync(string metode)
    {
        var pesan = JsonSerializer.Serialize(new { jsonrpc = "2.0", method = metode });
        await _process.StandardInput.WriteLineAsync(pesan);
        await _process.StandardInput.FlushAsync();
    }

    /// <summary>Teks isi tool MCP: hasil sukses maupun penolakan dibungkus di content[0].text.</summary>
    public static string Teks(JsonDocument jawaban) =>
        jawaban.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;

    public static bool Gagal(JsonDocument jawaban) =>
        jawaban.RootElement.GetProperty("result").GetProperty("isError").GetBoolean();

    private async Task<string> BacaBarisAsync()
    {
        var baris = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
        if (baris is null)
        {
            var kode = _process.HasExited ? _process.ExitCode : (int?)null;
            throw new InvalidOperationException(
                $"MCP menutup stdout (exit {kode?.ToString() ?? "masih hidup"}). Stderr:{Environment.NewLine}{_log}");
        }
        return baris;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(5000)) _process.Kill(entireProcessTree: true);
            }
        }
        catch { /* proses sudah berhenti sendiri */ }
        finally
        {
            _process.Dispose();
            try { if (File.Exists(_keyPath)) File.Delete(_keyPath); } catch { /* berkas sementara */ }
        }
    }
}
