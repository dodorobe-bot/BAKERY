using System.Diagnostics;
using System.Text;
using BakeryDesktop.UI.Tests.Helpers;
using Xunit.Abstractions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Mencetak buku manual (docs/manual/manual.html) menjadi PDF A4.
///
/// Bukan pagar mutu: hanya berjalan bila diminta lewat SERUNI_PRINT_MANUAL=1. Seluruh alur
/// (dua lintasan daftar isi, lapisan kepala/kaki, penggabungan pypdf, dan pemeriksaan hasil)
/// berada di docs/manual/cetak_manual.py supaya dapat dijalankan juga tanpa test host.
///
/// Chrome dijalankan single-process oleh skrip. Pada mesin ini, pencetakan lewat Playwright
/// PdfAsync berhenti tanpa menghasilkan berkas ketika RAM menipis, karena buku memuat 41
/// tangkapan layar; jalur Chrome langsung dengan satu proses selesai dalam hitungan detik.
/// </summary>
[Collection("UI Tests")]
public sealed class ManualPrintTests
{
    private readonly ITestOutputHelper _output;

    public ManualPrintTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Cetak_PDF_Manual()
    {
        if (Environment.GetEnvironmentVariable("SERUNI_PRINT_MANUAL") != "1") return;

        var akar = TestHost.RepositoryRoot;
        var skrip = Path.Combine(akar, "docs", "manual", "cetak_manual.py");
        var hasil = Path.Combine(akar, "docs", "manual", "Seruni22-Buku-Manual-Pengguna.pdf");
        if (!File.Exists(skrip))
            throw new InvalidOperationException($"Skrip cetak manual tidak ditemukan di {skrip}.");

        var psi = new ProcessStartInfo("python")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = akar,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(skrip);

        using var proses = Process.Start(psi) ?? throw new InvalidOperationException("Python tidak dapat dijalankan.");
        var keluaran = proses.StandardOutput.ReadToEndAsync();
        var galat = proses.StandardError.ReadToEndAsync();

        using var batas = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try
        {
            await proses.WaitForExitAsync(batas.Token);
        }
        catch (OperationCanceledException)
        {
            try { proses.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("Cetak manual melebihi 12 menit dan dihentikan.");
        }

        var stdout = await keluaran;
        var stderr = await galat;
        _output.WriteLine(stdout);
        if (!string.IsNullOrWhiteSpace(stderr)) _output.WriteLine(stderr);

        Assert.Equal(0, proses.ExitCode);

        var berkas = new FileInfo(hasil);
        Assert.True(berkas.Exists && berkas.Length > 1_000_000, "PDF manual tidak terbentuk.");
        _output.WriteLine($"[manual] {berkas.Length / 1024.0 / 1024.0:F1} MB → {hasil}");
    }
}
