# Build paket instalasi produksi Seruni 22 Bakery.
# Urutan: tes -> frontend produksi -> publish self-contained -> konfigurasi paket ->
# Inno Setup -> (opsional) tanda tangan -> checksum dan ringkasan.
# Gagal di langkah mana pun menghentikan skrip; tidak ada langkah yang ditelan.

[CmdletBinding()]
param(
    [string]$Version = "1.2.12",
    [switch]$SkipTests,
    [string]$SignThumbprint = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$frontendDir = Join-Path $repo "frontend"
$publishDir = Join-Path $repo "dist\publish-single"
$installerDir = Join-Path $repo "dist\installer"
$logDir = Join-Path $repo "dist\build-logs"
$hostProject = Join-Path $repo "src\Host.Service\BakeryDesktop.Host.Service.csproj"
$issFile = Join-Path $repo "build\installer\inno\setup.iss"
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$launcherSource = Join-Path $repo "build\installer\launcher\Seruni22BakeryLauncher.cs"
$launcherIcon = Join-Path $repo "build\installer\inno\app.ico"
$launcherRcTemplate = Join-Path $repo "build\installer\launcher\Seruni22BakeryLauncher.rc.in"

New-Item -ItemType Directory -Force -Path $logDir, $installerDir | Out-Null

# Alat bawaan Windows (npm, dotnet, ISCC) menulis informasi ke stderr. Dengan
# $ErrorActionPreference = "Stop" PowerShell mengubah baris stderr menjadi galat
# yang menghentikan skrip walau prosesnya sukses. Karena itu preferensi dilonggarkan
# hanya selama pemanggilan, lalu kode keluar tetap diperiksa dan menggagalkan skrip.
function Invoke-Alat([string]$Nama, [string]$LogPath, [scriptblock]$Aksi) {
    $sebelum = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        # Baris stderr diubah menjadi teks biasa supaya log tidak memuat blok
        # "NativeCommandError" yang terlihat seperti kegagalan padahal bukan.
        & $Aksi 2>&1 |
            ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.ToString() } else { $_ } } |
            Tee-Object -FilePath $LogPath
        $kode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $sebelum
    }
    if ($kode -ne 0) {
        throw "$Nama gagal dengan kode keluar $kode. Lihat log: $LogPath"
    }
}

Write-Host "== Pemeriksaan alat =="
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet tidak ditemukan di PATH." }
if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) { throw "npm tidak ditemukan di PATH." }
if (-not (Test-Path $iscc)) { throw "Inno Setup compiler tidak ditemukan: $iscc" }
if (-not (Test-Path $csc)) { throw "csc.exe .NET Framework tidak ditemukan: $csc" }

