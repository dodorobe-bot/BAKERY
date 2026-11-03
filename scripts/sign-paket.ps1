# Tanda tangan Authenticode untuk paket Seruni 22 Bakery.
# Skrip ini TIDAK membuat sertifikat: ia memakai sertifikat code signing yang sudah
# terpasang (token USB di CurrentUser\My / LocalMachine\My) atau berkas PFX.
#
# Tanpa argumen berkas, skrip menandatangani Seruni22Bakery_Setup_v*.exe dan
# Seruni22Bakery_v*_x64.msi di dist\installer, lalu menulis ulang SHA256SUMS.txt
# karena isi berkas berubah setelah ditandatangani.
#
# Yang diverifikasi Windows saat pengguna mengunduh/menjalankan paket adalah tanda
# tangan installer itu sendiri, jadi urutan normal cukup:
#   1) .\scripts\build-installer.ps1 -Version 1.2.6   (atau -SignThumbprint <th> sekalian)
#      .\scripts\build-msi.ps1 -Version 1.2.6
#   2) .\scripts\sign-paket.ps1 -Thumbprint <th>
#
# -TermasukIsi ikut menandatangani exe di dist\publish-single (peluncur + host).
# Itu hanya berguna bila paket dibangun ulang dari folder publish-single yang sama
# (build-msi.ps1 tidak mem-publish ulang, build-installer.ps1 mem-publish ulang
# sehingga tanda tangan payload hilang lagi). Tanpa rebuild, tanda tangan payload
# tidak masuk ke paket mana pun.
#
# Contoh PFX: .\scripts\sign-paket.ps1 -PfxPath D:\sertifikat\seruni22.pfx
# Mode PFX mengoper sandi lewat /p sehingga sandi terlihat di daftar proses selama
# signtool berjalan; untuk produksi pakai mode thumbprint (token/HSM).

[CmdletBinding(DefaultParameterSetName = "Thumb")]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Thumb")]
    [string]$Thumbprint,

    [Parameter(Mandatory = $true, ParameterSetName = "Pfx")]
    [string]$PfxPath,

    [Parameter(ParameterSetName = "Pfx")]
    [SecureString]$PfxPassword,

    # Menampilkan berkas yang akan ditandatangani lalu berhenti. Tidak butuh sertifikat
    # maupun signtool; dipakai untuk memastikan versi yang dipilih benar sebelum menandatangani.
    [Parameter(Mandatory = $true, ParameterSetName = "Daftar")]
    [switch]$DaftarSaja,

    [string]$Version,

    # Diarahkan ke salinan percobaan agar pengujian tidak menyentuh paket terbit.
    [string]$InstallerDir,

    [string]$PublishDir,

    [string]$TimestampUrl = "http://timestamp.digicert.com",

    [switch]$TermasukIsi,

    [string[]]$Files
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$installerDir = if ($InstallerDir) { (Resolve-Path -LiteralPath $InstallerDir).Path } else { Join-Path $repo "dist\installer" }
$publishDir = if ($PublishDir) { (Resolve-Path -LiteralPath $PublishDir).Path } else { Join-Path $repo "dist\publish-single" }
$logDir = Join-Path $repo "dist\build-logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# --- Versi paket yang ditandatangani -------------------------------------------
# Folder dist\installer bisa memuat paket dari beberapa versi sekaligus. Dulu skrip
# memilih berkas pertama menurut abjad, sehingga v1.2.10 bisa ditandatangani padahal
# yang dimaksud v1.2.12. Versi kini diambil dari laporan build, lalu dari proyek host.
if (-not $Version) {
    $infoJson = Join-Path $installerDir "BUILD-INFO.json"
    if (Test-Path $infoJson) {
        try { $Version = (Get-Content -Raw $infoJson | ConvertFrom-Json).versi } catch { }
    }
}
if (-not $Version) {
    $hostProject = Join-Path $repo "src\Host.Service\BakeryDesktop.Host.Service.csproj"
    if (Test-Path $hostProject) {
        $Version = ([xml](Get-Content -Raw $hostProject)).Project.PropertyGroup.Version |
            Where-Object { $_ } | Select-Object -First 1
        if ($Version) { $Version = $Version.Trim() }
    }
}
if (-not $Version) { throw "Versi paket tidak bisa ditentukan. Jalankan dengan -Version x.y.z." }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versi '$Version' bukan bentuk x.y.z." }

