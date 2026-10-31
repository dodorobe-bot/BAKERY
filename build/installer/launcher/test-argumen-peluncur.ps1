<#
  Uji regresi peluncur: perintah pemeliharaan host wajib diteruskan utuh.

  Cacat yang dikunci: peluncur 1.2.7 hanya membaca --tanpa-browser lalu menyalakan
  Seruni22Bakery.Host.exe tanpa argumen. Akibatnya `Seruni22Bakery.exe --restore <berkas>`
  hanya membuka aplikasi dan data TIDAK dipulihkan. Uji ini mengompilasi peluncur dari
  sumbernya, memasang host tiruan yang mencatat argumen, lalu memeriksa dua hal:
    1. --backup/--restore/--Database:Path sampai ke host persis seperti diketik,
       termasuk path berspasi dan tanda kutip;
    2. kode keluar host diteruskan ke pemanggil (0, 1, 2, 3).

  Kontrol negatif: sumber peluncur dari commit ff65f67 (sebelum perbaikan) dijalankan
  dengan kasus yang sama dan HARUS merah, supaya uji ini terbukti bisa gagal.

  Pemakaian:
    powershell -NoProfile -ExecutionPolicy Bypass -File build\installer\launcher\test-argumen-peluncur.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$launcherCs = Join-Path $PSScriptRoot 'Seruni22BakeryLauncher.cs'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