# Nama installer, versi host, dan versi berkas peluncur harus satu angka. Tanpa pemeriksaan ini
# paket bisa lahir sebagai 1.2.4 sementara host di dalamnya masih melaporkan versi lama.
$versiCsproj = ([xml](Get-Content -Raw $hostProject)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if ($versiCsproj -and $versiCsproj.Trim() -ne $Version) {
    throw "Versi parameter '$Version' tidak sama dengan <Version> di proyek host ('$($versiCsproj.Trim())')."
}

if (-not $SkipTests) {
    Write-Host "== Tes otomatis =="
    Invoke-Alat "Tes otomatis" (Join-Path $logDir "test.log") {
        dotnet test "BakeryDesktop.slnx" -c Release -m:1 -nodeReuse:false
    }
}

Write-Host "== Build frontend produksi =="
Push-Location $frontendDir
try {
    Invoke-Alat "Build frontend" (Join-Path $logDir "frontend-build.log") {
        npm.cmd run build
    }
}
finally {
    Pop-Location
}

Write-Host "== Publish host self-contained (win-x64) =="
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
Invoke-Alat "Publish host" (Join-Path $logDir "publish.log") {
    dotnet publish $hostProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -p:Version=$Version `
        -o $publishDir -m:1 -nodeReuse:false
}

$wajib = @("Seruni22Bakery.Host.exe", "Seruni22Bakery.Host.dll", "hostfxr.dll", "coreclr.dll", "appsettings.json")
foreach ($berkas in $wajib) {
    if (-not (Test-Path (Join-Path $publishDir $berkas))) {
        throw "Hasil publish tidak lengkap: $berkas tidak ditemukan."
    }
}
if (Test-Path (Join-Path $publishDir "appsettings.Development.json")) {
    throw "appsettings.Development.json tidak boleh ikut ke paket produksi."
}

Write-Host "== Salin aset web dan bangun peluncur =="
$wwwroot = Join-Path $publishDir "wwwroot"
if (Test-Path $wwwroot) { Remove-Item -Recurse -Force $wwwroot }
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $frontendDir "dist\*") -Destination $wwwroot -Recurse -Force

# Peluncur desktop dikompilasi dari sumber C# memakai csc .NET Framework bawaan
# Windows: WinExe tanpa jendela konsol dan tanpa ketergantungan VBScript, yang
# tidak selalu tersedia di Windows 11.
#
# Sumber daya versi dibuat lebih dulu dengan rc.exe Windows SDK: tanpa itu Exe
# peluncur tampil sebagai 0.0.0.0 di properti berkas, panel Program & Fitur, dan
# pemeriksaan versi oleh pengguna. Ikon ikut masuk ke sumber daya yang sama.
$rcTool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter rc.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -match '\\x64$' } |
    Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending |
    Select-Object -First 1
if (-not $rcTool) { throw "rc.exe Windows SDK (x64) tidak ditemukan; sumber daya versi peluncur tidak bisa dibuat." }

$bagianVersi = $Version.Split('.')
$versiQuad = (@($bagianVersi) + @('0', '0', '0', '0'))[0..3] -join ','
$launcherRc = Join-Path $env:TEMP "seruni-launcher-$Version.rc"
$launcherRes = Join-Path $env:TEMP "seruni-launcher-$Version.res"
$isiRc = Get-Content -Raw $launcherRcTemplate
# rc.exe membaca string sebagai literal C: garis miring terbalik tunggal di jalur ikon
# akan ditelan sebagai escape (mis. \a), jadi jalur harus digandakan.
$isiRc = $isiRc.Replace('@VERSI_QUAD@', $versiQuad).Replace('@VERSI@', $Version)
$isiRc = $isiRc.Replace('@IKON@', $launcherIcon.Replace('\', '\\'))
Set-Content -Path $launcherRc -Value $isiRc -Encoding ASCII
Remove-Item $launcherRes -Force -ErrorAction SilentlyContinue

Invoke-Alat "Sumber daya versi peluncur" (Join-Path $logDir "launcher-rc.log") {
    & $rcTool.FullName /nologo "/fo$launcherRes" $launcherRc
}
if (-not (Test-Path $launcherRes)) { throw "rc.exe tidak menghasilkan sumber daya peluncur: $launcherRes" }

Invoke-Alat "Kompilasi peluncur" (Join-Path $logDir "launcher-build.log") {
    & $csc -nologo -target:winexe -optimize+ "-win32res:$launcherRes" `
        "-out:$publishDir\Seruni22Bakery.exe" $launcherSource
}
if (-not (Test-Path (Join-Path $publishDir "Seruni22Bakery.exe"))) {
    throw "Peluncur desktop tidak dihasilkan."
}

$versiPeluncur = (Get-Item (Join-Path $publishDir "Seruni22Bakery.exe")).VersionInfo
if ($versiPeluncur.ProductVersion -ne $Version) {
    throw "Peluncur desktop melaporkan versi '$($versiPeluncur.ProductVersion)', seharusnya '$Version'."
}
Remove-Item $launcherRc, $launcherRes -Force -ErrorAction SilentlyContinue

Write-Host "== Kompilasi installer Inno Setup =="
Invoke-Alat "Kompilasi Inno Setup" (Join-Path $logDir "inno-build.log") {
    & $iscc "/DMyAppVersion=$Version" $issFile
}

$setup = Join-Path $installerDir "Seruni22Bakery_Setup_v$Version.exe"
if (-not (Test-Path $setup)) { throw "Installer tidak dihasilkan: $setup" }

$tandaTangan = "NotSigned (tidak ada sertifikat penandatanganan yang diberikan)"
if ($SignThumbprint -ne "") {
    Write-Host "== Tanda tangan Authenticode =="
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (-not $signtool) {
        $kandidat = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" `
            -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($kandidat) { $signtool = $kandidat.FullName }
    }
    if (-not $signtool) { throw "signtool.exe tidak ditemukan; paket tidak ditandatangani." }

    Invoke-Alat "Tanda tangan installer" (Join-Path $logDir "sign.log") {
        & $signtool sign /sha1 $SignThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $setup
    }
    Invoke-Alat "Verifikasi tanda tangan" (Join-Path $logDir "sign-verify.log") {
        & $signtool verify /pa $setup
    }
    $tandaTangan = "Signed"
}

Write-Host "== Checksum dan ringkasan =="
$hashSetup = (Get-FileHash -Algorithm SHA256 $setup).Hash.ToLowerInvariant()
$hashHost = (Get-FileHash -Algorithm SHA256 (Join-Path $publishDir "Seruni22Bakery.Host.dll")).Hash.ToLowerInvariant()
$hashLauncher = (Get-FileHash -Algorithm SHA256 (Join-Path $publishDir "Seruni22Bakery.exe")).Hash.ToLowerInvariant()
$hashIndex = (Get-FileHash -Algorithm SHA256 (Join-Path $wwwroot "index.html")).Hash.ToLowerInvariant()
$auth = (Get-AuthenticodeSignature $setup).Status

$ringkasan = @(
    "Versi          : $Version",
    "Installer      : $setup",
    "Ukuran         : $((Get-Item $setup).Length) byte",
    "SHA-256 setup  : $hashSetup",
    "SHA-256 host dll: $hashHost",
    "SHA-256 peluncur : $hashLauncher",
    "SHA-256 index  : $hashIndex",
    "Tanda tangan   : $auth",
    "Dibuat         : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
)
$ringkasan | Set-Content -Encoding UTF8 (Join-Path $installerDir "BUILD-INFO.txt")
"$hashSetup *Seruni22Bakery_Setup_v$Version.exe" |
    Set-Content -Encoding ASCII (Join-Path $installerDir "SHA256SUMS.txt")

$ringkasan | ForEach-Object { Write-Host $_ }
Write-Host "== Selesai =="