# --- Berkas yang ditandatangani ------------------------------------------------
if (-not $Files -or $Files.Count -eq 0) {
    $Files = @()
    $jalurExe = Join-Path $installerDir "Seruni22Bakery_Setup_v$Version.exe"
    $jalurMsi = Join-Path $installerDir "Seruni22Bakery_v$($Version)_x64.msi"
    if (Test-Path $jalurExe) { $Files += $jalurExe }
    if (Test-Path $jalurMsi) { $Files += $jalurMsi }
    if ($TermasukIsi) {
        $Files += @(
            (Join-Path $publishDir "Seruni22Bakery.exe"),
            (Join-Path $publishDir "Seruni22Bakery.Host.exe")
        )
    }
}
if ($Files.Count -eq 0) {
    throw "Tidak ada berkas $Version untuk ditandatangani di $installerDir (diharapkan Seruni22Bakery_Setup_v$Version.exe atau Seruni22Bakery_v${Version}_x64.msi)."
}

if ($DaftarSaja) {
    Write-Host "Versi          : $Version"
    Write-Host "Folder         : $installerDir"
    Write-Host "Berkas        :"
    foreach ($f in $Files) {
        $ada = Test-Path $f
        Write-Host ("  [{0}] {1}" -f $(if ($ada) { 'ADA' } else { 'HILANG' }), $f)
        if (-not $ada) { throw "Berkas tidak ditemukan: $f" }
    }
    exit 0
}

# signtool menulis sebagian pesan ke stderr. Dengan $ErrorActionPreference = "Stop",
# PowerShell 5.1 mengubah baris stderr itu menjadi galat yang menghentikan skrip walau
# signtool sukses. Preferensi dilonggarkan hanya selama pemanggilan; kode keluar tetap
# diperiksa pemanggilnya.
function Jalankan-Signtool([string]$LogPath, [string[]]$Argumen) {
    $sebelum = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $signtool @Argumen 2>&1 |
            ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.ToString() } else { $_ } } |
            Tee-Object -FilePath $LogPath -Append | Out-Null
        $kode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $sebelum
    }
    return $kode
}

function Cari-Signtool {
    $kandidat = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (-not $kandidat) {
        $kandidat = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" `
            -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $kandidat) { throw "signtool.exe tidak ditemukan. Pasang Windows SDK (Signing Tools)." }
    return $kandidat
}

function Punya-EkuCodeSigning($cert) {
    $eku = $cert.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.37" }
    if (-not $eku) { return $false }
    foreach ($o in $eku.EnhancedKeyUsages) {
        if ($o.Value -eq "1.3.6.1.5.5.7.3.3") { return $true }
    }
    return $false
}

function Validasi-Sertifikat($cert, $asal) {
    if (-not $cert.HasPrivateKey) { throw "Sertifikat $asal tidak punya private key yang bisa diakses." }
    if (-not (Punya-EkuCodeSigning $cert)) { throw "Sertifikat $asal tidak punya EKU Code Signing (1.3.6.1.5.5.7.3.3)." }
    if ($cert.NotAfter -lt (Get-Date)) { throw "Sertifikat $asal sudah kedaluwarsa ($($cert.NotAfter.ToString('yyyy-MM-dd')))." }
}

$signtool = Cari-Signtool

