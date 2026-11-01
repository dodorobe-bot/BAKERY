<#
  Uji pagar build-pro dan validate-paket dengan kontrol negatif.

  Alasan ada: validator yang selalu hijau tidak membuktikan apa pun. Skrip ini menjalankan
  validator pada paket yang benar (harus HIJAU), lalu menyalin paket ke folder sementara dan
  merusaknya dengan cara yang menyerupai kesalahan nyata: checksum salah, baris MSI hilang,
  manifest payload tidak cocok, MSI tidak ada, versi tertukar, folder publish memuat
  kunci, laporan build menyebut artefak hantu, dan SHA256SUMS memuat nama ber-`..`. Setiap
  kerusakan harus membuat validator berhenti dengan kode 1 dan menyebut pemeriksaan yang
  tepat. Akhirnya baseline dijalankan lagi untuk membuktikan artefak asli tidak tersentuh.

  Baseline tidak cukup hanya "0 FAIL": laporan JSON juga diperiksa agar payload MSI benar-
  benar dibandingkan (referensi dan hasil ekstraksi > 0). Tanpa itu, folder publish yang
  tidak ada membuat perbandingan payload dilewati dan baseline hijau secara palsu.

  Selain itu skrip ini membuktikan penjaga versi terbit build-pro.ps1 menolak build ulang
  versi yang sudah punya tag tanpa -IzinkanTulisUlang. Penjaga itu berhenti sebelum satu
  perintah build pun dijalankan, jadi uji ini tidak menulis apa pun ke dist\. Bila tag
  v<versi> belum ada (misalnya tepat sesudah naik versi), penjaga memang tidak akan menolak;
  uji itu ditandai SKIP, bukan FAIL, supaya tidak menjalankan build sungguhan.

  Tidak memasang paket, tidak menandatangani, tidak menyentuh basis data pengguna.
  Semua berkas uji berada di folder temp dan dibersihkan di akhir; hanya log yang disimpan.

  Pemakaian:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\uji-build-pro.ps1
    ... -Version 1.2.12            (bawaan: <Version> csproj)
    ... -LewatiUjiBuildPro         (hanya uji validator)

  Keluar 1 bila ada harapan yang tidak terpenuhi.
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$InstallerDir = "",
    [string]$PublishDir = "",
    [string]$LaporanJson = "",
    [switch]$LewatiUjiBuildPro
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Normalisasi-Jalur([string]$jalur) {
    return ([IO.Path]::GetFullPath($jalur)).TrimEnd('\', '/')
}

$validator = Join-Path $PSScriptRoot 'validate-paket.ps1'
$buildPro = Join-Path $PSScriptRoot 'build-pro.ps1'
$hostProject = Join-Path $root 'src\Host.Service\BakeryDesktop.Host.Service.csproj'

if (-not (Test-Path $validator)) { throw "Validator tidak ditemukan: $validator" }
if (-not (Test-Path $buildPro)) { throw "build-pro.ps1 tidak ditemukan: $buildPro" }

if (-not $Version) {
    $versiCsproj = ([xml](Get-Content -Raw $hostProject)).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
    if (-not $versiCsproj) { throw 'Versi tidak diberikan dan proyek host tidak memuat <Version>.' }
    $Version = $versiCsproj.Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versi '$Version' bukan bentuk x.y.z." }

$installerDir = if ($InstallerDir) { Normalisasi-Jalur $InstallerDir } else { Join-Path $root 'dist\installer' }
$publishDir = if ($PublishDir) { Normalisasi-Jalur $PublishDir } else { Join-Path $root 'dist\publish-single' }

$stamp = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$kerja = Join-Path ([IO.Path]::GetTempPath()) "seruni-uji-$stamp"

$namaExe = "Seruni22Bakery_Setup_v$Version.exe"
$namaMsi = "Seruni22Bakery_v$($Version)_x64.msi"
$jalurExeAsli = Join-Path $installerDir $namaExe
$jalurMsiAsli = Join-Path $installerDir $namaMsi

foreach ($berkas in @($jalurExeAsli, $jalurMsiAsli)) {
    if (-not (Test-Path $berkas)) { throw "Artefak tidak ditemukan: $berkas. Jalankan build lebih dulu." }
}

# Uji ini menyalin EXE+MSI lalu mengekstrak MSI dua kali; tanpa pemeriksaan ini kehabisan
# ruang disk muncul sebagai kegagalan uji yang membingungkan di tengah jalan.
$ukuranArtefakMB = [math]::Ceiling(((Get-Item $jalurExeAsli).Length + (Get-Item $jalurMsiAsli).Length) / 1MB)
$perluMB = $ukuranArtefakMB * 2 + 300
$driveUji = New-Object IO.DriveInfo([IO.Path]::GetPathRoot([IO.Path]::GetTempPath()))
$sisaMB = [math]::Floor($driveUji.AvailableFreeSpace / 1MB)
if ($sisaMB -lt $perluMB) {
    throw "Ruang disk $($driveUji.Name) tinggal $sisaMB MB; uji ini butuh sekitar $perluMB MB."
}

$logDir = Join-Path $kerja 'log'
New-Item -ItemType Directory -Force -Path $kerja, $logDir | Out-Null
if (-not $LaporanJson) { $LaporanJson = Join-Path $kerja 'uji-build-pro.json' }
$LaporanJson = Normalisasi-Jalur $LaporanJson

$hasil = New-Object System.Collections.ArrayList

function Catat-Uji([string]$nama, [bool]$lulus, [string]$detail, [string]$log) {
    [void]$hasil.Add([pscustomobject]@{
        Uji    = $nama
        Status = $(if ($lulus) { 'PASS' } else { 'FAIL' })
        Detail = $detail
        Log    = $log
    })
    Write-Host ("[{0}] {1} {2}" -f $(if ($lulus) { 'PASS' } else { 'FAIL' }), $nama, $detail)
}

# Menjalankan skrip PowerShell sebagai proses anak, menyalin keluarannya ke berkas log.
# Native stderr menjadi ErrorRecord bila ErrorActionPreference=Stop, jadi preferensi
# dilonggarkan selama pemanggilan dan kode keluar tetap diperiksa.
function Jalankan-Anak([string]$skrip, [string[]]$argumen, [string]$log) {
    $sebelum = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $keluar = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $skrip @argumen 2>&1 |
            ForEach-Object {
                if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.ToString() } else { [string]$_ }
            })
        $kode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $sebelum
    }
    $teks = ($keluar -join "`n")
    [IO.File]::WriteAllText($log, $teks, (New-Object Text.UTF8Encoding($false)))
    return [pscustomobject]@{ Kode = $kode; Teks = $teks }
}

