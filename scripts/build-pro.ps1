<#
  Build-pro: satu pintu masuk untuk membangun dan memvalidasi paket Seruni 22 Bakery.

  Sebelumnya build adalah dua langkah manual (build-installer lalu build-msi) dan
  sejumlah pemeriksaan penting dikerjakan tangan: membandingkan isi MSI dengan folder
  publish, membaca ProductCode lewat decompile, mencatat versi alat, dan memastikan
  paket yang sudah terbit tidak tertimpa. Skrip ini menjalankan semuanya berurutan,
  berhenti pada kegagalan pertama, dan menulis laporan JSON yang bisa diperiksa ulang.

  Pagar penting:
    - versi diambil dari <Version> csproj; parameter -Version hanya boleh sama
    - setup.iss dan Package.wxs harus sudah memakai versi yang sama (sumber naik serentak)
    - paket yang sudah terbit (tag v<versi> atau berkas di folder keluaran) tidak boleh
      ditimpa tanpa -IzinkanTulisUlang; build ulang versi yang sama menghasilkan byte
      berbeda karena metadata waktu, jadi hash yang sudah diumumkan akan gugur
    - folder keluaran hanya boleh di dalam repo atau folder temp
    - berkas basis data, kunci, kredensial, dan debug ditolak sebelum packaging
    - payload MSI diverifikasi terhadap manifest publish, bukan diasumsikan benar

  Pemakaian:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-pro.ps1
    ... -SkipTests                       : lewati dotnet test (dicatat sebagai SKIP)
    ... -SkipMsi                         : hanya EXE (validasi MSI dilewati, dicatat)
    ... -OutRoot C:\tmp\build-pro-uji     : build percobaan tanpa menyentuh dist\
    ... -IzinkanTulisUlang               : izinkan menimpa paket versi yang sama
    ... -SinkronkanBundelDesktop         : segarkan desktop\ dari frontend\dist

  Keluar 1 bila ada langkah FAIL. SKIP tidak menggagalkan, tetapi selalu tertulis di
  laporan supaya build yang tidak lengkap tidak terlihat seperti bukti lengkap.
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$OutRoot = "",
    [switch]$SkipTests,
    [switch]$SkipMsi,
    [switch]$SkipVerifikasiPaket,
    [switch]$SinkronkanBundelDesktop,
    [switch]$IzinkanTulisUlang
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Normalisasi-Jalur([string]$jalur) {
    return ([IO.Path]::GetFullPath($jalur)).TrimEnd('\', '/')
}

# Jalur yang lolos pemeriksaan awalan masih bisa keluar dari repo lewat junction/symlink.
# Build menulis dan menghapus banyak berkas, jadi komponen bertanda reparse harus ditolak.
function Tolak-Reparse([string]$jalur, [string]$nama) {
    $kandidat = Normalisasi-Jalur $jalur
    while ($kandidat) {
        if (Test-Path -LiteralPath $kandidat) {
            $item = Get-Item -LiteralPath $kandidat -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "$nama '$jalur' ditolak: komponen '$kandidat' adalah tautan/junction. Pakai folder biasa di dalam repo atau temp."
            }
        }
        $induk = Split-Path -Parent $kandidat
        if (-not $induk -or $induk -eq $kandidat) { break }
        $kandidat = $induk
    }
}

$langkah = New-Object System.Collections.ArrayList

function Catat([string]$nama, [string]$status, [string]$detail) {
    [void]$langkah.Add([pscustomobject]@{ Nama = $nama; Status = $status; Detail = $detail })
    Write-Host ("[{0}] {1} {2}" -f $status, $nama, $detail)
}

function Jalankan-Skrip([string]$skrip, [string[]]$argumen, [string]$logPath) {
    $sebelum = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $skrip @argumen 2>&1 |
            ForEach-Object {
                if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.ToString() } else { $_ }
            } |
            Tee-Object -FilePath $logPath |
            Out-Host
        $kode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $sebelum
    }
    return $kode
}

function Get-VersiAlat {
    $hasil = [ordered]@{}
    $hasil['dotnet'] = ((& dotnet --version) -join '').Trim()
    $hasil['node'] = ((& node --version) -join '').Trim()
    $hasil['npm'] = ((& npm.cmd --version) -join '').Trim()
    $hasil['git'] = ((& git --version) -join '').Trim()
    if (Test-Path $iscc) { $hasil['iscc'] = (Get-Item $iscc).VersionInfo.FileVersion }
    if (Test-Path $wix) { $hasil['wix'] = ((& $wix --version) -join '').Trim() }
    return $hasil
}

