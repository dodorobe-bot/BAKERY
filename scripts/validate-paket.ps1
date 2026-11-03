<#
  Validator paket Seruni 22 Bakery yang sudah dibangun.

  Tujuan: satu perintah yang menjawab "apakah paket ini benar-benar paket yang
  dimaksud?" tanpa langkah manual. Dulu identitas MSI dibaca lewat decompile dan isi
  payload dibandingkan dengan ekstraksi /a yang dijalankan tangan; sekarang keduanya
  diperiksa otomatis dan hasilnya ditulis sebagai JSON yang bisa diperiksa ulang.

  Yang diperiksa:
    - kedua artefak ada dan SHA256SUMS.txt mencantumkan serta cocok dengan keduanya
    - versi EXE (VersionInfo) dan versi MSI (tabel Property) sama dengan versi yang diminta
    - status Authenticode dilaporkan apa adanya (Valid atau NotSigned; selain itu gagal)
    - ProductName, Manufacturer, ProductCode, UpgradeCode, dan arsitektur x64 MSI
    - payload MSI (ekstraksi administratif /a) identik dengan manifest publish,
      termasuk berkas yang hilang, berlebih, atau berubah isi
    - folder publish bebas basis data, kunci, kredensial, dan berkas debug
    - UpgradeCode sama dengan MSI lain di folder yang sama (rantai peningkatan utuh)

  Tidak melakukan: pemasangan, penandatanganan, atau penulisan ke folder data pengguna.
  Ekstraksi /a bersifat administratif (tidak memasang apa pun) dan dibersihkan lagi.

  Pemakaian:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\validate-paket.ps1 -Version 1.2.12
    ... -InstallerDir dist\installer -PublishDir dist\publish-single
    ... -LewatiEkstraksi            (pemeriksaan cepat tanpa membongkar MSI)
    ... -JsonPath $env:TEMP\laporan.json   (harus di dalam repo atau folder temp)

  Keluar 1 bila ada pemeriksaan FAIL. SKIP/WARN tidak menggagalkan, tetapi selalu
  dicatat di laporan supaya "tidak diperiksa" tidak pernah terlihat seperti "lulus".
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$InstallerDir = "",
    [string]$PublishDir = "",
    [string]$ManifestPath = "",
    [string]$JsonPath = "",
    [string]$ProductName = "Seruni 22 Bakery",
    [string]$Manufacturer = "Seruni 22",
    [switch]$LewatiEkstraksi,
    [switch]$TanpaMsi
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Normalisasi-Jalur([string]$jalur) {
    return ([IO.Path]::GetFullPath($jalur)).TrimEnd('\', '/')
}

$pemeriksaan = New-Object System.Collections.ArrayList

function Catat([string]$nama, [string]$status, [string]$detail) {
    [void]$pemeriksaan.Add([pscustomobject]@{
        Nama   = $nama
        Status = $status
        Detail = $detail
    })
    Write-Host ("[{0}] {1} {2}" -f $status, $nama, $detail)
}
function Lulus([string]$nama, [string]$detail) { Catat $nama 'PASS' $detail }
function Gagal([string]$nama, [string]$detail) { Catat $nama 'FAIL' $detail }
function Lewati([string]$nama, [string]$detail) { Catat $nama 'SKIP' $detail }
function Peringatkan([string]$nama, [string]$detail) { Catat $nama 'WARN' $detail }

function Baca-PropertiMsi([string]$jalur) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($jalur, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @('SELECT Property, Value FROM Property'))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $hasil = @{}
    while ($true) {
        $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if (-not $rec) { break }
        $nama = $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(1))
        $nilai = $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(2))
        if ($nama) { $hasil[[string]$nama] = [string]$nilai }
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return $hasil
}

function Baca-TemplateMsi([string]$jalur) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $summary = $installer.GetType().InvokeMember('SummaryInformation', 'GetProperty', $null, $installer, @($jalur, 0))
    return [string]$summary.GetType().InvokeMember('Property', 'GetProperty', $null, $summary, @(7))
}

