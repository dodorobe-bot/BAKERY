# update-js-hash.ps1
# Reads the actual JS and CSS filenames from the published assets folder and updates Package.wxs
# Run this before building the WiX installer.
#
# Nama berkas ber-hash berubah setiap build. Dulu skrip ini hanya memperbarui JS, sehingga CSS
# yang berganti hash membuat MSI gagal dibangun (atau, lebih buruk, menunjuk berkas yang tidak ada).
# Sekarang keduanya diperbarui, dan jumlah berkas diperiksa supaya sisa build lama tidak terpilih.

$ErrorActionPreference = 'Stop'

$assetsDir = Join-Path $PSScriptRoot "..\..\..\dist\publish-single\wwwroot\assets"
$wxsFile   = Join-Path $PSScriptRoot "Package.wxs"

$content = Get-Content $wxsFile -Raw

foreach ($ext in @('js', 'css')) {
    $files = @(Get-ChildItem -Path $assetsDir -Filter "index-*.$ext")
    if ($files.Count -ne 1) {
        Write-Error ("Diharapkan tepat satu index-*.$ext di $assetsDir, ditemukan $($files.Count). " +
            "Bersihkan folder assets sebelum publish.")
        exit 1
    }

    $name = $files[0].Name
    $updated = $content -replace "index-[^`"]+\.$ext", $name
    if ($content -eq $updated) {
        Write-Host "Package.wxs sudah menunjuk $name - tidak berubah."
    } else {
        $content = $updated
        Write-Host "Package.wxs diperbarui ke $name"
    }
}

Set-Content $wxsFile $content -NoNewline