$laporan = New-Object System.Collections.ArrayList
function Catat([string]$nama, [bool]$lulus, [string]$detail) {
    [void]$laporan.Add([pscustomobject]@{ Nama = $nama; Status = $(if ($lulus) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("[{0}] {1} {2}" -f $(if ($lulus) { 'PASS' } else { 'FAIL' }), $nama, $detail)
}

if (-not (Test-Path $csc)) { throw "csc.exe .NET Framework tidak ditemukan: $csc" }
if (-not (Test-Path $launcherCs)) { throw "sumber peluncur tidak ditemukan: $launcherCs" }

$dir = Join-Path $env:TEMP ("seruni-peluncur-uji-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $dir -Force | Out-Null

$stubCs = Join-Path $dir 'StubHost.cs'
$stubSumber = @'
using System;
using System.IO;
using System.Text;

internal static class StubHost
{
    private static int Main(string[] args)
    {
        var tujuan = Environment.GetEnvironmentVariable("SERUNI_STUB_OUTPUT");
        var teks = new StringBuilder();
        teks.AppendLine("jumlah=" + args.Length);
        for (var i = 0; i < args.Length; i++) teks.AppendLine(i + "=" + args[i]);
        File.WriteAllText(tujuan, teks.ToString(), new UTF8Encoding(false));
        Console.WriteLine("stub-host-selesai");
        Console.Error.WriteLine("stub-host-galat");
        int kode;
        return int.TryParse(Environment.GetEnvironmentVariable("SERUNI_STUB_EXIT"), out kode) ? kode : 0;
    }
}
'@
Set-Content -Path $stubCs -Value $stubSumber -Encoding UTF8

$stubExe = Join-Path $dir 'Seruni22Bakery.Host.exe'
& $csc -nologo -target:exe "-out:$stubExe" $stubCs
if (-not (Test-Path $stubExe)) { throw "host tiruan gagal dikompilasi" }

$peluncurExe = Join-Path $dir 'Seruni22Bakery.exe'
& $csc -nologo -target:winexe -optimize+ "-out:$peluncurExe" $launcherCs
if (-not (Test-Path $peluncurExe)) { throw "peluncur gagal dikompilasi" }

function Jalankan-Peluncur([string]$exe, [string]$argumen, [int]$kodeHarap, [string]$keluaranHarap, [string]$galatHarap) {
    $catatan = Join-Path $dir ("catatan-" + [Guid]::NewGuid().ToString('N').Substring(0, 6) + ".txt")
    $out = Join-Path $dir 'peluncur.out'
    $err = Join-Path $dir 'peluncur.err'
    Remove-Item $out, $err -Force -ErrorAction SilentlyContinue

    $env:SERUNI_STUB_OUTPUT = $catatan
    $env:SERUNI_STUB_EXIT = "$kodeHarap"
    $env:SERUNI_LAUNCH_QUIET = '1'
    $env:SERUNI_LAUNCH_NO_BROWSER = '1'
    try {
        $proses = Start-Process -FilePath $exe -ArgumentList $argumen -PassThru -Wait `
            -RedirectStandardOutput $out -RedirectStandardError $err
        $kode = $proses.ExitCode
    }
    finally {
        $env:SERUNI_STUB_OUTPUT = ''
        $env:SERUNI_STUB_EXIT = ''
        $env:SERUNI_LAUNCH_QUIET = ''
        $env:SERUNI_LAUNCH_NO_BROWSER = ''
    }

    $isi = if (Test-Path $catatan) { Get-Content $catatan -Raw } else { '' }
    $stdout = if (Test-Path $out) { Get-Content $out -Raw } else { '' }
    $stderr = if (Test-Path $err) { Get-Content $err -Raw } else { '' }

    $lulus = $true
    $alasan = @()
    if ($kode -ne $kodeHarap) { $lulus = $false; $alasan += "kode keluar $kode, seharusnya $kodeHarap" }
    if ($keluaranHarap -ne '' -and $isi -notmatch [regex]::Escape($keluaranHarap)) {
        $lulus = $false; $alasan += "argumen tidak sampai: harap '$keluaranHarap' di catatan host"
    }
    if ($galatHarap -ne '') {
        if ($stdout -notmatch [regex]::Escape($galatHarap)) {
            $lulus = $false; $alasan += "keluaran host tidak tampil di stdout peluncur"
        }
    }
    return [pscustomobject]@{ Lulus = $lulus; Alasan = ($alasan -join '; '); Catatan = ($isi -replace "`r?`n", ' | '); Kode = $kode }
}

# ── Kasus positif: argumen utuh dan kode keluar diteruskan ─────────────────
$kasus = @(
    @{ Nama = 'restore path berspasi'; Argumen = '--restore "D:\Cadangan Seruni\auto 2026-10-30.db"'; Kode = 0;
       Harap = "1=D:\Cadangan Seruni\auto 2026-10-30.db"; Galat = '' },
    @{ Nama = 'backup + Database:Path berspasi'; Argumen = '--backup "--Database:Path=C:\tmp\seruni dev\uji.db"'; Kode = 0;
       Harap = '1=--Database:Path=C:\tmp\seruni dev\uji.db'; Galat = '' },
    @{ Nama = 'restore tanpa path (kode 2)'; Argumen = '--restore'; Kode = 2;
       Harap = 'jumlah=1'; Galat = 'stub-host-galat' },
    @{ Nama = 'aplikasi masih berjalan (kode 3)'; Argumen = '--restore "C:\tmp\a.db"'; Kode = 3;
       Harap = '1=C:\tmp\a.db'; Galat = 'stub-host-galat' }
)

foreach ($k in $kasus) {
    $hasil = Jalankan-Peluncur $peluncurExe $k.Argumen $k.Kode $k.Harap $k.Galat
    $detail = if ($hasil.Lulus) { "kode $($hasil.Kode) diteruskan; argumen utuh" } else { $hasil.Alasan }
    Catat ("peluncur kini: " + $k.Nama) $hasil.Lulus $detail
}

# ── Kontrol negatif: peluncur lama wajib merah ──────────────────────────────
$komitLama = 'ff65f67'
$adaGit = Test-Path (Join-Path $repo '.git')
if (-not $adaGit) {
    Catat 'kontrol negatif peluncur lama' $false 'repositori git tidak ada; kontrol negatif tidak bisa dijalankan'
} else {
    $sumberLama = Join-Path $dir 'Seruni22BakeryLauncher-lama.cs'
    $teksLama = & git -C $repo show "${komitLama}:build/installer/launcher/Seruni22BakeryLauncher.cs" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($teksLama -join ''))) {
        Catat 'kontrol negatif peluncur lama' $false "sumber peluncur pada commit $komitLama tidak terbaca"
    } else {
        Set-Content -Path $sumberLama -Value ($teksLama -join "`n") -Encoding UTF8
        $exeLama = Join-Path $dir 'Seruni22Bakery-lama.exe'
        & $csc -nologo -target:winexe -optimize+ "-out:$exeLama" $sumberLama
        if (-not (Test-Path $exeLama)) {
            Catat 'kontrol negatif peluncur lama' $false 'peluncur lama gagal dikompilasi'
        } else {
            # Tidak ada penerusan: host tiruan dipanggil tanpa argumen dan peluncur
            # berakhir sebagai "gagal start" (kode 1), jadi hasil HARUS merah.
            $hasilLama = Jalankan-Peluncur $exeLama '--restore "D:\Cadangan Seruni\auto 2026-10-30.db"' 0 `
                '1=D:\Cadangan Seruni\auto 2026-10-30.db' ''
            Catat 'kontrol negatif: peluncur lama membuang argumen (harus FAIL)' (-not $hasilLama.Lulus) `
                ("kode $($hasilLama.Kode); catatan host: " + $hasilLama.Catatan.Trim())
        }
    }
}

Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
$gagal = @($laporan | Where-Object Status -eq 'FAIL')
$ringkas = "TOTAL {0} pemeriksaan, {1} gagal" -f ($laporan | Measure-Object).Count, $gagal.Count
Write-Host $ringkas
if ($gagal.Count -gt 0) { exit 1 }
exit 0