function Jumlah-Gagal([string]$teks) {
    return ([regex]::Matches($teks, '(?m)^\[FAIL\]')).Count
}

function Punya-Gagal([string]$teks, [string]$nama) {
    $pola = '(?m)^\[FAIL\] ' + [regex]::Escape($nama) + ' '
    return [regex]::IsMatch($teks, $pola)
}

function Catat-Lewati([string]$nama, [string]$detail) {
    [void]$hasil.Add([pscustomobject]@{
        Uji    = $nama
        Status = 'SKIP'
        Detail = $detail
        Log    = ''
    })
    Write-Host ("[SKIP] {0} {1}" -f $nama, $detail)
}

function Baca-Laporan([string]$jalur) {
    if (-not (Test-Path $jalur)) { return $null }
    try { return Get-Content -Path $jalur -Raw | ConvertFrom-Json } catch { return $null }
}

# Baseline hijau = validator keluar 0, tanpa FAIL, dan payload MSI benar-benar dibandingkan
# terhadap referensi nyata. Bagian terakhir penting: bila folder publish hilang, validator
# menandai perbandingan payload SKIP dan hasilnya tetap "0 FAIL" tanpa membuktikan apa pun.
function Periksa-Baseline([string]$jalurJson) {
    $l = Baca-Laporan $jalurJson
    if (-not $l) {
        return [pscustomobject]@{ Lulus = $false; Detail = 'laporan JSON tidak terbaca' }
    }
    $referensi = 0
    $terekstrak = 0
    $hilangPayload = -1
    $berlebih = -1
    $beda = -1
    if ($l.payload) {
        $referensi = [int]$l.payload.Referensi
        $terekstrak = [int]$l.payload.Terekstrak
        $hilangPayload = [int]$l.payload.Hilang
        $berlebih = [int]$l.payload.Berlebih
        $beda = [int]$l.payload.Beda
    }
    $lulus = ($l.gagal -eq 0) -and ($l.jumlahPeriksa -ge 15) -and
        ($referensi -gt 0) -and ($terekstrak -gt 0) -and
        ($hilangPayload -eq 0) -and ($berlebih -eq 0) -and ($beda -eq 0)
    $detail = "periksa $($l.jumlahPeriksa), gagal $($l.gagal), payload $terekstrak/$referensi"
    return [pscustomobject]@{ Lulus = $lulus; Detail = $detail }
}

