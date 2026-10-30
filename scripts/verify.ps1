<#
  Pagar satu perintah: build, seluruh tes xUnit/Playwright, lalu verifikasi perilaku lewat API.

  Alasan ada: sebelum ini, "build sukses" dan "aplikasi masih benar" adalah dua kebiasaan manual,
  dan skrip verifikasi Batch-2/Batch-3 hidup di luar repositori sehingga tidak bisa dijalankan
  orang lain. Skrip ini menutup keduanya dan tidak pernah menyentuh basis data usaha: setiap
  run memakai basis data sementara di folder temp yang dihapus lagi di akhir.

  Pemakaian:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
    ... -SkipTests        : lewati dotnet test (mis. baru saja jalan)
    ... -SkipVerify       : lewati skrip verifikasi node
    ... -Configuration Release
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [switch]$SkipTests,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$laporan = New-Object System.Collections.ArrayList

function Catat([string]$nama, [bool]$lulus, [string]$detail) {
    [void]$laporan.Add([pscustomobject]@{
        Nama  = $nama
        Status = $(if ($lulus) { 'PASS' } else { 'FAIL' })
        Detail = $detail
    })
    Write-Host ("[{0}] {1} {2}" -f $(if ($lulus) { 'PASS' } else { 'FAIL' }), $nama, $detail)
}

function Get-FreePort {
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    return $port
}

# ── 1. Build ────────────────────────────────────────────────────────────────
$solusi = Join-Path $root 'BakeryDesktop.slnx'
Write-Host "==> Build $solusi ($Configuration)"
# -m:1 -nodeReuse:false: msbuild yang menyimpan node membuat berkas output tergenggam.
dotnet build $solusi -c $Configuration -m:1 -nodeReuse:false -p:UseSharedCompilation=false
$buildCode = $LASTEXITCODE
Catat 'build solusi' ($buildCode -eq 0) "exit $buildCode"
if ($buildCode -ne 0) {
    Write-Host 'BUILD GAGAL - pagar berhenti di sini.'
    exit 1
}

# ── 2. Tes otomatis ─────────────────────────────────────────────────────────
if (-not $SkipTests) {
    Write-Host '==> dotnet test'
    dotnet test $solusi -c $Configuration -m:1 -nodeReuse:false --nologo
    $testCode = $LASTEXITCODE
    Catat 'dotnet test (unit + integrasi + UI)' ($testCode -eq 0) "exit $testCode"
} else {
    Write-Host '==> dotnet test DILEWATI (-SkipTests)'
}

