# Merakit kit uji pasang/peningkatan 1.2.9 yang siap disalin ke mesin Windows lain.
# Keluaran: folder berisi dua MSI, dua EXE, manifes payload, skrip tamu, peluncur,
# panduan, dan checksum. Skrip ini hanya menyalin berkas yang sudah ada; tidak
# membangun ulang paket dan tidak menjalankan installer.

[CmdletBinding()]
param(
    [string]$OutputDir = 'C:\tmp\kit-uji-129',

    # Menimpa kit lama harus disengaja: folder kit boleh memuat 'bukti' hasil uji
    # sebelumnya, dan menghapusnya tanpa izin berarti bukti itu hilang.
    [switch]$Timpa
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sumber = Join-Path $repo 'docs\paket-bukti-2026-10-31\uji-129'
$dist = Join-Path $repo 'dist\installer'

$paket = @(
    'Seruni22Bakery_Setup_v1.2.7.exe',
    'Seruni22Bakery_Setup_v1.2.9.exe',
    'Seruni22Bakery_v1.2.7_x64.msi',
    'Seruni22Bakery_v1.2.9_x64.msi'
)
$skrip = @(
    'uji-129.ps1',
    'run-sandbox.ps1',
    'hash-publish-129.txt',
    'PANDUAN-KIT.md'
)

foreach ($f in $paket) {
    if (-not (Test-Path (Join-Path $dist $f))) { throw "Paket tidak ditemukan: $dist\$f" }
}
foreach ($f in $skrip) {
    if (-not (Test-Path (Join-Path $sumber $f))) { throw "Berkas harness tidak ditemukan: $sumber\$f" }
}

$boleh = @($paket) + @('uji-129.ps1','run-sandbox.ps1','hash-publish-129.txt','README-UJI.md','KIT-SHA256SUMS.txt','bukti','runner.log','uji-jalan.wsb')
if (Test-Path $OutputDir) {
    $isi = @(Get-ChildItem $OutputDir -Force)
    $asing = @($isi | Where-Object { $_.Name -notin $boleh })
    if ($asing.Count -gt 0) {
        throw "Folder tujuan berisi berkas asing: $($asing.Name -join ', '). Pilih folder lain atau kosongkan dulu."
    }
    if ($isi.Count -gt 0 -and -not $Timpa) {
        throw "Folder tujuan sudah berisi kit lama ($($isi.Count) entri, termasuk kemungkinan folder 'bukti'). Pakai -Timpa untuk menimpanya."
    }
    # Hapus per entri tanpa menelan galat: kegagalan menghapus (berkas terkunci)
    # tidak boleh berlanjut menjadi kit campuran tanpa peringatan.
    foreach ($item in $isi) {
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

foreach ($f in $paket) { Copy-Item (Join-Path $dist $f) (Join-Path $OutputDir $f) }
foreach ($f in @('uji-129.ps1','run-sandbox.ps1','hash-publish-129.txt')) {
    Copy-Item (Join-Path $sumber $f) (Join-Path $OutputDir $f)
}
Copy-Item (Join-Path $sumber 'PANDUAN-KIT.md') (Join-Path $OutputDir 'README-UJI.md')

# Checksum memakai LF supaya `sha256sum -c` di Linux/Git Bash juga lolos.
$baris = foreach ($f in @($paket + 'uji-129.ps1','run-sandbox.ps1','hash-publish-129.txt','README-UJI.md')) {
    $h = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $OutputDir $f)).Hash.ToLowerInvariant()
    "$h *$f"
}
$isiSum = ($baris -join "`n") + "`n"
[IO.File]::WriteAllText((Join-Path $OutputDir 'KIT-SHA256SUMS.txt'), $isiSum, (New-Object Text.UTF8Encoding($false)))

$total = (Get-ChildItem $OutputDir -File | Measure-Object Length -Sum).Sum
Write-Host "Kit  : $OutputDir"
Write-Host ("Berkas: " + (Get-ChildItem $OutputDir -File).Count + ", " + [math]::Round($total / 1MB, 1) + " MB")
Get-Content (Join-Path $OutputDir 'KIT-SHA256SUMS.txt') | ForEach-Object { Write-Host ("  " + $_) }