# ── Helper penyusun paket uji ──────────────────────────────────────────────
$paket = Join-Path $kerja 'paket'
New-Item -ItemType Directory -Force -Path $paket | Out-Null
Copy-Item $jalurExeAsli (Join-Path $paket $namaExe) -Force
Copy-Item $jalurMsiAsli (Join-Path $paket $namaMsi) -Force

function Hash-Berkas([string]$jalur) {
    return (Get-FileHash -Algorithm SHA256 -Path $jalur).Hash.ToLowerInvariant()
}

function Tulis-Sums([string]$tujuan, [string[]]$baris) {
    [IO.File]::WriteAllText($tujuan, (($baris -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
}

$hashExe = Hash-Berkas (Join-Path $paket $namaExe)
$hashMsi = Hash-Berkas (Join-Path $paket $namaMsi)
$barisExe = "$hashExe *$namaExe"
$barisMsi = "$hashMsi *$namaMsi"
$sumsPaket = Join-Path $paket 'SHA256SUMS.txt'
$sumsNormal = @($barisExe, $barisMsi)
Tulis-Sums $sumsPaket $sumsNormal

$jsonKerja = Join-Path $logDir 'hasil.json'

function Validasi([string]$log, [string[]]$argumen) {
    return Jalankan-Anak $validator $argumen $log
}

try {
    # ── 1. Baseline: paket asli harus hijau ────────────────────────────────
    Write-Host "== Uji $Version di $kerja =="
    $logDasar1 = Join-Path $logDir '01-dasar-asli.log'
    $r = Validasi $logDasar1 @('-Version', $Version, '-InstallerDir', $installerDir,
        '-PublishDir', $publishDir, '-JsonPath', $jsonKerja)
    $gagalDasar = Jumlah-Gagal $r.Teks
    $b = Periksa-Baseline $jsonKerja
    Catat-Uji 'baseline paket asli hijau' (($r.Kode -eq 0) -and $gagalDasar -eq 0 -and $b.Lulus) `
        "exit $($r.Kode), $gagalDasar FAIL, $($b.Detail)" $logDasar1

    # ── 2. Kontrol negatif A: hash EXE di SHA256SUMS diubah ────────────────
    $hashPalsu = ('0' * 64)
    if ($hashExe.Substring(0, 1) -eq '0') { $hashPalsu = ('f' * 64) }
    Tulis-Sums $sumsPaket @("$hashPalsu *$namaExe", $barisMsi)
    $logA = Join-Path $logDir '02-sums-exe-salah.log'
    $r = Validasi $logA @('-Version', $Version, '-InstallerDir', $paket,
        '-PublishDir', $publishDir, '-ManifestPath', (Join-Path $kerja 'belum-ada.txt'),
        '-JsonPath', $jsonKerja, '-LewatiEkstraksi')
    Catat-Uji 'kontrol A: hash EXE salah terdeteksi' `
        (($r.Kode -eq 1) -and (Jumlah-Gagal $r.Teks) -eq 1 -and (Punya-Gagal $r.Teks 'SHA256SUMS cocok dengan berkas')) `
        "exit $($r.Kode), $(Jumlah-Gagal $r.Teks) FAIL" $logA
    Tulis-Sums $sumsPaket $sumsNormal

    # ── 3. Kontrol negatif B: baris MSI hilang dari SHA256SUMS ─────────────
    Tulis-Sums $sumsPaket @($barisExe)
    $logB = Join-Path $logDir '03-sums-msi-hilang.log'
    $r = Validasi $logB @('-Version', $Version, '-InstallerDir', $paket,
        '-PublishDir', $publishDir, '-JsonPath', $jsonKerja, '-LewatiEkstraksi')
    Catat-Uji 'kontrol B: MSI tak tercantum terdeteksi' `
        (($r.Kode -eq 1) -and (Jumlah-Gagal $r.Teks) -eq 1 -and (Punya-Gagal $r.Teks 'SHA256SUMS mencantumkan MSI')) `
        "exit $($r.Kode), $(Jumlah-Gagal $r.Teks) FAIL" $logB
    Tulis-Sums $sumsPaket $sumsNormal

    # ── 4. Kontrol negatif C: manifest payload dirusak ─────────────────────
    $manifestAsli = Join-Path $kerja 'manifest-asli.txt'
    $akarPublish = Normalisasi-Jalur $publishDir
    $barisManifest = New-Object System.Collections.ArrayList
    foreach ($berkas in (Get-ChildItem -Path $akarPublish -Recurse -File -Force | Sort-Object FullName)) {
        $relatif = $berkas.FullName.Substring($akarPublish.Length).TrimStart('\')
        [void]$barisManifest.Add("$(Hash-Berkas $berkas.FullName) $($berkas.Length) $relatif")
    }
    [IO.File]::WriteAllText($manifestAsli, (($barisManifest -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))

    $manifestRusak = Join-Path $kerja 'manifest-rusak.txt'
    $isiManifest = Get-Content -Path $manifestAsli
    $barisDiubah = New-Object System.Collections.ArrayList
    $sudahUbah = $false
    foreach ($b in $isiManifest) {
        if (-not $sudahUbah -and $b -match '^([0-9a-f]{64})(\s+.*)$') {
            $awal = if ($Matches[1].Substring(0, 1) -eq '0') { 'f' } else { '0' }
            [void]$barisDiubah.Add($awal + $Matches[1].Substring(1) + $Matches[2])
            $sudahUbah = $true
        } else {
            [void]$barisDiubah.Add($b)
        }
    }
    [IO.File]::WriteAllText($manifestRusak, (($barisDiubah -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))

    $logC = Join-Path $logDir '04-manifest-payload-salah.log'
    $r = Validasi $logC @('-Version', $Version, '-InstallerDir', $paket,
        '-ManifestPath', $manifestRusak, '-PublishDir', $publishDir, '-JsonPath', $jsonKerja)
    Catat-Uji 'kontrol C: manifest payload salah terdeteksi' `
        (($r.Kode -eq 1) -and (Jumlah-Gagal $r.Teks) -eq 1 -and (Punya-Gagal $r.Teks 'payload MSI identik dengan manifest')) `
        "exit $($r.Kode), $(Jumlah-Gagal $r.Teks) FAIL" $logC

    # ── 5. Kontrol negatif D: MSI tidak ada ────────────────────────────────
    $msiSimpan = Join-Path $kerja 'msi-disimpan.bin'
    Move-Item (Join-Path $paket $namaMsi) $msiSimpan -Force
    Tulis-Sums $sumsPaket @($barisExe)
    $logD = Join-Path $logDir '05-msi-hilang.log'
    $r = Validasi $logD @('-Version', $Version, '-InstallerDir', $paket,
        '-PublishDir', $publishDir, '-JsonPath', $jsonKerja, '-LewatiEkstraksi')
    $namaGagalD = @('artefak MSI tersedia', 'SHA256SUMS mencantumkan MSI', 'payload MSI identik dengan manifest')
    $lengkapD = $true
    foreach ($n in $namaGagalD) { if (-not (Punya-Gagal $r.Teks $n)) { $lengkapD = $false } }
    Catat-Uji 'kontrol D: MSI hilang terdeteksi' `
        (($r.Kode -eq 1) -and (Jumlah-Gagal $r.Teks) -eq 3 -and $lengkapD) `
        "exit $($r.Kode), $(Jumlah-Gagal $r.Teks) FAIL" $logD
    Move-Item $msiSimpan (Join-Path $paket $namaMsi) -Force
    Tulis-Sums $sumsPaket $sumsNormal

    # ── 6. Kontrol negatif E: versi tertukar ───────────────────────────────
    $dirVersi = Join-Path $kerja 'versi-salah'
    New-Item -ItemType Directory -Force -Path $dirVersi | Out-Null
    $namaSalah = 'Seruni22Bakery_Setup_v9.9.9.exe'
    Copy-Item (Join-Path $paket $namaExe) (Join-Path $dirVersi $namaSalah) -Force
    Tulis-Sums (Join-Path $dirVersi 'SHA256SUMS.txt') @("$hashExe *$namaSalah")
    $logE = Join-Path $logDir '06-versi-tertukar.log'
    $r = Validasi $logE @('-Version', '9.9.9', '-InstallerDir', $dirVersi,
        '-PublishDir', $publishDir, '-JsonPath', $jsonKerja, '-TanpaMsi')
    Catat-Uji 'kontrol E: versi EXE tertukar terdeteksi' `
        (($r.Kode -eq 1) -and (Jumlah-Gagal $r.Teks) -eq 1 -and (Punya-Gagal $r.Teks 'versi EXE sama dengan versi paket')) `
        "exit $($r.Kode), $(Jumlah-Gagal $r.Teks) FAIL" $logE

    # ── 7. Kontrol negatif F: publish memuat kunci rahasia ─────────────────
    $publishPalsu = Join-Path $kerja 'publish-palsu'
    New-Item -ItemType Directory -Force -Path $publishPalsu | Out-Null
    [IO.File]::WriteAllText((Join-Path $publishPalsu 'jwt.key'), 'rahasia-uji', (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText((Join-Path $publishPalsu 'appsettings.json'), '{}', (New-Object Text.UTF8Encoding($false)))
    $logF = Join-Path $logDir '07-publish-kunci.log'
    $r = Validasi $logF @('-Version', $Version, '-InstallerDir', $paket,
        '-PublishDir', $publishPalsu, '-JsonPath', $jsonKerja, '-TanpaMsi')
    Catat-Uji 'kontrol F: kunci di publish terdeteksi' `
        (($r.Kode -eq 1) -and (Jumlah-Gagal $r.Teks) -eq 1 -and (Punya-Gagal $r.Teks 'publish bebas berkas terlarang')) `
        "exit $($r.Kode), $(Jumlah-Gagal $r.Teks) FAIL" $logF

    # ── 8. Kontrol negatif G: laporan build menyebut artefak hantu ─────────
    # Ukuran EXE sengaja dibedakan satu byte dan satu artefak tidak pernah ada; keduanya
    # harus muncul sebagai ketidaksesuaian, bukan dilewati.
    $infoPalsu = Join-Path $paket 'BUILD-INFO.json'
    $ukuranExe = (Get-Item (Join-Path $paket $namaExe)).Length
    $laporanPalsu = [pscustomobject]@{
        skema   = 1
        alat    = 'uji-build-pro.ps1'
        versi   = $Version
        artefak = @(
            [pscustomobject]@{ nama = $namaExe; ukuran = ($ukuranExe + 1); sha256 = $hashExe; tandaTangan = 'NotSigned' },
            [pscustomobject]@{ nama = 'Seruni22Bakery_v99.0.0_x64.msi'; ukuran = 1234; sha256 = ('a' * 64); tandaTangan = 'NotSigned' }
        )
    }
    [IO.File]::WriteAllText($infoPalsu, ($laporanPalsu | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    $logG = Join-Path $logDir '08-laporan-artefak-hantu.log'
    $r = Validasi $logG @('-Version', $Version, '-InstallerDir', $paket,
        '-PublishDir', $publishDir, '-JsonPath', $jsonKerja, '-LewatiEkstraksi', '-TanpaMsi')
    Catat-Uji 'kontrol G: laporan artefak hantu terdeteksi' `
        (($r.Kode -eq 1) -and (Jumlah-Gagal $r.Teks) -eq 1 -and (Punya-Gagal $r.Teks 'BUILD-INFO.json sepakat dengan paket')) `
        "exit $($r.Kode), $(Jumlah-Gagal $r.Teks) FAIL" $logG
    Remove-Item $infoPalsu -Force -ErrorAction SilentlyContinue

    # ── 9. Kontrol negatif H: nama ber-.. di SHA256SUMS ────────────────────
    # Tanpa penolakan ini, validator akan membaca berkas di luar folder paket.
    $barisJahat = "$('b' * 64) *..\..\Windows\System32\drivers\etc\hosts"
    Tulis-Sums $sumsPaket @($barisExe, $barisMsi, $barisJahat)
    $logH = Join-Path $logDir '09-sums-nama-berbahaya.log'
    $r = Validasi $logH @('-Version', $Version, '-InstallerDir', $paket,
        '-PublishDir', $publishDir, '-JsonPath', $jsonKerja, '-LewatiEkstraksi', '-TanpaMsi')
    Catat-Uji 'kontrol H: nama ber-.. di SHA256SUMS terdeteksi' `
        (($r.Kode -eq 1) -and (Jumlah-Gagal $r.Teks) -eq 1 -and (Punya-Gagal $r.Teks 'SHA256SUMS hanya memuat nama berkas polos')) `
        "exit $($r.Kode), $(Jumlah-Gagal $r.Teks) FAIL" $logH
    Tulis-Sums $sumsPaket $sumsNormal

    # ── 10. Penjaga versi terbit build-pro menolak tanpa izin ──────────────
    if ($LewatiUjiBuildPro) {
        Catat-Lewati 'penjaga versi terbit menolak build ulang' 'dilewati atas permintaan (-LewatiUjiBuildPro)'
    } elseif (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Catat-Lewati 'penjaga versi terbit menolak build ulang' 'git tidak ditemukan; tag versi tidak bisa diperiksa'
    } else {
        $tagAda = ((& git -C $root tag --list "v$Version") -join '').Trim() -ne ''
        if (-not $tagAda) {
            # Tanpa tag, penjaga build-pro memang tidak menolak; menjalankan uji ini akan
            # memulai build sungguhan. Itu bukan kegagalan pagar, jadi ditandai SKIP.
            Catat-Lewati 'penjaga versi terbit menolak build ulang' "tag v$Version belum ada; penjaga tidak akan menolak build ini"
        } else {
            $keluarDist = Join-Path $kerja 'keluaran-tolak'
            $logI = Join-Path $logDir '10-penjaga-versi-terbit.log'
            $r = Jalankan-Anak $buildPro @('-Version', $Version, '-OutRoot', $keluarDist, '-SkipTests', '-SkipMsi', '-SkipVerifikasiPaket') $logI
            $adaPenjaga = [regex]::IsMatch($r.Teks, '(?m)^\[FAIL\] penjaga versi terbit ')
            $tidakBuild = -not (Test-Path (Join-Path $keluarDist 'publish-single'))
            Catat-Uji 'penjaga versi terbit menolak build ulang' `
                (($r.Kode -eq 1) -and $adaPenjaga -and $tidakBuild) `
                "exit $($r.Kode), penjaga=$adaPenjaga, tanpa publish=$tidakBuild" $logI
        }
    }

    # ── 11. Baseline sesudah sabotase: paket asli masih hijau ──────────────
    $logDasar2 = Join-Path $logDir '11-dasar-akhir.log'
    $r = Validasi $logDasar2 @('-Version', $Version, '-InstallerDir', $installerDir,
        '-PublishDir', $publishDir, '-JsonPath', $jsonKerja)
    $gagalAkhir = Jumlah-Gagal $r.Teks
    $b = Periksa-Baseline $jsonKerja
    Catat-Uji 'baseline akhir tetap hijau' (($r.Kode -eq 0) -and $gagalAkhir -eq 0 -and $b.Lulus) `
        "exit $($r.Kode), $gagalAkhir FAIL, $($b.Detail)" $logDasar2
}
finally {
    # Salinan EXE/MSI besar dihapus; log dan laporan JSON dipertahankan sebagai bukti.
    foreach ($sisa in @($paket, (Join-Path $kerja 'versi-salah'), (Join-Path $kerja 'publish-palsu'), (Join-Path $kerja 'keluaran-tolak'))) {
        if (Test-Path $sisa) { Remove-Item -Path $sisa -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Remove-Item (Join-Path $kerja 'msi-disimpan.bin') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $kerja 'manifest-asli.txt'), (Join-Path $kerja 'manifest-rusak.txt') -Force -ErrorAction SilentlyContinue
}

$gagal = @($hasil | Where-Object Status -eq 'FAIL')
$lewati = @($hasil | Where-Object Status -eq 'SKIP')
$laporan = [pscustomobject]@{
    skema       = 1
    alat        = 'uji-build-pro.ps1'
    waktu       = (Get-Date).ToString('o')
    versi       = $Version
    installerDir = $installerDir
    logDir      = $logDir
    jumlahUji   = $hasil.Count
    lulus       = @($hasil | Where-Object Status -eq 'PASS').Count
    gagal       = $gagal.Count
    dilewati    = $lewati.Count
    uji         = @($hasil)
}
$jsonDir = Split-Path -Parent $LaporanJson
if (-not (Test-Path $jsonDir)) { New-Item -ItemType Directory -Path $jsonDir -Force | Out-Null }
[IO.File]::WriteAllText($LaporanJson, ($laporan | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))

Write-Host ''
Write-Host '================ HASIL UJI BUILD-PRO ================'
$hasil | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Write-Host "Uji: $($hasil.Count) total, $($gagal.Count) gagal, $($lewati.Count) dilewati"
Write-Host "Log: $logDir"
Write-Host "Laporan JSON: $LaporanJson"
if ($gagal.Count -gt 0) {
    Write-Host "UJI GAGAL: $($gagal.Count) harapan tidak terpenuhi."
    exit 1
}
if ($lewati.Count -gt 0) {
    Write-Host 'UJI SELESAI DENGAN UJI DILEWATI - bukan bukti lengkap.'
    exit 0
}
Write-Host 'SEMUA UJI LULUS: pagar menolak paket rusak dan baseline tetap hijau.'
exit 0