function Get-PetaBerkas([string]$akar) {
    $peta = @{}
    if (-not (Test-Path $akar)) { return $peta }
    $akarPenuh = Normalisasi-Jalur $akar
    foreach ($berkas in (Get-ChildItem -Path $akarPenuh -Recurse -File -Force)) {
        $relatif = $berkas.FullName.Substring($akarPenuh.Length).TrimStart('\')
        $peta[$relatif] = [pscustomobject]@{
            Ukuran = $berkas.Length
            Hash   = (Get-FileHash -Algorithm SHA256 -Path $berkas.FullName).Hash.ToLowerInvariant()
        }
    }
    return $peta
}

function Baca-Manifest([string]$jalur) {
    $peta = @{}
    if (-not (Test-Path $jalur)) { return $peta }
    foreach ($baris in (Get-Content -Path $jalur)) {
        if ($baris -match '^([0-9a-fA-F]{64})\s+(\d+)\s+(.+)$') {
            $relatif = $Matches[3].Trim().Replace('/', '\')
            $peta[$relatif] = [pscustomobject]@{
                Ukuran = [int64]$Matches[2]
                Hash   = $Matches[1].ToLowerInvariant()
            }
        }
    }
    return $peta
}

function Bandingkan-Payload($referensi, $terekstrak) {
    $hasil = @{ Hilang = @(); Berlebih = @(); Beda = @() }
    $hilang = New-Object System.Collections.ArrayList
    $berlebih = New-Object System.Collections.ArrayList
    $beda = New-Object System.Collections.ArrayList
    foreach ($kunci in $referensi.Keys) {
        if (-not $terekstrak.ContainsKey($kunci)) {
            [void]$hilang.Add($kunci)
        } elseif ($terekstrak[$kunci].Hash -ne $referensi[$kunci].Hash -or
                  $terekstrak[$kunci].Ukuran -ne $referensi[$kunci].Ukuran) {
            [void]$beda.Add($kunci)
        }
    }
    foreach ($kunci in $terekstrak.Keys) {
        if (-not $referensi.ContainsKey($kunci)) { [void]$berlebih.Add($kunci) }
    }
    $hasil.Hilang = @($hilang)
    $hasil.Berlebih = @($berlebih)
    $hasil.Beda = @($beda)
    return $hasil
}

function Ringkas-Contoh([string[]]$daftar) {
    if ($daftar.Count -eq 0) { return '' }
    $ambil = @($daftar | Select-Object -First 5)
    $teks = $ambil -join ', '
    if ($daftar.Count -gt 5) { $teks += ", ... ($($daftar.Count) total)" }
    return $teks
}

# Satu sumber versi: <Version> di proyek host.
$hostProject = Join-Path $root 'src\Host.Service\BakeryDesktop.Host.Service.csproj'
$versiCsproj = ([xml](Get-Content -Raw $hostProject)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $Version) {
    if (-not $versiCsproj) { throw 'Versi tidak diberikan dan proyek host tidak memuat <Version>.' }
    $Version = $versiCsproj.Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versi '$Version' bukan bentuk x.y.z." }

$installerDir = if ($InstallerDir) { Normalisasi-Jalur $InstallerDir } else { Join-Path $root 'dist\installer' }
# Nama variabel PowerShell tidak peka huruf besar/kecil: $publishDir dan $PublishDir adalah
# variabel yang sama. Jangan pernah mengosongkannya sebelum parameter diperiksa.
if ($PublishDir) { $publishDir = Normalisasi-Jalur $PublishDir }
elseif (Test-Path (Join-Path $root 'dist\publish-single')) { $publishDir = Join-Path $root 'dist\publish-single' }
else { $publishDir = '' }
if ($ManifestPath) { $ManifestPath = Normalisasi-Jalur $ManifestPath }
if (-not $JsonPath) { $JsonPath = Join-Path $installerDir 'VERIFY-REPORT.json' }

# Laporan bukti tidak boleh ditulis ke folder sembarang: satu salah ketik parameter bisa
# membuat berkas di luar repo/temp. Sama seperti pagar -OutRoot di build-pro.
$jsonPenuh = Normalisasi-Jalur $JsonPath
$repoPenuh = Normalisasi-Jalur $root
$tempPenuh = Normalisasi-Jalur ([IO.Path]::GetTempPath())
if (-not ($jsonPenuh.StartsWith($repoPenuh + '\', [StringComparison]::OrdinalIgnoreCase) -or
          $jsonPenuh.StartsWith($tempPenuh + '\', [StringComparison]::OrdinalIgnoreCase))) {
    throw "JsonPath '$JsonPath' ditolak: laporan hanya boleh ditulis di dalam repo ($repoPenuh) atau folder temp ($tempPenuh)."
}
$JsonPath = $jsonPenuh

$namaExe = "Seruni22Bakery_Setup_v$Version.exe"
$namaMsi = "Seruni22Bakery_v$($Version)_x64.msi"
$jalurExe = Join-Path $installerDir $namaExe
$jalurMsi = Join-Path $installerDir $namaMsi

Write-Host "== Validasi paket $Version =="
Write-Host "   folder paket : $installerDir"
if ($publishDir) { Write-Host "   folder publish: $publishDir" }
if ($ManifestPath) { Write-Host "   manifest      : $ManifestPath" }

$adaExe = Test-Path $jalurExe
$adaMsi = Test-Path $jalurMsi
if ($adaExe) { Lulus 'artefak EXE tersedia' $namaExe } else { Gagal 'artefak EXE tersedia' "tidak ada: $jalurExe" }
if ($adaMsi) { Lulus 'artefak MSI tersedia' $namaMsi }
elseif ($TanpaMsi) { Lewati 'artefak MSI tersedia' 'dilewati atas permintaan (-TanpaMsi)' }
else { Gagal 'artefak MSI tersedia' "tidak ada: $jalurMsi" }

# ── Checksum ────────────────────────────────────────────────────────────────
$sumPath = Join-Path $installerDir 'SHA256SUMS.txt'
$artefak = @()
if (Test-Path $sumPath) {
    $mentah = Get-Content -Path $sumPath -Raw
    if ($mentah -match "`r") {
        Gagal 'SHA256SUMS memakai akhir baris LF' 'berkas memuat CRLF; sha256sum -c di Git Bash menolak baris ber-CR'
    } else {
        Lulus 'SHA256SUMS memakai akhir baris LF' 'aman untuk sha256sum -c'
    }
    $petaSum = @{}
    $namaTidakAman = New-Object System.Collections.ArrayList
    foreach ($baris in ($mentah -split "`n")) {
        if ($baris -match '^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') {
            $nama = $Matches[2]
            # sha256sum hanya menamai berkas di folder yang sama. Nama yang memuat pemisah
            # jalur atau ".." membuat Join-Path di bawah membaca berkas di luar folder paket,
            # sehingga pemeriksaan hash bisa lulus memakai berkas yang salah.
            if ($nama -match '[\\/]' -or $nama -match '(^|\.)\.\.($|\.)') {
                [void]$namaTidakAman.Add($nama)
                continue
            }
            $petaSum[$nama] = $Matches[1].ToLowerInvariant()
        }
    }
    if ($namaTidakAman.Count -eq 0) {
        Lulus 'SHA256SUMS hanya memuat nama berkas polos' 'tanpa pemisah jalur atau ..'
    } else {
        Gagal 'SHA256SUMS hanya memuat nama berkas polos' (Ringkas-Contoh @($namaTidakAman))
    }
    if ($petaSum.ContainsKey($namaExe)) { Lulus 'SHA256SUMS mencantumkan EXE' $namaExe }
    else { Gagal 'SHA256SUMS mencantumkan EXE' "tidak ada baris untuk $namaExe" }
    if ($petaSum.ContainsKey($namaMsi)) { Lulus 'SHA256SUMS mencantumkan MSI' $namaMsi }
    elseif ($TanpaMsi) { Lewati 'SHA256SUMS mencantumkan MSI' 'dilewati atas permintaan (-TanpaMsi)' }
    else { Gagal 'SHA256SUMS mencantumkan MSI' "tidak ada baris untuk $namaMsi" }

    $cocok = 0
    $tidakCocok = New-Object System.Collections.ArrayList
    foreach ($nama in $petaSum.Keys) {
        $jalur = Join-Path $installerDir $nama
        if (-not (Test-Path $jalur)) {
            [void]$tidakCocok.Add("$nama (berkas tidak ada)")
            continue
        }
        $hash = (Get-FileHash -Algorithm SHA256 -Path $jalur).Hash.ToLowerInvariant()
        if ($hash -eq $petaSum[$nama]) { $cocok++ } else { [void]$tidakCocok.Add("$nama (hash beda)") }
    }
    if ($tidakCocok.Count -eq 0) {
        Lulus 'SHA256SUMS cocok dengan berkas' "$cocok baris diperiksa"
    } else {
        Gagal 'SHA256SUMS cocok dengan berkas' (Ringkas-Contoh @($tidakCocok))
    }
    if ($petaSum.ContainsKey($namaExe) -and $adaExe) {
        $artefak += [pscustomobject]@{ Nama = $namaExe; Ukuran = (Get-Item $jalurExe).Length; Sha256 = $petaSum[$namaExe] }
    }
    if ($petaSum.ContainsKey($namaMsi) -and $adaMsi) {
        $artefak += [pscustomobject]@{ Nama = $namaMsi; Ukuran = (Get-Item $jalurMsi).Length; Sha256 = $petaSum[$namaMsi] }
    }
} else {
    Gagal 'SHA256SUMS tersedia' "tidak ada: $sumPath"
}

# ── Versi dan tanda tangan EXE ──────────────────────────────────────────────
if ($adaExe) {
    $infoExe = (Get-Item $jalurExe).VersionInfo
    # Inno menulis VersionInfoProductVersion sebagai string berlebar tetap dengan spasi
    # pengisi; tanpa Trim() versi yang benar terlihat berbeda.
    $versiExe = ([string]$infoExe.ProductVersion).Trim()
    if ($versiExe -eq $Version) {
        Lulus 'versi EXE sama dengan versi paket' $versiExe
    } else {
        Gagal 'versi EXE sama dengan versi paket' "ProductVersion '$versiExe'"
    }
    $tandaExe = (Get-AuthenticodeSignature -FilePath $jalurExe)
    if ($tandaExe.Status -in @('Valid', 'NotSigned')) {
        Lulus 'status tanda tangan EXE tercatat' "$($tandaExe.Status)"
    } else {
        Gagal 'status tanda tangan EXE tercatat' "$($tandaExe.Status) (bukan Valid/NotSigned)"
    }
}

# ── Identitas MSI ──────────────────────────────────────────────────────────
$propMsi = @{}
if ($adaMsi) {
    $tandaMsi = (Get-AuthenticodeSignature -FilePath $jalurMsi)
    if ($tandaMsi.Status -in @('Valid', 'NotSigned')) {
        Lulus 'status tanda tangan MSI tercatat' "$($tandaMsi.Status)"
    } else {
        Gagal 'status tanda tangan MSI tercatat' "$($tandaMsi.Status) (bukan Valid/NotSigned)"
    }

    $propMsi = Baca-PropertiMsi $jalurMsi
    if ($propMsi['ProductVersion'] -eq $Version) {
        Lulus 'versi MSI sama dengan versi paket' $propMsi['ProductVersion']
    } else {
        Gagal 'versi MSI sama dengan versi paket' "ProductVersion '$($propMsi['ProductVersion'])'"
    }
    if ($propMsi['ProductName'] -eq $ProductName -and $propMsi['Manufacturer'] -eq $Manufacturer) {
        Lulus 'identitas MSI (nama/pembuat)' "$($propMsi['ProductName']) / $($propMsi['Manufacturer'])"
    } else {
        Gagal 'identitas MSI (nama/pembuat)' "$($propMsi['ProductName']) / $($propMsi['Manufacturer'])"
    }
    if ($propMsi['ProductCode'] -match '^\{[0-9A-Fa-f-]{36}\}$') {
        Lulus 'ProductCode berbentuk GUID' $propMsi['ProductCode']
    } else {
        Gagal 'ProductCode berbentuk GUID' "'$($propMsi['ProductCode'])'"
    }
    if ($propMsi['UpgradeCode'] -match '^\{[0-9A-Fa-f-]{36}\}$') {
        Lulus 'UpgradeCode berbentuk GUID' $propMsi['UpgradeCode']
    } else {
        Gagal 'UpgradeCode berbentuk GUID' "'$($propMsi['UpgradeCode'])'"
    }
    $template = Baca-TemplateMsi $jalurMsi
    if ($template -match 'x64') {
        Lulus 'template MSI x64' $template
    } else {
        Gagal 'template MSI x64' "'$template'"
    }

    # UpgradeCode harus sama dengan paket lain di folder yang sama; kalau berubah,
    # peningkatan antar versi tidak akan dikenali Windows Installer.
    $msiLain = @(Get-ChildItem -Path $installerDir -Filter 'Seruni22Bakery_v*_x64.msi' -File |
        Where-Object { $_.FullName -ne $jalurMsi })
    $bedaUpgrade = New-Object System.Collections.ArrayList
    foreach ($lain in $msiLain) {
        try {
            $propLain = Baca-PropertiMsi $lain.FullName
            if ($propLain['UpgradeCode'] -ne $propMsi['UpgradeCode']) {
                [void]$bedaUpgrade.Add("$($lain.Name)=$($propLain['UpgradeCode'])")
            }
        } catch {
            Peringatkan 'UpgradeCode banding paket lain' "tidak bisa membaca $($lain.Name): $($_.Exception.Message)"
        }
    }
    if ($msiLain.Count -eq 0) {
        Lewati 'UpgradeCode sama dengan paket lain' 'tidak ada MSI lain di folder ini'
    } elseif ($bedaUpgrade.Count -eq 0) {
        Lulus 'UpgradeCode sama dengan paket lain' "$($msiLain.Count) MSI pembanding"
    } else {
        Gagal 'UpgradeCode sama dengan paket lain' (Ringkas-Contoh @($bedaUpgrade))
    }
}

# ── Payload: bandingkan isi MSI dengan manifest publish ────────────────────
$referensi = @{}
if ($ManifestPath) {
    if (Test-Path $ManifestPath) { $referensi = Baca-Manifest $ManifestPath }
} elseif ($publishDir -and (Test-Path $publishDir)) {
    $referensi = Get-PetaBerkas $publishDir
}

$ringkasPayload = [pscustomobject]@{ Referensi = 0; Terekstrak = 0; Hilang = 0; Berlebih = 0; Beda = 0 }
if ($TanpaMsi) {
    Lewati 'payload MSI identik dengan manifest' 'dilewati atas permintaan (-TanpaMsi)'
} elseif ($referensi.Count -eq 0) {
    # Manifest yang diminta tetapi tidak terbaca adalah salah ketik parameter, bukan
    # "tidak ada pembanding": menjadikannya SKIP membuat validator hijau tanpa perbandingan.
    if ($ManifestPath) {
        Gagal 'payload MSI identik dengan manifest' "manifest tidak ditemukan atau kosong: $ManifestPath"
    } else {
        Lewati 'payload MSI identik dengan manifest' 'tidak ada folder publish maupun manifest sebagai pembanding'
    }
} elseif ($referensi.Count -gt 0 -and -not $adaMsi) {
    Gagal 'payload MSI identik dengan manifest' 'MSI tidak tersedia untuk diekstrak'
} elseif ($LewatiEkstraksi) {
    Lewati 'payload MSI identik dengan manifest' 'dilewati atas permintaan (-LewatiEkstraksi)'
} else {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("seruni-validate-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    $logMsi = Join-Path $temp 'ekstrak.log'
    try {
        Write-Host "   mengekstrak MSI (administratif) ke $temp"
        $proses = Start-Process -FilePath 'msiexec.exe' -PassThru -Wait -ArgumentList @(
            '/a', $jalurMsi, '/qn', "TARGETDIR=$temp", '/l*v', $logMsi)
        if ($proses.ExitCode -ne 0) {
            Gagal 'payload MSI identik dengan manifest' "ekstraksi administratif gagal, exit $($proses.ExitCode). Log: $logMsi"
        } else {
            $hostFile = Get-ChildItem -Path $temp -Recurse -Filter 'Seruni22Bakery.Host.exe' -File -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if (-not $hostFile) {
                Gagal 'payload MSI identik dengan manifest' 'hasil ekstraksi tidak memuat Seruni22Bakery.Host.exe'
            } else {
                $akarPayload = $hostFile.Directory.FullName
                $terekstrak = Get-PetaBerkas $akarPayload
                $selisih = Bandingkan-Payload $referensi $terekstrak
                $ringkasPayload = [pscustomobject]@{
                    Referensi  = $referensi.Count
                    Terekstrak = $terekstrak.Count
                    Hilang     = $selisih.Hilang.Count
                    Berlebih   = $selisih.Berlebih.Count
                    Beda       = $selisih.Beda.Count
                }
                if ($selisih.Hilang.Count -eq 0 -and $selisih.Berlebih.Count -eq 0 -and $selisih.Beda.Count -eq 0) {
                    Lulus 'payload MSI identik dengan manifest' "$($terekstrak.Count) berkas, hash dan ukuran sama"
                } else {
                    $rincian = @()
                    if ($selisih.Hilang.Count -gt 0) { $rincian += "hilang: " + (Ringkas-Contoh $selisih.Hilang) }
                    if ($selisih.Berlebih.Count -gt 0) { $rincian += "berlebih: " + (Ringkas-Contoh $selisih.Berlebih) }
                    if ($selisih.Beda.Count -gt 0) { $rincian += "beda: " + (Ringkas-Contoh $selisih.Beda) }
                    Gagal 'payload MSI identik dengan manifest' ($rincian -join ' | ')
                }
            }
        }
    } finally {
        # msiexec kadang masih memegang berkas hasil ekstraksi beberapa saat setelah
        # proses selesai; satu percobaan hapus meninggalkan folder temp permanen.
        $terhapus = $false
        for ($percobaan = 0; $percobaan -lt 5 -and -not $terhapus; $percobaan++) {
            if ($percobaan -gt 0) { Start-Sleep -Milliseconds (500 * $percobaan) }
            Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
            $terhapus = -not (Test-Path $temp)
        }
        if (-not $terhapus) {
            Write-Host "   peringatan: folder temp $temp tidak bisa dihapus; bersihkan manual"
        }
    }
}

# ── Folder publish bebas berkas terlarang ──────────────────────────────────
if ($publishDir -and (Test-Path $publishDir)) {
    $polaTerlarang = @(
        '*.db', '*.db-wal', '*.db-shm', '*.sqlite', '*.sqlite3',
        'jwt.key', 'jwt.key.*', 'appsettings.Development.json',
        '.env', '.env.*', '*.pfx', '*.p12', '*.pem', '*.key', '*.snk', '*.pdb'
    )
    $terlarang = New-Object System.Collections.ArrayList
    foreach ($berkas in (Get-ChildItem -Path $publishDir -Recurse -File -Force)) {
        foreach ($pola in $polaTerlarang) {
            if ($berkas.Name -like $pola) {
                [void]$terlarang.Add($berkas.FullName.Substring($publishDir.Length).TrimStart('\'))
                break
            }
        }
    }
    if ($terlarang.Count -eq 0) {
        Lulus 'publish bebas berkas terlarang' 'tidak ada basis data, kunci, kredensial, atau berkas debug'
    } else {
        Gagal 'publish bebas berkas terlarang' (Ringkas-Contoh @($terlarang))
    }
} else {
    Lewati 'publish bebas berkas terlarang' 'folder publish tidak diberikan/tidak ada'
}

# ── Laporan build (jika ada) harus sepakat ────────────────────────────────
$infoJson = Join-Path $installerDir 'BUILD-INFO.json'
if (Test-Path $infoJson) {
    try {
        $info = Get-Content -Path $infoJson -Raw | ConvertFrom-Json
        $salah = New-Object System.Collections.ArrayList
        # build-pro menulis kunci "versi"; kunci "version" diterima untuk laporan lama.
        $versiLaporan = $info.versi
        if (-not $versiLaporan) { $versiLaporan = $info.version }
        if ($versiLaporan -ne $Version) { [void]$salah.Add("versi='$versiLaporan'") }
        foreach ($a in @($info.artefak)) {
            if (-not $a) { continue }
            $namaArtefak = [string]$a.nama
            if (-not $namaArtefak) { [void]$salah.Add('artefak tanpa nama'); continue }
            $jalur = Join-Path $installerDir $namaArtefak
            # Artefak yang tercatat di laporan tetapi tidak ada adalah ketidaksesuaian:
            # dulu pemeriksaan ini dilewati diam-diam sehingga laporan hantu terlihat sah.
            if (-not (Test-Path $jalur)) {
                [void]$salah.Add("$namaArtefak tercatat di laporan tetapi tidak ada")
                continue
            }
            if ($null -ne $a.ukuran -and (Get-Item $jalur).Length -ne [int64]$a.ukuran) {
                [void]$salah.Add("$namaArtefak ukuran tidak sama dengan laporan")
            }
            $hash = (Get-FileHash -Algorithm SHA256 -Path $jalur).Hash.ToLowerInvariant()
            if ($hash -ne $a.sha256) { [void]$salah.Add("$namaArtefak hash tidak sama dengan laporan") }
        }
        if ($salah.Count -eq 0) { Lulus 'BUILD-INFO.json sepakat dengan paket' 'versi dan hash cocok' }
        else { Gagal 'BUILD-INFO.json sepakat dengan paket' (Ringkas-Contoh @($salah)) }
    } catch {
        Gagal 'BUILD-INFO.json sepakat dengan paket' "tidak bisa dibaca: $($_.Exception.Message)"
    }
} else {
    Lewati 'BUILD-INFO.json sepakat dengan paket' 'laporan build belum ada'
}

# ── Ringkasan dan laporan JSON ────────────────────────────────────────────
$gagal = @($pemeriksaan | Where-Object Status -eq 'FAIL')
$lulus = @($pemeriksaan | Where-Object Status -eq 'PASS')
$lewati = @($pemeriksaan | Where-Object Status -eq 'SKIP')
$peringatan = @($pemeriksaan | Where-Object Status -eq 'WARN')

$laporan = [pscustomobject]@{
    skema          = 1
    alat           = 'validate-paket.ps1'
    waktu          = (Get-Date).ToString('o')
    versi          = $Version
    installerDir   = $installerDir
    publishDir     = $publishDir
    manifestPath   = $ManifestPath
    jumlahPeriksa  = $pemeriksaan.Count
    lulus          = $lulus.Count
    gagal          = $gagal.Count
    lewati         = $lewati.Count
    peringatan     = $peringatan.Count
    artefak        = @($artefak)
    msi            = [pscustomobject]@{
        productName  = $propMsi['ProductName']
        productCode  = $propMsi['ProductCode']
        upgradeCode  = $propMsi['UpgradeCode']
        productVersion = $propMsi['ProductVersion']
    }
    payload        = $ringkasPayload
    pemeriksaan    = @($pemeriksaan)
}

$jsonDir = Split-Path -Parent $JsonPath
if (-not (Test-Path $jsonDir)) { New-Item -ItemType Directory -Path $jsonDir -Force | Out-Null }
[IO.File]::WriteAllText($JsonPath, ($laporan | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))

Write-Host ''
Write-Host ("TOTAL {0} pemeriksaan, {1} gagal, {2} lewati, {3} peringatan" -f $pemeriksaan.Count, $gagal.Count, $lewati.Count, $peringatan.Count)
Write-Host "Laporan JSON: $JsonPath"

if ($gagal.Count -gt 0) {
    Write-Host "PAKET TIDAK VALID: $($gagal.Count) pemeriksaan gagal."
    exit 1
}
Write-Host 'PAKET VALID untuk pemeriksaan yang dijalankan.'
exit 0