# --- Sertifikat penanda tangan -------------------------------------------------
$argSert = @()
$infoSert = ""
if ($PSCmdlet.ParameterSetName -eq "Thumb") {
    $th = $Thumbprint -replace '[^0-9A-Fa-f]', ''
    if ($th.Length -ne 40) { throw "Thumbprint '$Thumbprint' tidak sah; harus 40 digit heksadesimal SHA-1." }
    $cert = $null
    foreach ($store in @("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")) {
        $cert = Get-ChildItem $store -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $th } | Select-Object -First 1
        if ($cert) { break }
    }
    if (-not $cert) { throw "Sertifikat dengan thumbprint $th tidak ditemukan di CurrentUser\My atau LocalMachine\My." }
    Validasi-Sertifikat $cert "thumbprint $th"
    $argSert = @("/sha1", $th)
    $infoSert = $cert.Subject
}
else {
    if (-not (Test-Path $PfxPath)) { throw "Berkas PFX tidak ditemukan: $PfxPath" }
    if (-not $PfxPassword) {
        $PfxPassword = Read-Host -AsSecureString "Sandi PFX $([System.IO.Path]::GetFileName($PfxPath))"
    }
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword)
    try { $sandi = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    $uji = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
    $uji.Import($PfxPath, $sandi, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    Validasi-Sertifikat $uji "PFX"
    $infoSert = $uji.Subject
    $uji.Dispose()
    $argSert = @("/f", (Resolve-Path $PfxPath).Path, "/p", $sandi)
}

# --- Berkas yang ditandatangani ------------------------------------------------
# (dipilih di atas, sebelum sertifikat diperiksa, agar -DaftarSaja tidak butuh sertifikat)

$cap = Get-Date -Format "yyyyMMdd-HHmmss"
$log = Join-Path $logDir "signing-$cap.log"
$baris = New-Object System.Collections.Generic.List[string]
$baris.Add("Waktu          : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
$baris.Add("Penanda tangan : $infoSert")
$baris.Add("Timestamp      : $(if ($TimestampUrl) { $TimestampUrl } else { '(tidak dipakai)' })")
$baris.Add("Signtool       : $signtool")
$baris.Add("")

$gagal = 0
# Hasil per berkas dipakai untuk memperbarui checksum dan laporan build: keduanya
# menunjuk hash lama, sehingga tanpa pembaruan paket yang sudah ditandatangani akan
# terlihat "tidak cocok" oleh validate-paket.ps1.
$tanda = [ordered]@{}
foreach ($f in $Files) {
    if (-not (Test-Path $f)) { throw "Berkas tidak ditemukan: $f" }
    $hashSebelum = (Get-FileHash -Algorithm SHA256 $f).Hash.ToLowerInvariant()

    Write-Host "== Tanda tangan $(Split-Path -Leaf $f) =="
    $argSign = @("sign", "/fd", "SHA256")
    if ($TimestampUrl) { $argSign += @("/tr", $TimestampUrl, "/td", "SHA256") }
    $argSign += $argSert
    $argSign += (Resolve-Path $f).Path
    $kodeSign = Jalankan-Signtool $log $argSign
    if ($kodeSign -ne 0) {
        $baris.Add("GAGAL  $(Split-Path -Leaf $f): signtool sign keluar $kodeSign")
        $gagal++
        continue
    }

    $hasil = Get-AuthenticodeSignature $f
    $hashSesudah = (Get-FileHash -Algorithm SHA256 $f).Hash.ToLowerInvariant()
    $stempel = ""
    if ($hasil.TimeStamperCertificate) { $stempel = $hasil.TimeStamperCertificate.Subject }

    Write-Host "== Verifikasi $(Split-Path -Leaf $f) =="
    $kodeVerifikasi = Jalankan-Signtool $log @("verify", "/pa", "/v", $f)

    $baris.Add("$(Split-Path -Leaf $f)")
    $baris.Add("  Status       : $($hasil.Status)")
    $baris.Add("  Penanda      : $($hasil.SignerCertificate.Subject)")
    $baris.Add("  Penerbit     : $($hasil.SignerCertificate.Issuer)")
    $baris.Add("  Serial       : $($hasil.SignerCertificate.SerialNumber)")
    $baris.Add("  Berlaku s/d  : $($hasil.SignerCertificate.NotAfter.ToString('yyyy-MM-dd'))")
    $baris.Add("  Timestamp    : $(if ($stempel) { $stempel } else { 'TIDAK ADA' })")
    $baris.Add("  SHA-256      : $hashSebelum -> $hashSesudah")
    $baris.Add("  Verifikasi   : $(if ($kodeVerifikasi -eq 0) { 'PA: lulus' } else { "PA: keluar $kodeVerifikasi (lihat log)" })")
    $baris.Add("")

    $namaBerkas = Split-Path -Leaf $f
    $tanda[$namaBerkas] = [pscustomobject]@{
        Nama   = $namaBerkas
        Jalur  = (Resolve-Path $f).Path
        Ukuran = (Get-Item $f).Length
        Sha256 = $hashSesudah
        Status = [string]$hasil.Status
    }

    if ($hasil.Status -ne "Valid") { $gagal++ }
}

$baris.Add("")

# SHA256SUMS harus mengikuti artefak yang sudah berubah isinya. Ditulis hanya untuk
# EXE+MSI versi ini (sama seperti build-installer/build-msi), memakai akhir baris LF:
# validator menolak checksum ber-CRLF karena `sha256sum -c` di Git Bash gagal membacanya.
$sumPath = Join-Path $installerDir "SHA256SUMS.txt"
$namaArtefak = @(
    "Seruni22Bakery_Setup_v$Version.exe",
    "Seruni22Bakery_v$($Version)_x64.msi"
)
$barisSum = @()
foreach ($n in $namaArtefak) {
    $jalur = Join-Path $installerDir $n
    if (Test-Path $jalur) {
        $h = (Get-FileHash -Algorithm SHA256 $jalur).Hash.ToLowerInvariant()
        $barisSum += "$h *$n"
    }
}
if ($barisSum.Count -gt 0) {
    [IO.File]::WriteAllText($sumPath, (($barisSum -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
    Write-Host "SHA256SUMS.txt diperbarui untuk EXE/MSI versi $Version."
}

# BUILD-INFO.txt (dibuat build-installer) memuat hash dan status tanda tangan lama.
$buildInfoTxt = Join-Path $installerDir "BUILD-INFO.txt"
$namaExeSetup = "Seruni22Bakery_Setup_v$Version.exe"
$statusTanda = if ($gagal -eq 0) { $infoSert } else { "TIDAK LENGKAP: $gagal berkas gagal/tidak valid" }
if (Test-Path $buildInfoTxt) {
    $teks = [IO.File]::ReadAllText($buildInfoTxt)
    if ($tanda.Contains($namaExeSetup)) {
        $t = $tanda[$namaExeSetup]
        $teks = [regex]::Replace($teks, '(?m)^SHA-256 setup\s*:.*$', "SHA-256 setup  : $($t.Sha256)")
        $teks = [regex]::Replace($teks, '(?m)^Ukuran\s*:.*$', "Ukuran         : $($t.Ukuran) byte")
    }
    $teks = [regex]::Replace($teks, '(?m)^Tanda tangan\s*:.*$', "Tanda tangan   : $statusTanda")
    [IO.File]::WriteAllText($buildInfoTxt, $teks, (New-Object Text.UTF8Encoding($false)))
    Write-Host "BUILD-INFO.txt diperbarui (hash setup + status tanda tangan)."
}

# BUILD-INFO.json (dibuat build-pro) dipakai validate-paket untuk mencocokkan hash.
# Tanpa pembaruan, paket yang baru ditandatangani selalu dinyatakan "hash tidak sama".
$infoJson = Join-Path $installerDir "BUILD-INFO.json"
if ((Test-Path $infoJson) -and $tanda.Count -gt 0) {
    try {
        $info = Get-Content -Path $infoJson -Raw | ConvertFrom-Json
        $berubah = 0
        foreach ($a in @($info.artefak)) {
            if (-not $a) { continue }
            $t = $tanda[[string]$a.nama]
            if ($t) {
                $a.ukuran = $t.Ukuran
                $a.sha256 = $t.Sha256
                $a.tandaTangan = $t.Status
                $berubah++
            }
        }
        if ($berubah -gt 0) {
            $info | Add-Member -NotePropertyName tandaTangan -NotePropertyValue ([pscustomobject]@{
                waktu   = (Get-Date).ToString("o")
                penanda = $infoSert
                berkas  = @($tanda.Keys)
            }) -Force
            [IO.File]::WriteAllText($infoJson, ($info | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
            Write-Host "BUILD-INFO.json diperbarui untuk $berubah artefak bertanda tangan."
        }
    } catch {
        Add-Content -Path $log -Encoding UTF8 -Value "GAGAL  BUILD-INFO.json tidak bisa diperbarui: $($_.Exception.Message)"
        $gagal++
    }
}

$baris.Add("Ringkasan      : $(if ($gagal -eq 0) { 'semua berkas bertanda tangan dan rantainya valid' } else { "$gagal berkas gagal/tidak valid" })")
# Ditambahkan ke log yang sama: log ini juga memuat keluaran mentah signtool.
$baris | Add-Content -Path $log -Encoding UTF8

Write-Host ""
Get-Content $log | Write-Host
Write-Host "Log lengkap: $log"
if ($gagal -gt 0) { exit 1 }