function Tulis-Manifest([string]$akar, [string]$tujuan) {
    $akarPenuh = Normalisasi-Jalur $akar
    $baris = New-Object System.Collections.ArrayList
    foreach ($berkas in (Get-ChildItem -Path $akarPenuh -Recurse -File -Force | Sort-Object FullName)) {
        $relatif = $berkas.FullName.Substring($akarPenuh.Length).TrimStart('\')
        $hash = (Get-FileHash -Algorithm SHA256 -Path $berkas.FullName).Hash.ToLowerInvariant()
        [void]$baris.Add("$hash $($berkas.Length) $relatif")
    }
    [IO.File]::WriteAllText($tujuan, (($baris -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
    return $baris.Count
}

function Get-PetaHash([string]$akar) {
    $peta = @{}
    if (-not (Test-Path $akar)) { return $peta }
    $akarPenuh = Normalisasi-Jalur $akar
    foreach ($berkas in (Get-ChildItem -Path $akarPenuh -Recurse -File -Force)) {
        $relatif = $berkas.FullName.Substring($akarPenuh.Length).TrimStart('\')
        $peta[$relatif] = (Get-FileHash -Algorithm SHA256 -Path $berkas.FullName).Hash.ToLowerInvariant()
    }
    return $peta
}

# ── Lokasi dan pagar jalur ─────────────────────────────────────────────────
$repoPenuh = Normalisasi-Jalur $repo
$outRoot = if ($OutRoot) { Normalisasi-Jalur $OutRoot } else { Join-Path $repo 'dist' }
$tempPenuh = Normalisasi-Jalur ([IO.Path]::GetTempPath())
$diBawahRepo = $outRoot.StartsWith($repoPenuh + '\', [StringComparison]::OrdinalIgnoreCase)
$diBawahTemp = $outRoot.StartsWith($tempPenuh + '\', [StringComparison]::OrdinalIgnoreCase)
if ($outRoot -eq $repoPenuh -or (-not $diBawahRepo -and -not $diBawahTemp)) {
    throw "OutRoot '$outRoot' ditolak: harus di dalam repo ($repoPenuh) atau folder temp ($tempPenuh)."
}
Tolak-Reparse $outRoot "OutRoot"

$installerDir = Join-Path $outRoot 'installer'
$publishDir = Join-Path $outRoot 'publish-single'
$logDir = Join-Path $outRoot 'build-logs'
$manifestPath = Join-Path $outRoot 'publish-single.manifest.txt'
$skripInstaller = Join-Path $PSScriptRoot 'build-installer.ps1'
$skripMsi = Join-Path $PSScriptRoot 'build-msi.ps1'
$skripValidasi = Join-Path $PSScriptRoot 'validate-paket.ps1'
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$hostProject = Join-Path $repo 'src\Host.Service\BakeryDesktop.Host.Service.csproj'
$issFile = Join-Path $repo 'build\installer\inno\setup.iss'
$wxsFile = Join-Path $repo 'build\installer\wix\Package.wxs'

New-Item -ItemType Directory -Force -Path $outRoot, $logDir, $installerDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$transcript = Join-Path $logDir "build-pro-$stamp.log"
try { Start-Transcript -Path $transcript -Force | Out-Null } catch { }

$commit = ((& git -C $repo rev-parse HEAD) -join '').Trim()
$kotor = @(& git -C $repo status --porcelain)

# ── Versi: satu sumber, semua salinan harus sepakat ────────────────────────
$versiCsproj = ([xml](Get-Content -Raw $hostProject)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $versiCsproj) { throw "Proyek host tidak memuat <Version>." }
$versiCsproj = $versiCsproj.Trim()
if (-not $Version) { $Version = $versiCsproj }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versi '$Version' bukan bentuk x.y.z." }

$isiIss = Get-Content -Raw $issFile
$isiWxs = Get-Content -Raw $wxsFile
$versiIss = ''
if ($isiIss -match '#define\s+MyAppVersion\s+"([^"]+)"') { $versiIss = $Matches[1] }
$versiWxs = ''
# Package.wxs mendefinisikan versi dengan sintaks preprocessor WiX: <?define ProductVersion = "x.y.z" ?>
if ($isiWxs -match 'define\s+ProductVersion\s*=\s*"([^"]+)"') { $versiWxs = $Matches[1] }
$upgradeCode = ''
if ($isiWxs -match 'UpgradeCode="([^"]+)"') { $upgradeCode = $Matches[1] }

$namaExe = "Seruni22Bakery_Setup_v$Version.exe"
$namaMsi = "Seruni22Bakery_v$($Version)_x64.msi"
$jalurExe = Join-Path $installerDir $namaExe
$jalurMsi = Join-Path $installerDir $namaMsi
$reportJson = Join-Path $installerDir 'VERIFY-REPORT.json'

# Bukti lama di folder keluaran belum boleh disentuh: penjaga versi terbit masih harus
# dievaluasi. Build yang ditolak tidak boleh menghapus atau menimpa laporan paket terbit.
$buildDimulai = $false

function Tulis-Laporan {
    $artefak = @()
    foreach ($jalur in @($jalurExe, $jalurMsi)) {
        if (Test-Path $jalur) {
            $item = Get-Item $jalur
            $artefak += [pscustomobject]@{
                nama        = $item.Name
                ukuran      = $item.Length
                sha256      = (Get-FileHash -Algorithm SHA256 -Path $jalur).Hash.ToLowerInvariant()
                tandaTangan = [string](Get-AuthenticodeSignature -FilePath $jalur).Status
            }
        }
    }
    $verifikasi = $null
    if (Test-Path $reportJson) {
        try { $verifikasi = Get-Content -Path $reportJson -Raw | ConvertFrom-Json } catch { $verifikasi = $null }
    }
    $laporan = [pscustomobject]@{
        skema        = 1
        alat         = 'build-pro.ps1'
        waktu        = (Get-Date).ToString('o')
        versi        = $Version
        commit       = $commit
        pohonKotor   = $kotor.Count
        outRoot      = $outRoot
        alatVersi    = Get-VersiAlat
        opsi         = [pscustomobject]@{
            skipTests              = [bool]$SkipTests
            skipMsi                = [bool]$SkipMsi
            skipVerifikasiPaket    = [bool]$SkipVerifikasiPaket
            sinkronkanBundelDesktop = [bool]$SinkronkanBundelDesktop
            izinkanTulisUlang      = [bool]$IzinkanTulisUlang
        }
        upgradeCode  = $upgradeCode
        artefak      = @($artefak)
        langkah      = @($langkah)
        verifikasi   = $verifikasi
        batasan      = @(
            'payload EXE tidak dibandingkan berkas per berkas; hanya MSI yang diekstrak administratif',
            'pemasangan ke sistem tidak dijalankan oleh skrip ini',
            'tanda tangan hanya dilaporkan statusnya, tidak dibuat di sini'
        )
    }
    [IO.File]::WriteAllText(
        (Join-Path $installerDir 'BUILD-INFO.json'),
        ($laporan | ConvertTo-Json -Depth 8),
        (New-Object Text.UTF8Encoding($false)))
}

try {
    Write-Host "== Build-pro $Version =="
    Write-Host "   repo    : $repo"
    Write-Host "   keluaran: $outRoot"
    Write-Host "   commit  : $commit"
    if ($kotor.Count -gt 0) {
        Catat 'pohon kerja bersih' 'WARN' "$($kotor.Count) berkas belum di-commit; laporan tetap mencatat commit $commit"
    } else {
        Catat 'pohon kerja bersih' 'PASS' "semua perubahan sudah di-commit"
    }

    # ── Preflight ──────────────────────────────────────────────────────────
    $hilang = New-Object System.Collections.ArrayList
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { [void]$hilang.Add('dotnet') }
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) { [void]$hilang.Add('node') }
    if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) { [void]$hilang.Add('npm.cmd') }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { [void]$hilang.Add('git') }
    if (-not (Test-Path $iscc)) { [void]$hilang.Add("ISCC: $iscc") }
    if (-not (Test-Path $csc)) { [void]$hilang.Add("csc: $csc") }
    if (-not $SkipMsi -and -not (Test-Path $wix)) { [void]$hilang.Add("wix: $wix") }
    if ($hilang.Count -gt 0) {
        Catat 'preflight alat' 'FAIL' (@($hilang) -join ', ')
        throw "Alat wajib tidak ditemukan."
    }
    $alatVersi = Get-VersiAlat
    Catat 'preflight alat' 'PASS' (($alatVersi.Keys | ForEach-Object { "$_=$($alatVersi[$_])" }) -join '; ')

    $drive = New-Object IO.DriveInfo([IO.Path]::GetPathRoot($outRoot))
    $sisaGB = [math]::Round($drive.AvailableFreeSpace / 1GB, 1)
    if ($sisaGB -lt 2) {
        Catat 'ruang disk' 'FAIL' "tersisa $sisaGB GB di $($drive.Name); minimal 2 GB"
        throw 'Ruang disk tidak cukup.'
    }
    Catat 'ruang disk' 'PASS' "$sisaGB GB di $($drive.Name)"

    if ($versiCsproj -ne $Version) {
        Catat 'versi selaras' 'FAIL' "parameter $Version != csproj $versiCsproj"
        throw 'Versi parameter tidak sama dengan proyek host.'
    }
    if ($versiIss -ne $Version -or $versiWxs -ne $Version) {
        Catat 'versi selaras' 'FAIL' "iss='$versiIss' wxs='$versiWxs' csproj='$versiCsproj'"
        throw 'setup.iss dan Package.wxs harus memakai versi yang sama; naikkan versi di semua sumber lebih dulu.'
    }
    # WiX menerima GUID dengan atau tanpa kurung kurawal; tabel Property MSI selalu
    # menyimpannya ber-kurawal. Keduanya sah selama bentuknya GUID lengkap.
    if ($upgradeCode -notmatch '^\{?[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}?$') {
        Catat 'versi selaras' 'FAIL' "UpgradeCode '$upgradeCode' tidak berbentuk GUID"
        throw 'UpgradeCode di Package.wxs tidak valid.'
    }
    Catat 'versi selaras' 'PASS' "csproj/iss/wxs = $Version; UpgradeCode $upgradeCode"

    # ── Penjaga versi terbit ───────────────────────────────────────────────
    $tagAda = ((& git -C $repo tag --list "v$Version") -join '').Trim() -ne ''
    $artefakAda = (Test-Path $jalurExe) -or (Test-Path $jalurMsi)
    if ($tagAda -or $artefakAda) {
        $alasan = @()
        if ($tagAda) { $alasan += "tag v$Version sudah ada" }
        if ($artefakAda) { $alasan += "artefak $Version sudah ada di $installerDir" }
        if (-not $IzinkanTulisUlang) {
            Catat 'penjaga versi terbit' 'FAIL' (($alasan -join '; ') + '. Naikkan versi, atau pakai -IzinkanTulisUlang bila ini build percobaan.')
            throw 'Versi yang sudah terbit tidak boleh dibangun ulang tanpa izin eksplisit.'
        }
        Catat 'penjaga versi terbit' 'WARN' (($alasan -join '; ') + '; DITIMPA atas permintaan -IzinkanTulisUlang')
    } else {
        Catat 'penjaga versi terbit' 'PASS' "v$Version belum punya tag maupun artefak di folder keluaran"
    }

    # Mulai dari sini build benar-benar menulis artefak. Laporan lama baru boleh dihapus
    # sekarang: hash-nya menunjuk artefak yang akan diganti, tetapi bila penjaga di atas
    # menolak, bukti paket terbit harus tetap utuh.
    $buildDimulai = $true
    Remove-Item (Join-Path $installerDir 'BUILD-INFO.json') -Force -ErrorAction SilentlyContinue
    Remove-Item $reportJson -Force -ErrorAction SilentlyContinue

    # ── Bangun EXE (tes + frontend + publish + peluncur + Inno) ────────────
    $argInstaller = @('-Version', $Version, '-OutRoot', $outRoot)
    if ($SkipTests) { $argInstaller += '-SkipTests' }
    if ($SinkronkanBundelDesktop) { $argInstaller += '-SinkronkanBundelDesktop' }
    $kodeInstaller = Jalankan-Skrip $skripInstaller $argInstaller (Join-Path $logDir 'build-pro-installer.log')
    if ($kodeInstaller -ne 0) {
        Catat 'build EXE (Inno)' 'FAIL' "build-installer keluar $kodeInstaller"
        throw 'build-installer gagal.'
    }
    Catat 'build EXE (Inno)' 'PASS' $namaExe
    if ($SkipTests) {
        Catat 'tes otomatis' 'SKIP' 'dilewati atas permintaan (-SkipTests); hasil ini bukan bukti tes'
    } else {
        Catat 'tes otomatis' 'PASS' 'dotnet test Release lulus di dalam build-installer'
    }

    if (-not (Test-Path (Join-Path $publishDir 'Seruni22Bakery.Host.exe'))) {
        Catat 'payload publish' 'FAIL' "tidak ada Seruni22Bakery.Host.exe di $publishDir"
        throw 'Payload publish tidak lengkap.'
    }
    $selisihDesktop = @()
    $petaDesktop = Get-PetaHash (Join-Path $repo 'desktop')
    $petaFrontend = Get-PetaHash (Join-Path $repo 'frontend\dist')
    foreach ($k in $petaDesktop.Keys) {
        if (-not $petaFrontend.ContainsKey($k) -or $petaFrontend[$k] -ne $petaDesktop[$k]) { $selisihDesktop += $k }
    }
    foreach ($k in $petaFrontend.Keys) {
        if (-not $petaDesktop.ContainsKey($k)) { $selisihDesktop += $k }
    }
    if ($selisihDesktop.Count -eq 0) {
        Catat 'bundel desktop selaras' 'PASS' 'desktop\ = frontend\dist'
    } else {
        Catat 'bundel desktop selaras' 'FAIL' (($selisihDesktop | Select-Object -First 3) -join ', ')
        throw 'Bundel desktop berbeda dari frontend hasil build.'
    }

    $jumlahPayload = Tulis-Manifest $publishDir $manifestPath
    if ($jumlahPayload -lt 100) {
        Catat 'manifest payload' 'FAIL' "hanya $jumlahPayload berkas; payload tampak tidak lengkap"
        throw 'Manifest payload tidak wajar.'
    }
    Catat 'manifest payload' 'PASS' "$jumlahPayload berkas -> $manifestPath"

    # ── MSI ────────────────────────────────────────────────────────────────
    if ($SkipMsi) {
        Catat 'build MSI (WiX)' 'SKIP' 'dilewati atas permintaan (-SkipMsi)'
    } else {
        $argMsi = @('-Version', $Version, '-OutRoot', $outRoot, '-PublishDir', $publishDir)
        $kodeMsi = Jalankan-Skrip $skripMsi $argMsi (Join-Path $logDir 'build-pro-msi.log')
        if ($kodeMsi -ne 0) {
            Catat 'build MSI (WiX)' 'FAIL' "build-msi keluar $kodeMsi"
            throw 'build-msi gagal.'
        }
        Catat 'build MSI (WiX)' 'PASS' $namaMsi
    }

    # Laporan ditulis lebih dulu agar validator dapat memeriksa bahwa laporan sepakat
    # dengan artefak yang baru dibangun. Laporan ditulis ulang sesudah validasi untuk
    # memuat hasil verifikasi.
    Tulis-Laporan

    # ── Validasi paket oleh skrip terpisah ────────────────────────────────
    if ($SkipVerifikasiPaket) {
        Catat 'validasi paket' 'SKIP' 'dilewati atas permintaan (-SkipVerifikasiPaket); paket TIDAK terbukti valid'
    } else {
        $argValidasi = @('-Version', $Version, '-InstallerDir', $installerDir, '-PublishDir', $publishDir,
            '-ManifestPath', $manifestPath, '-JsonPath', $reportJson)
        if ($SkipMsi) { $argValidasi += '-TanpaMsi' }
        $kodeValidasi = Jalankan-Skrip $skripValidasi $argValidasi (Join-Path $logDir 'build-pro-validasi.log')
        $ringkas = ''
        if (Test-Path $reportJson) {
            try {
                $ver = Get-Content -Path $reportJson -Raw | ConvertFrom-Json
                $ringkas = "pemeriksaan $($ver.jumlahPeriksa), gagal $($ver.gagal), lewati $($ver.lewati)"
                if ($ver.payload) { $ringkas += "; payload $($ver.payload.Terekstrak) berkas" }
            } catch { }
        }
        if ($kodeValidasi -ne 0) {
            Catat 'validasi paket' 'FAIL' $(if ($ringkas) { $ringkas } else { "exit $kodeValidasi" })
            throw 'Validasi paket gagal.'
        }
        Catat 'validasi paket' 'PASS' $ringkas
    }

    Tulis-Laporan
    Catat 'laporan build' 'PASS' (Join-Path $installerDir 'BUILD-INFO.json')
}
catch {
    Catat 'build-pro berhenti' 'FAIL' $_.Exception.Message
    # Penolakan sebelum build tidak boleh menimpa laporan paket yang sedang dilindungi.
    if ($buildDimulai) {
        try { Tulis-Laporan } catch { }
    }
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}

Write-Host ''
Write-Host '================ HASIL BUILD-PRO ================'
$langkah | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$gagal = @($langkah | Where-Object Status -eq 'FAIL')
$lewati = @($langkah | Where-Object Status -eq 'SKIP')
$catatan = @($langkah | Where-Object Status -eq 'WARN')
Write-Host ("Langkah: {0} total, {1} gagal, {2} dilewati, {3} peringatan" -f $langkah.Count, $gagal.Count, $lewati.Count, $catatan.Count)
if ($gagal.Count -gt 0) {
    Write-Host 'BUILD-PRO GAGAL.'
    exit 1
}
if ($lewati.Count -gt 0) {
    Write-Host 'BUILD-PRO SELESAI DENGAN LANGKAH DILEWATI - bukan bukti build lengkap.'
    exit 0
}
Write-Host 'BUILD-PRO LULUS.'
exit 0
