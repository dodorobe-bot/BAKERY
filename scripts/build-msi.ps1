# Build paket MSI (WiX) Seruni 22 Bakery.
# Prasyarat: dist\publish-single sudah berisi hasil publish self-contained,
# artinya scripts\build-installer.ps1 sudah dijalankan. Skrip ini tidak mem-build
# ulang biner; ia hanya membungkus isi folder publish yang ada.
# -arch x64 dikunci di sini: tanpa flag itu WiX membuat paket x86 dan aplikasi
# terpasang di "Program Files (x86)".

[CmdletBinding()]
param(
    [string]$Version = "1.2.10",
    [string]$OutputPath = "",
    # Folder hasil publish yang dipaketkan. Nilai ini dulu ditulis mati di Package.wxs
    # sebagai jalur absolut mesin pengembang, sehingga MSI hanya bisa dibangun di satu
    # komputer. Sekarang selalu diteruskan dari sini.
    [string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$publishDir = if ($PublishDir) { $PublishDir } else { Join-Path $repo "dist\publish-single" }
$installerDir = Join-Path $repo "dist\installer"
$logDir = Join-Path $repo "dist\build-logs"
$hostProject = Join-Path $repo "src\Host.Service\BakeryDesktop.Host.Service.csproj"
$wxs = Join-Path $repo "build\installer\wix\Package.wxs"
$wix = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"

if (-not (Test-Path (Join-Path $publishDir "Seruni22Bakery.Host.exe"))) {
    throw "Folder publish belum siap: $publishDir. Jalankan scripts\build-installer.ps1 lebih dulu."
}
if (-not (Test-Path $wix)) {
    throw "WiX tidak ditemukan: $wix. Pasang dengan: dotnet tool install --global wix"
}

# Versi MSI, versi host, dan nama berkas harus satu angka.
$versiCsproj = ([xml](Get-Content -Raw $hostProject)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if ($versiCsproj -and $versiCsproj.Trim() -ne $Version) {
    throw "Versi parameter '$Version' tidak sama dengan <Version> di proyek host ('$($versiCsproj.Trim())')."
}

New-Item -ItemType Directory -Force -Path $installerDir, $logDir | Out-Null
if (-not $OutputPath) {
    $OutputPath = Join-Path $installerDir "Seruni22Bakery_v$($Version)_x64.msi"
}
$pdbPath = Join-Path $logDir "Seruni22Bakery_v$($Version)_x64.wixpdb"
$log = Join-Path $logDir "msi-build.log"

Write-Host "== Build MSI $Version (x64) =="
# Log selalu dibuat, termasuk baris perintahnya: wix yang sukses tidak mencetak
# apa pun, dan tanpa header ini log bisa tidak terbentuk sama sekali.
"wix build $wxs -arch x64 -ext WixToolset.UI.wixext -d ProductVersion=$Version -d PublishDir=$publishDir -o $OutputPath -pdb $pdbPath" |
    Set-Content -Path $log -Encoding ASCII
$sebelum = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& $wix build $wxs -arch x64 -ext WixToolset.UI.wixext -d "ProductVersion=$Version" -d "PublishDir=$publishDir" -o $OutputPath -pdb $pdbPath 2>&1 |
    Tee-Object -FilePath $log -Append
$kode = $LASTEXITCODE
$ErrorActionPreference = $sebelum
if ($kode -ne 0) {
    throw "Build MSI gagal dengan kode keluar $kode. Lihat log: $log"
}
if (-not (Test-Path $OutputPath)) {
    throw "Build MSI selesai tanpa menghasilkan berkas: $OutputPath"
}

$berkas = Get-Item $OutputPath
$sha = (Get-FileHash -Algorithm SHA256 -Path $OutputPath).Hash.ToLowerInvariant()
$tanda = (Get-AuthenticodeSignature -FilePath $OutputPath).Status

Write-Host ""
Write-Host "MSI        : $($berkas.FullName)"
Write-Host "Ukuran     : $($berkas.Length) byte"
Write-Host "SHA-256    : $sha"
Write-Host "Tanda tangan: $tanda"
Write-Host "Log        : $log"
