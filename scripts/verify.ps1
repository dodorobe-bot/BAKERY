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
    ... -LaporanJson C:\tmp\laporan.json   (bawaan: dist\verify\verify-<stempel>.json)

  Laporan JSON memuat commit, status pohon kotor, opsi run, dan status setiap langkah,
  sehingga hasil "lulus" bisa ditelusuri setelah konsolnya hilang.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [switch]$SkipTests,
    [switch]$SkipVerify,
    [string]$LaporanJson = ""
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$laporan = New-Object System.Collections.ArrayList

$mulai = Get-Date
$ringkasTes = $null
$logTes = ''
if (-not $LaporanJson) {
    $LaporanJson = Join-Path $root ("dist\verify\verify-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".json")
}
$LaporanJson = [IO.Path]::GetFullPath($LaporanJson)
$repoPenuh = [IO.Path]::GetFullPath($root).TrimEnd('\', '/')
$tempPenuh = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
if (-not ($LaporanJson.StartsWith($repoPenuh + '\', [StringComparison]::OrdinalIgnoreCase) -or
          $LaporanJson.StartsWith($tempPenuh + '\', [StringComparison]::OrdinalIgnoreCase))) {
    Write-Host "LaporanJson '$LaporanJson' ditolak: laporan hanya boleh ditulis di dalam repo atau folder temp."
    exit 1
}

function Simpan-Laporan([int]$kode) {
    try {
        $commit = ''
        try { $commit = ((& git -C $root rev-parse HEAD) -join '').Trim() } catch { }
        $kotor = 0
        try { $kotor = @(& git -C $root status --porcelain).Count } catch { }
        $isi = [pscustomobject]@{
            skema       = 1
            alat        = 'verify.ps1'
            waktu       = (Get-Date).ToString('o')
            durasiDetik = [math]::Round(((Get-Date) - $mulai).TotalSeconds, 1)
            commit      = $commit
            pohonKotor  = $kotor
            konfigurasi = $Configuration
            opsi        = [pscustomobject]@{
                skipTests  = [bool]$SkipTests
                skipVerify = [bool]$SkipVerify
            }
            hasil       = $(if ($kode -eq 0) { 'LULUS' } else { 'GAGAL' })
            kodeKeluar  = $kode
            tes         = $ringkasTes
            logTes      = $logTes
            langkah     = @($laporan)
        }
        $folder = Split-Path -Parent $LaporanJson
        if ($folder) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }
        [IO.File]::WriteAllText($LaporanJson,
            (($isi | ConvertTo-Json -Depth 6) + "`n"),
            (New-Object Text.UTF8Encoding($false)))
        Write-Host "Laporan pagar: $LaporanJson"
    } catch {
        Write-Host "PERINGATAN: laporan pagar gagal ditulis: $($_.Exception.Message)"
    }
}

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
    Simpan-Laporan 1
    exit 1
}

# ── 2. Tes otomatis ─────────────────────────────────────────────────────────
if (-not $SkipTests) {
    Write-Host '==> dotnet test'
    $logTes = [IO.Path]::ChangeExtension($LaporanJson, '.test.log')
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logTes) | Out-Null
    dotnet test $solusi -c $Configuration -m:1 -nodeReuse:false --nologo 2>&1 |
        Tee-Object -FilePath $logTes
    $testCode = $LASTEXITCODE
    # "exit 0" saja bisa menutupi proyek tes yang tidak berjalan. Ringkasan per proyek
    # dijumlahkan supaya laporan memuat jumlah lulus/gagal yang benar-benar dieksekusi.
    if (Test-Path $logTes) {
        $isiTes = Get-Content $logTes -Raw
        $lulus = 0; $gagalTes = 0; $dilewati = 0; $adaRingkas = $false
        foreach ($m in [regex]::Matches($isiTes,
            '(?m)(?:Passed!|Failed!|Gagal!)\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+)')) {
            $adaRingkas = $true
            $gagalTes += [int]$m.Groups[1].Value
            $lulus += [int]$m.Groups[2].Value
            $dilewati += [int]$m.Groups[3].Value
        }
        if ($adaRingkas) {
            $ringkasTes = [pscustomobject]@{ lulus = $lulus; gagal = $gagalTes; dilewati = $dilewati }
        }
    }
    $detailTes = if ($ringkasTes) {
        "exit $testCode, lulus $($ringkasTes.lulus), gagal $($ringkasTes.gagal), dilewati $($ringkasTes.dilewati)"
    } else { "exit $testCode" }
    Catat 'dotnet test (unit + integrasi + UI)' ($testCode -eq 0) $detailTes
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

# Pagar penerusan argumen: peluncur wajib meneruskan --backup/--restore/--Database:Path
# ke host. Tanpa uji ini cacat "peluncur membuang argumen" (1.2.7) hanya ketahuan dari
# dokumentasi yang salah, bukan dari pagar.
$ujiPeluncur = Join-Path $root 'build\installer\launcher\test-argumen-peluncur.ps1'
if (Test-Path $ujiPeluncur) {
    Write-Host '==> Uji penerusan argumen peluncur'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ujiPeluncur
    $kodePeluncur = $LASTEXITCODE
    Catat 'uji argumen peluncur' ($kodePeluncur -eq 0) "exit $kodePeluncur"
} else {
    Catat 'uji argumen peluncur' $false "tidak menemukan $ujiPeluncur"
}

$wxs = Join-Path $root 'build\installer\wix\Package.wxs'
if (Test-Path $wxs) {
    $isiWxs = Get-Content $wxs -Raw
    Catat 'paket WiX tanpa wscript/.vbs' `
        (($isiWxs -notmatch '(?i)wscript') -and ($isiWxs -notmatch '(?i)\.vbs')) `
        'shortcut MSI juga harus menunjuk peluncur native'
}

# ── 2c. Pagar zona waktu toko ──────────────────────────────────────────────
# Tanggal bisnis (nomor dokumen, rekap harian, tanggal cetak ekspor) tidak boleh diambil dari
# jam mesin atau tanggal UTC. Cacat yang dijaga: pesanan 01:00 WIB bernomor/tanggal kemarin.
$pagarZona = @(
    @{ Nama = 'ekspor memakai jam toko'; File = 'src\Application\Services\ExportService.cs';
       Pola = 'DateTime\.Now'; Ket = 'tanggal cetak dan nama berkas ekspor harus BusinessTime.Now()' },
    @{ Nama = 'rentang ekspor bawaan memakai tanggal toko'; File = 'src\Host.Service\Program.cs';
       Pola = 'DateTime\.Today'; Ket = 'default rentang ekspor harus BusinessTime.Today()' },
    @{ Nama = 'penomoran dokumen memakai tanggal toko'; File = 'src\Application\Services\OrderService.cs';
       Pola = 'NextNumberAsync\([^)]*DateTime\.UtcNow'; Ket = 'nomor pesanan memakai BusinessTime.Now()' },
    @{ Nama = 'penomoran retur memakai tanggal toko'; File = 'src\Application\Services\SalesReturnService.cs';
       Pola = 'NextNumberAsync\([^)]*DateTime\.UtcNow'; Ket = 'nomor retur memakai BusinessTime.Now()' },
    @{ Nama = 'nomor SPK/PO memakai tanggal toko'; File = 'src\Host.Service\Program.cs';
       Pola = 'NextNumberAsync\([^)]*DateTime\.UtcNow'; Ket = 'nomor SPK dan PO memakai BusinessTime.Now()' },
    @{ Nama = 'rekap harian memakai tanggal toko'; File = 'src\Infrastructure.Sqlite\Repositories\SalesOrderRepository.cs';
       Pola = 'substr\(o\.CreatedAt, 1, 10\)'; Ket = 'rekap per hari tidak boleh memotong tanggal UTC' }
)
foreach ($p in $pagarZona) {
    $jalur = Join-Path $root $p.File
    if (-not (Test-Path $jalur)) {
        Catat $p.Nama $false "berkas tidak ada: $jalur"
        continue
    }
    $isi = Get-Content $jalur -Raw
    Catat $p.Nama ($isi -notmatch $p.Pola) $p.Ket
}

# ── 2d. Pagar ketahanan sambungan ──────────────────────────────────────────
# Cacat yang dijaga: banner "Server lokal tidak menjawab" muncul berulang ketika soket keep-alive
# lama dipakai lagi atau saat host baru hidup. Perbaikannya tiga lapis (batas keep-alive Kestrel,
# probe /health yang membedakan server mati dari gangguan sekejap, dan layar yang menunggu sendiri)
# sehingga satu lapis yang hilang tidak boleh lolos tanpa ketahuan.
$pagarSambung = @(
    @{ Nama = 'Kestrel memakai keep-alive panjang'; File = 'src\Host.Service\Program.cs';
       Pola = 'KeepAliveTimeout'; Ket = 'tanpa batas ini peramban memakai soket yang sudah ditutup server' },
    @{ Nama = 'klien membedakan server mati'; File = 'frontend\src\services\api.js';
       Pola = 'ServerTidakTerhubung[\s\S]*serverHidup'; Ket = 'kegagalan jaringan harus diprobes ke /health' },
    @{ Nama = 'layar menunggu server hidup lagi'; File = 'frontend\src\App.jsx';
       Pola = 'api\.serverHidup\(\)'; Ket = 'banner server mati harus memulihkan diri tanpa klik' }
)
foreach ($p in $pagarSambung) {
    $jalur = Join-Path $root $p.File
    if (-not (Test-Path $jalur)) {
        Catat $p.Nama $false "berkas tidak ada: $jalur"
        continue
    }
    $isi = Get-Content $jalur -Raw
    Catat $p.Nama ($isi -match $p.Pola) $p.Ket
}
$asetDesktop = @(Get-ChildItem -Path (Join-Path $root 'desktop\assets') -Filter 'index-*.js' -ErrorAction SilentlyContinue)
if ($asetDesktop.Count -eq 0) {
    Catat 'bundel desktop memuat penantian server' $false 'desktop\assets\index-*.js tidak ada'
} else {
    $gabungAset = ($asetDesktop | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    Catat 'bundel desktop memuat penantian server' ($gabungAset -match 'Menunggu server hidup lagi') `
        'bundel yang disajikan host harus memuat perbaikan, bukan sumber saja (bundel basi pernah lolos)'
}

# ── 2e. Pagar jaringan: loopback, tanpa aturan firewall otomatis ───────────
# Pagar runtime bisa hilang tanpa ketahuan bila hanya diuji sekali. Gerbang ini memastikan
# pemanggilannya masih ada dan alamat dengar bawaan tetap loopback.
$programCs = Join-Path $root 'src\Host.Service\Program.cs'
$isiProgram = if (Test-Path $programCs) { Get-Content $programCs -Raw } else { '' }
Catat 'host memasang pagar loopback' ($isiProgram -match 'NetworkExposureGuard\.PastikanLoopback') `
    'start harus menolak alamat dengar di luar loopback sebelum database dibuat'

$appsettings = Join-Path $root 'src\Host.Service\appsettings.json'
$urlsBawaan = ''
try { $urlsBawaan = [string](Get-Content $appsettings -Raw | ConvertFrom-Json).Urls } catch { }
$hostBawaan = ''
if ($urlsBawaan -match '^[a-z]+://(\[[^\]]+\]|[^:/]+)') { $hostBawaan = $Matches[1].Trim('[', ']') }
Catat 'alamat dengar bawaan loopback' `
    ($hostBawaan -in @('localhost', '127.0.0.1', '::1')) `
    "Urls bawaan: '$urlsBawaan'"

$berkasJaringan = @(Get-ChildItem -Path (Join-Path $root 'scripts') -Filter '*.ps1' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $PSCommandPath })   # jangan mencocokkan teks pagar ini sendiri
foreach ($jalur in @((Join-Path $root 'build\installer\inno\setup.iss'),
                     (Join-Path $root 'build\installer\wix\Package.wxs'))) {
    if (Test-Path $jalur) { $berkasJaringan += Get-Item $jalur }
}
$adaAturanFirewall = @($berkasJaringan | Where-Object {
    (Get-Content $_.FullName -Raw) -match '(?i)(New-NetFirewallRule|netsh\s+advfirewall|firewall\s+add)'
}).Count -gt 0
Catat 'tidak ada aturan firewall otomatis' (-not $adaAturanFirewall) `
    'aplikasi loopback tidak boleh membuka port diam-diam lewat instalasi'

# ── 3. Verifikasi perilaku lewat API pada basis data sementara ──────────────
$verifikasi = @()
if (-not $SkipVerify) {
    $hostDll = Join-Path $root "src\Host.Service\bin\$Configuration\net8.0\Seruni22Bakery.Host.dll"
    if (-not (Test-Path $hostDll)) {
        Catat 'host uji tersedia' $false "tidak menemukan $hostDll"
        Write-Host 'HOST UJI TIDAK ADA - pagar berhenti di sini.'
        Simpan-Laporan 1
        exit 1
    }
    $desktop = Join-Path $root 'desktop'
    if (-not (Test-Path (Join-Path $desktop 'index.html'))) {
        Catat 'bundel frontend tersedia' $false "tidak menemukan $desktop\index.html"
        Write-Host 'BUNDEL FRONTEND TIDAK ADA - pagar berhenti di sini.'
        Simpan-Laporan 1
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
        # Rincian pemeriksaan merah harus ikut ke laporan; sebelumnya hanya ada di konsol.
        $barisMerah = @($total -split "`r?`n" | Where-Object { $_ -match '^GAGAL\s*\|' } |
            Select-Object -First 5)
        if ($barisMerah.Count -gt 0) { $ringkas = "$ringkas; $($barisMerah -join ' ; ')" }
        Catat "verifikasi $skenario" ($kode -eq 0) $(if ($ringkas) { $ringkas } else { "exit $kode" })

        # Folder temp dibersihkan di akhir; bukti kegagalan disalin lebih dulu ke folder laporan.
        if ($kode -ne 0) {
            $folderBukti = Join-Path (Split-Path -Parent $LaporanJson) 'bukti'
            New-Item -ItemType Directory -Force -Path $folderBukti | Out-Null
            $awalan = [IO.Path]::GetFileNameWithoutExtension($LaporanJson)
            foreach ($berkasBukti in @($out, $galatNode, $stdout, $stderr)) {
                if (Test-Path $berkasBukti) {
                    Copy-Item -LiteralPath $berkasBukti -Force -Destination (Join-Path $folderBukti (
                        "$awalan-" + [IO.Path]::GetFileName($berkasBukti)))
                }
            }
            Write-Host "Bukti kegagalan disimpan: $folderBukti"
        }

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
    Simpan-Laporan 1
    exit 1
}
$langkah = @($laporan | ForEach-Object { $_.Nama })
Write-Host "PAGER LULUS: $(($laporan | Measure-Object).Count) langkah lulus ($($langkah -join '; '))."
if ($SkipTests) { Write-Host 'CATATAN: dotnet test DILEWATI, jadi hasil ini tidak membuktikan tes xUnit.' }
if ($SkipVerify) { Write-Host 'CATATAN: verifikasi perilaku DILEWATI, jadi hasil ini tidak bukti jalur API.' }
Simpan-Laporan 0
exit 0