# ── 2b. Pagar paket: peluncur wajib program native ─────────────────────────
# VBScript sudah usang dan tidak tersedia pada sebagian image Windows 11 24H2+/26100.
# Paket yang menunjuk wscript/.vbs tetap lolos build, tetapi tidak bisa dibuka pengguna
# (dibuktikan pada uji mesin bersih 2026-10-28). Karena itu jalur peluncur diperiksa tiap run.
$iss = Join-Path $root 'build\installer\inno\setup.iss'
if (-not (Test-Path $iss)) {
    Catat 'skrip Inno tersedia' $false $iss
} else {
    Catat 'skrip Inno tersedia' $true $iss
    $barisIss = Get-Content $iss
    $isiIss = Get-Content $iss -Raw

    $bagianPeluncur = New-Object System.Collections.ArrayList
    $aktif = $false
    foreach ($b in $barisIss) {
        if ($b -match '^\s*\[([^\]]+)\]') {
            $aktif = ($Matches[1] -in @('Icons', 'Run', 'UninstallRun'))
            continue
        }
        if ($aktif -and $b.Trim() -ne '' -and -not $b.TrimStart().StartsWith(';')) {
            [void]$bagianPeluncur.Add($b)
        }
    }
    $teksPeluncur = $bagianPeluncur -join "`n"

    Catat 'Icons/Run tanpa wscript' ($teksPeluncur -notmatch '(?i)wscript') `
        'shortcut dan [Run] harus langsung ke program, bukan perantara skrip'
    Catat 'Icons/Run tanpa berkas .vbs' ($teksPeluncur -notmatch '(?i)\.vbs') `
        'VBScript tidak boleh menjadi peluncur'
    Catat 'Icons/Run menunjuk Seruni22Bakery.exe' ($teksPeluncur -match '(?i)Seruni22Bakery\.exe') `
        'peluncur native wajib dirujuk'
    Catat 'define MyAppExeName = Seruni22Bakery.exe' `
        ($isiIss -match '(?i)#define\s+MyAppExeName\s+"Seruni22Bakery\.exe"') `
        'nama peluncur di header skrip Inno'
}

$launcherCs = Join-Path $root 'build\installer\launcher\Seruni22BakeryLauncher.cs'
Catat 'sumber peluncur native tersedia' (Test-Path $launcherCs) $launcherCs
$skripPaket = Join-Path $root 'scripts\build-installer.ps1'
if (Test-Path $skripPaket) {
    $isiPaket = Get-Content $skripPaket -Raw
    Catat 'build-installer memakai csc untuk peluncur' `
        ($isiPaket -match '(?i)csc\.exe' -and $isiPaket -match '(?i)Seruni22BakeryLauncher\.cs') `
        'peluncur dikompilasi, bukan disalin sebagai .vbs'
}

$wxs = Join-Path $root 'build\installer\wix\Package.wxs'
if (Test-Path $wxs) {
    $isiWxs = Get-Content $wxs -Raw
    Catat 'paket WiX tanpa wscript/.vbs' `
        (($isiWxs -notmatch '(?i)wscript') -and ($isiWxs -notmatch '(?i)\.vbs')) `
        'shortcut MSI juga harus menunjuk peluncur native'
}

# ── 3. Verifikasi perilaku lewat API pada basis data sementara ──────────────
$verifikasi = @()
if (-not $SkipVerify) {
    $hostDll = Join-Path $root "src\Host.Service\bin\$Configuration\net8.0\Seruni22Bakery.Host.dll"
    if (-not (Test-Path $hostDll)) {
        Catat 'host uji tersedia' $false "tidak menemukan $hostDll"
        Write-Host 'HOST UJI TIDAK ADA - pagar berhenti di sini.'
        exit 1
    }
    $desktop = Join-Path $root 'desktop'
    if (-not (Test-Path (Join-Path $desktop 'index.html'))) {
        Catat 'bundel frontend tersedia' $false "tidak menemukan $desktop\index.html"
        Write-Host 'BUNDEL FRONTEND TIDAK ADA - pagar berhenti di sini.'
        exit 1
    }

    $stamp = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "seruni-verify-$stamp"
    New-Item -ItemType Directory -Path $dir -Force | Out-Null

    # Pagar ini menulis pesanan, bahan, dan jurnal sungguhan. Kalau folder sementara tidak
    # bisa dibuat di luar berkas data usaha, lebih baik berhenti daripada merusak produksi.
    if ($dir -notlike '*\seruni-verify-*') {
        Catat 'basis data sementara aman' $false "folder uji tak terduga: $dir"
        exit 1
    }
    Write-Host "==> Basis data sementara: $dir (data usaha tidak disentuh)"

    foreach ($skenario in @('verify-batch2.mjs', 'verify-batch3.mjs', 'verify-batch6.mjs', 'verify-bug09.mjs')) {
        $skrip = Join-Path $root "tests\Verify\$skenario"
        if (-not (Test-Path $skrip)) {
            Catat "verifikasi $skenario" $false 'skrip tidak ada di tests\Verify'
            continue
        }

        $namaBebas = [IO.Path]::GetFileNameWithoutExtension($skrip) -replace '^verify-', ''
        $db = Join-Path $dir "uji-$namaBebas.db"
        $kunci = Join-Path $dir "uji-$namaBebas.key"
        $stdout = Join-Path $dir "uji-$namaBebas.out"
        $stderr = Join-Path $dir "uji-$namaBebas.err"
        $port = Get-FreePort
        $baseUrl = "http://127.0.0.1:$port"

        Write-Host "==> $skenario pada $baseUrl (db sementara $db)"
        $argumen = @($hostDll,
            "--Database:Path=$db",
            "--Desktop:StaticFilesPath=$desktop",
            "--Jwt:SigningKeyFile=$kunci",
            "--urls=$baseUrl")
        $proses = Start-Process -FilePath 'dotnet' -ArgumentList $argumen -PassThru -NoNewWindow `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr

        $siap = $false
        $batas = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $batas) {
            if ($proses.HasExited) { break }
            try {
                $respon = Invoke-WebRequest -Uri "$baseUrl/health" -UseBasicParsing -TimeoutSec 3
                if ($respon.StatusCode -eq 200) { $siap = $true; break }
            } catch {
                Start-Sleep -Milliseconds 300
            }
        }
        if (-not $siap) {
            $log = ''
            foreach ($f in @($stdout, $stderr)) {
                if (Test-Path $f) { $log += (Get-Content $f -Raw) }
            }
            if ($log.Length -gt 400) { $log = $log.Substring(0, 400) + ' ...' }
            Catat "host uji untuk $skenario" $false "tidak siap dalam 60 detik. Log: $log"
            if (-not $proses.HasExited) { Stop-Process -Id $proses.Id -Force -ErrorAction SilentlyContinue }
            continue
        }
        Catat "host uji untuk $skenario" $true "siap di $baseUrl"

        # Output node disalurkan ke berkas, bukan pipeline: "| 2>&1" di PowerShell 5.1
        # mengubah baris stderr native menjadi error record dan bisa mengaburkan exit code.
        $argumenNode = @($skrip)
        if ($skenario -in @('verify-batch3.mjs', 'verify-batch6.mjs', 'verify-bug09.mjs')) { $argumenNode += $db }
        $env:SERUNI_VERIFY_BASE = $baseUrl
        $prosesNode = Start-Process -FilePath 'node' -ArgumentList $argumenNode -PassThru -NoNewWindow `
            -RedirectStandardOutput (Join-Path $dir "hasil-$namaBebas.log") `
            -RedirectStandardError (Join-Path $dir "galat-$namaBebas.log") -Wait
        $kode = $prosesNode.ExitCode
        $out = Join-Path $dir "hasil-$namaBebas.log"
        $total = if (Test-Path $out) { Get-Content $out -Raw } else { '' }
        Write-Host $total
        $galatNode = Join-Path $dir "galat-$namaBebas.log"
        if (Test-Path $galatNode) {
            $isiGalat = Get-Content $galatNode -Raw
            if (-not [string]::IsNullOrWhiteSpace($isiGalat)) { Write-Host "STDERR: $isiGalat" }
        }
        $ringkas = ''
        if ($total -match 'TOTAL\s+(\d+)\s+pemeriksaan,\s+(\d+)\s+gagal') {
            $ringkas = "pemeriksaan $($Matches[1]), gagal $($Matches[2])"
        }
        Catat "verifikasi $skenario" ($kode -eq 0) $(if ($ringkas) { $ringkas } else { "exit $kode" })

        Stop-Process -Id $proses.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $proses.Id -Timeout 10 -ErrorAction SilentlyContinue
        $env:SERUNI_VERIFY_BASE = ''
    }

    foreach ($file in (Get-ChildItem -Path $dir -Force -ErrorAction SilentlyContinue)) {
        Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $dir -Force -Recurse -ErrorAction SilentlyContinue
    Write-Host "Berkas sementara $dir dibersihkan."
}

# ── 4. Ringkasan ────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '================ HASIL PAGER ================'
$laporan | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$gagal = @($laporan | Where-Object Status -eq 'FAIL')
if ($gagal.Count -gt 0) {
    Write-Host "PAGER GAGAL: $($gagal.Count) langkah tidak lulus."
    exit 1
}
$langkah = @($laporan | ForEach-Object { $_.Nama })
Write-Host "PAGER LULUS: $(($laporan | Measure-Object).Count) langkah lulus ($($langkah -join '; '))."
if ($SkipTests) { Write-Host 'CATATAN: dotnet test DILEWATI, jadi hasil ini tidak membuktikan tes xUnit.' }
if ($SkipVerify) { Write-Host 'CATATAN: verifikasi perilaku DILEWATI, jadi hasil ini tidak bukti jalur API.' }
exit 0
