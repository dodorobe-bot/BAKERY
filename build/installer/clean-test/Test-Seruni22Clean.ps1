<#
  Uji pasang Seruni 22 Bakery 1.2.3 pada mesin Windows yang bersih.
  Dirancang untuk dijalankan TANPA hak administrator, langsung oleh pengguna
  yang akan memakai aplikasi. Semua langkah dicatat ke evidence\uJI-*.json.

  Pemakaian di mesin bersih (salin folder ini + installer ke mesin itu):
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Test-Seruni22Clean.ps1

  Opsi:
    -Installer <path>    default: .\Seruni22Bakery_Setup_v1.2.3.exe
    -Evidence <folder>   default: .\evidence
    -NewPassword <teks>  sandi admin pengganti (sintetis, hanya mesin uji)
    -Isolated            pakai folder + basis data sementara (untuk mesin ini,
                         yang sudah punya instalasi nyata / data nyata)
    -KeepInstalled       jangan copot di akhir (untuk pemeriksaan manual)

  Skrip berhenti dengan kode 1 bila ada pemeriksaan yang gagal.
  Tidak ada data pengguna yang dihapus: folder data %LOCALAPPDATA%\Seruni22Bakery
  hanya dibaca setelah uninstall.
#>
param(
    [string]$Installer = (Join-Path $PSScriptRoot "Seruni22Bakery_Setup_v1.2.3.exe"),
    [string]$Evidence = (Join-Path $PSScriptRoot "evidence"),
    [string]$NewPassword = "KasirUji2026#Lokal",
    [switch]$Isolated,
    [switch]$KeepInstalled
)

$ErrorActionPreference = "Stop"
$expectedHash = "28680DFCD281BC859AB281535D0FB0BB2709E75129B03980EFF8DEA96AD24E83"
$base = "http://127.0.0.1:5000"
$hasil = New-Object System.Collections.ArrayList
$mulai = Get-Date
$tag = Get-Date -Format "yyyyMMdd-HHmmss"

New-Item -ItemType Directory -Force -Path $Evidence | Out-Null

function Cek([string]$nama, [bool]$lulus, [string]$detail) {
    [void]$hasil.Add([pscustomobject]@{ pemeriksaan = $nama; status = $(if ($lulus) { "PASS" } else { "FAIL" }); detail = $detail })
    $tag = if ($lulus) { "[PASS]" } else { "[FAIL]" }
    "$tag $nama :: $detail"
}

function Catat([string]$nama, [string]$detail) {
    [void]$hasil.Add([pscustomobject]@{ pemeriksaan = $nama; status = "INFO"; detail = $detail })
    "[INFO] $nama :: $detail"
}

function LogBaru([string]$folder) {
    if (-not (Test-Path $folder)) { return $null }
    Get-ChildItem $folder -Filter "seruni-*.log" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

function TungguHealth {
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        try { return Invoke-RestMethod "$base/health" -TimeoutSec 2 } catch { }
    }
    return $null
}

function Login([string]$sandi) {
    Invoke-RestMethod -Method Post "$base/api/auth/login" -ContentType "application/json" `
        -Body (@{ username = "admin"; password = $sandi } | ConvertTo-Json)
}

function HentikanHost([string]$folder) {
    Get-Process Seruni22Bakery.Host -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$folder*" } | ForEach-Object { Stop-Process -Id $_.Id -Force }
    Start-Sleep -Seconds 2
}

function HashBerkas([string]$path) {
    if (-not (Test-Path $path)) { return "TIDAK-ADA" }
    $i = Get-Item $path
    "$((Get-FileHash $path -Algorithm SHA256).Hash)|$($i.Length)"
}

try {
    "$("=" * 70)"
    "UJI MESIN BERSIH - Seruni 22 Bakery 1.2.3  ($(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))"
    "$("=" * 70)"

    # ---- 0. Lingkungan ----
    $os = Get-CimInstance Win32_OperatingSystem
    $ram = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 2)
    Catat "Sistem" "$($os.Caption) build $($os.BuildNumber); RAM $ram GB; arsitektur $($os.OSArchitecture)"
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    Catat ".NET di PATH" $(if ($dotnet) { $dotnet } else { "tidak ada (diharapkan: aplikasi self-contained)" })
    $edge = Get-Item "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe" -ErrorAction SilentlyContinue
    Cek "Peramban Edge tersedia" ($null -ne $edge) $(if ($edge) { "versi $($edge.VersionInfo.ProductVersion)" } else { "Edge tidak ditemukan" })
    $browser = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice" -ErrorAction SilentlyContinue).ProgId
    Catat "Peramban default" "$browser"

    $terpasang = @(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like "Seruni 22 Bakery*" })
    $dataDefault = Join-Path $env:LOCALAPPDATA "Seruni22Bakery"
    $kunciPulih = $null
    if (-not $Isolated) {
        Cek "Belum ada instalasi sebelumnya" ($terpasang.Count -eq 0) "entri HKCU=$($terpasang.Count)"
        Cek "Belum ada data pengguna sebelumnya" (-not (Test-Path $dataDefault)) "folder $dataDefault"
    } else {
        Catat "Mode terisolasi" "instalasi + data sementara; tidak menyentuh data nyata"
        # Instalasi uji memakai AppId yang sama dengan instalasi nyata. Ekspor dulu
        # catatan uninstall milik instalasi nyata supaya bisa dikembalikan setelah copot.
        if ($terpasang.Count -gt 0) {
            $kunci = "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\$($terpasang[0].PSChildName)"
            $kunciPulih = Join-Path $Evidence "uninstall-key-pulih-$tag.reg"
            & reg.exe export $kunci $kunciPulih /y | Out-Null
            Catat "Catatan uninstall instalasi nyata diekspor" $kunciPulih
        }
    }
    $portDipakai = Get-NetTCPConnection -State Listen -LocalPort 5000 -ErrorAction SilentlyContinue
    Cek "Port 5000 bebas" ($null -eq $portDipakai) $(if ($portDipakai) { "dipakai PID $($portDipakai.OwningProcess -join ',')" } else { "bebas" })

    # ---- 1. Berkas installer ----
    Cek "Installer ada" (Test-Path $Installer) $Installer
    $hash = (Get-FileHash $Installer -Algorithm SHA256).Hash
    Cek "SHA-256 sesuai artefak rilis" ($hash -eq $expectedHash) $hash
    $sig = (Get-AuthenticodeSignature $Installer).Status
    Catat "Tanda tangan Authenticode" "$sig"

    $tag = Get-Date -Format "yyyyMMdd-HHmmss"
    $dir = if ($Isolated) { Join-Path $env:TEMP "seruni-clean-$tag" } else { "" }
    $data = if ($Isolated) { Join-Path $env:TEMP "seruni-clean-data-$tag" } else { $dataDefault }
    if ($Isolated) { New-Item -ItemType Directory -Force -Path $data | Out-Null }

    # ---- 2. Pasang ----
    $arg = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOICONS", "/TASKS=",
        "/LOG=$(Join-Path $Evidence "install-$tag.log")")
    if ($Isolated) { $arg += "/DIR=$dir" }
    $p = Start-Process -FilePath $Installer -ArgumentList $arg -Wait -PassThru
    Cek "Installer selesai" ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"

    $reg = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like "Seruni 22 Bakery*" } | Select-Object -First 1
    Cek "Entri uninstall terdaftar" ($null -ne $reg) $(if ($reg) { "$($reg.DisplayName) $($reg.DisplayVersion) / $($reg.Publisher)" } else { "-" })
    if ($reg) { $dir = $reg.InstallLocation.TrimEnd("\") }
    Cek "Versi terdaftar 1.2.3" ($reg.DisplayVersion -eq "1.2.3") "$($reg.DisplayVersion)"

    foreach ($w in "Seruni22Bakery.Host.exe", "Seruni22Bakery.Host.dll", "hostfxr.dll", "coreclr.dll",
        "appsettings.json", "Seruni22Bakery.exe", "wwwroot\index.html", "app.ico") {
        Cek "Berkas paket $w" (Test-Path (Join-Path $dir $w)) "ada"
    }
    Cek "Tanpa appsettings.Development.json" (-not (Test-Path (Join-Path $dir "appsettings.Development.json"))) "tidak ada"
    Cek "Tanpa PDB" (@(Get-ChildItem $dir -Filter *.pdb -ErrorAction SilentlyContinue).Count -eq 0) "0 pdb"
    Catat "Jumlah berkas terpasang" "$(@(Get-ChildItem $dir -Recurse -File).Count)"

    # ---- 3. Jalankan lewat peluncur / host ----
    $hostProc = $null
    if (-not $Isolated) {
        Cek "Peluncur mandiri tanpa VBScript" (-not (Test-Path (Join-Path $dir "Seruni22Bakery.vbs"))) "tidak ada berkas .vbs"
        $env:SERUNI_LAUNCH_NO_BROWSER = "1"
        $env:SERUNI_LAUNCH_QUIET = "1"
        $t0 = Get-Date
        # Tanpa redirect: bila stdio dialihkan, PowerShell 5.1 dapat mengembalikan
        # ExitCode kosong. Pesan galat peluncur dibaca dari launcher-error.txt.
        $lp = Start-Process -FilePath (Join-Path $dir "Seruni22Bakery.exe") -PassThru
        # Peluncur keluar sendiri setelah host menjawab /health; batas 2 menit menjaga uji tidak macet.
        if (-not $lp.WaitForExit(120000)) { try { $lp.Kill() } catch { } }
        $null = $lp.WaitForExit()
        $detik = [math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
        $berkasGalatLauncher = Join-Path $data "logs\launcher-error.txt"
        $galatPeluncur = ""
        if ((Test-Path $berkasGalatLauncher) -and ($lp.ExitCode -ne 0)) {
            $teks = Get-Content $berkasGalatLauncher -Raw
            if ($null -ne $teks) { $galatPeluncur = $teks.Trim() }
        }
        $rincian = "exit=$($lp.ExitCode), $detik detik"
        if ($galatPeluncur) { $rincian += " | $galatPeluncur" }
        Cek "Peluncur exit 0" ($lp.HasExited -and $lp.ExitCode -eq 0) $rincian
        Cek "Tanpa pesan galat peluncur" (-not (Test-Path $berkasGalatLauncher)) $berkasGalatLauncher
        Remove-Item Env:SERUNI_LAUNCH_NO_BROWSER, Env:SERUNI_LAUNCH_QUIET
    } else {
        $hostProc = Start-Process -FilePath (Join-Path $dir "Seruni22Bakery.Host.exe") -ArgumentList @(
            "--urls", $base, "--Database:Path=$data\seruni.db", "--Jwt:SigningKeyFile=$data\jwt.key") -PassThru
    }

    $health = TungguHealth
    Cek "Host menjawab /health" ($null -ne $health) $(if ($health) { $health | ConvertTo-Json -Compress } else { "tidak menjawab 30 detik" })
    Cek "Versi aplikasi 1.2.3" ($health.version -eq "1.2.3") "$($health.version)"

    # Mode terisolasi memakai --Database:Path eksplisit; mode biasa memakai nama
    # bawaan aplikasi di folder data pengguna.
    $dbPath = if ($Isolated) { Join-Path $data "seruni.db" } else { Join-Path $data "seruni22bakery.db" }
    Cek "Basis data dibuat" (Test-Path $dbPath) $dbPath
    Cek "Kunci JWT dibuat" (Test-Path (Join-Path $data "jwt.key")) "jwt.key"

    # ---- 4. Alur kritis ----
    $l = Login "admin123"
    Cek "Login awal diterima" ($null -ne $l.token) "role=$($l.role) mustChange=$($l.mustChangePassword)"
    $h1 = @{ Authorization = "Bearer $($l.token)" }
    try {
        Invoke-WebRequest "$base/api/dashboard" -Headers $h1 -UseBasicParsing | Out-Null
        Cek "Gerbang ganti sandi menolak dashboard" $false "dashboard menjawab 200 sebelum ganti sandi"
    } catch {
        Cek "Gerbang ganti sandi menolak dashboard" ($_.Exception.Response.StatusCode.value__ -eq 403) "HTTP $($_.Exception.Response.StatusCode.value__)"
    }
    Invoke-RestMethod -Method Post "$base/api/auth/change-password" -Headers $h1 -ContentType "application/json" `
        -Body (@{ currentPassword = "admin123"; newPassword = $NewPassword } | ConvertTo-Json) | Out-Null
    $l2 = Login $NewPassword
    Cek "Sandi baru berlaku" ($l2.mustChangePassword -eq $false) "mustChange=$($l2.mustChangePassword)"
    $h = @{ Authorization = "Bearer $($l2.token)" }

    $sc = Invoke-RestMethod "$base/api/system/self-check" -Headers $h
    Cek "Periksa-diri basis data" ($sc.status -eq "ok" -and $sc.jumlahGagal -eq 0) "status=$($sc.status) jumlah=$($sc.jumlahPemeriksaan) gagal=$($sc.jumlahGagal)"

    $o = Invoke-RestMethod -Method Post "$base/api/orders" -Headers $h -ContentType "application/json" -Body (@{
        customerName = "Pelanggan Uji Mesin Bersih"; phone = "0800000009"
        description = "Pesanan sintetis uji pasang"; pickupDate = "2026-11-10"
        totalAmount = 85000; downPayment = 25000; productTemplateId = $null
        notes = "data uji"; quantity = 2; idempotencyKey = "uji-mesin-bersih-$tag"
    } | ConvertTo-Json)
    $daftar = Invoke-RestMethod "$base/api/orders" -Headers $h
    $tersimpan = @($daftar | Where-Object { $_.id -eq $o.id })
    Cek "Pesanan tersimpan" ($tersimpan.Count -eq 1) "id=$($o.id) nomor=$($tersimpan[0].orderNumber) total=$($tersimpan[0].totalAmount) qty=$($tersimpan[0].quantity)"

    $pdf = Invoke-WebRequest "$base/api/export/orders?format=pdf" -Headers $h -UseBasicParsing
    [IO.File]::WriteAllBytes((Join-Path $Evidence "orders-$tag.pdf"), $pdf.Content)
    Cek "Ekspor PDF" ($pdf.StatusCode -eq 200 -and $pdf.RawContentLength -gt 5000) "HTTP $($pdf.StatusCode), $($pdf.RawContentLength) bita"
    $xl = Invoke-WebRequest "$base/api/export/orders?format=xlsx" -Headers $h -UseBasicParsing
    [IO.File]::WriteAllBytes((Join-Path $Evidence "orders-$tag.xlsx"), $xl.Content)
    Cek "Ekspor Excel" ($xl.StatusCode -eq 200 -and $xl.RawContentLength -gt 2000) "HTTP $($xl.StatusCode), $($xl.RawContentLength) bita"

    # ---- 5. Mulai ulang, data harus bertahan ----
    HentikanHost $dir
    if ($hostProc -and -not $hostProc.HasExited) { }
    if (-not $Isolated) {
        $env:SERUNI_LAUNCH_NO_BROWSER = "1"
        $env:SERUNI_LAUNCH_QUIET = "1"
        $lp2 = Start-Process -FilePath (Join-Path $dir "Seruni22Bakery.exe") -PassThru
        if (-not $lp2.WaitForExit(120000)) { try { $lp2.Kill() } catch { } }
        $null = $lp2.WaitForExit()
        Cek "Peluncur kedua exit 0" ($lp2.HasExited -and $lp2.ExitCode -eq 0) "exit=$($lp2.ExitCode)"
        Remove-Item Env:SERUNI_LAUNCH_NO_BROWSER, Env:SERUNI_LAUNCH_QUIET
        Cek "Tidak ada host ganda" (@(Get-Process Seruni22Bakery.Host -ErrorAction SilentlyContinue).Count -eq 1) "jumlah proses host=$(@(Get-Process Seruni22Bakery.Host -ErrorAction SilentlyContinue).Count)"
    } else {
        $hostProc = Start-Process -FilePath (Join-Path $dir "Seruni22Bakery.Host.exe") -ArgumentList @(
            "--urls", $base, "--Database:Path=$data\seruni.db", "--Jwt:SigningKeyFile=$data\jwt.key") -PassThru
    }
    Cek "Host hidup lagi" ($null -ne (TungguHealth)) "health ok"
    $l3 = Login $NewPassword
    $h3 = @{ Authorization = "Bearer $($l3.token)" }
    $daftar2 = Invoke-RestMethod "$base/api/orders" -Headers $h3
    Cek "Pesanan bertahan setelah mulai ulang" (@($daftar2 | Where-Object { $_.id -eq $o.id }).Count -eq 1) "id=$($o.id)"

    # ---- 6. Cadangan dan pemulihan ----
    HentikanHost $dir
    $backupDir = Join-Path $data "backups"
    $bp = Start-Process -FilePath (Join-Path $dir "Seruni22Bakery.Host.exe") `
        -ArgumentList @("--backup", "--Database:Path=$dbPath") -Wait -PassThru -RedirectStandardOutput (Join-Path $Evidence "backup-$tag.out")
    $cadangan = Get-ChildItem $backupDir -Filter "manual-*.db" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Cek "Cadangan manual dibuat" ($bp.ExitCode -eq 0 -and $null -ne $cadangan) $(if ($cadangan) { "$($cadangan.FullName) ($($cadangan.Length) bita)" } else { "exit=$($bp.ExitCode), tidak ada berkas" })

    if ($cadangan) {
        $hashDbSebelum = HashBerkas $dbPath
        $rp = Start-Process -FilePath (Join-Path $dir "Seruni22Bakery.Host.exe") `
            -ArgumentList @("--restore", "`"$($cadangan.FullName)`"", "--Database:Path=$dbPath") -Wait -PassThru `
            -RedirectStandardOutput (Join-Path $Evidence "restore-$tag.out")
        $hashDbSesudah = HashBerkas $dbPath
        Cek "Pemulihan dari cadangan" ($rp.ExitCode -eq 0) "exit=$($rp.ExitCode)"
        Catat "Basis data sebelum/sesudah pulih" "$hashDbSebelum -> $hashDbSesudah"
        $cadanganPra = Get-ChildItem $backupDir -Filter "pre-restore-*.db" -ErrorAction SilentlyContinue | Select-Object -First 1
        Cek "Salinan pengaman pra-pulih dibuat" ($null -ne $cadanganPra) $(if ($cadanganPra) { Split-Path $cadanganPra.FullName -Leaf } else { "tidak ada" })
    }

    # ---- 7. Status data sebelum copot ----
    $dataSebelum = @{}
    $namaDb = Split-Path $dbPath -Leaf
    foreach ($f in @($namaDb, "$namaDb-wal", "$namaDb-shm", "jwt.key")) {
        $dataSebelum[$f] = HashBerkas (Join-Path $data $f)
    }

    # ---- 8. Copot ----
    if (-not $KeepInstalled) {
        $unins = Join-Path $dir "unins000.exe"
        Cek "Uninstaller ada" (Test-Path $unins) $unins
        $up = Start-Process -FilePath $unins -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -Wait -PassThru
        Start-Sleep -Seconds 3
        Cek "Uninstall exit 0" ($up.ExitCode -eq 0) "exit=$($up.ExitCode)"
        Cek "Folder aplikasi terhapus" (-not (Test-Path $dir)) $dir
        $regSisa = @(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like "Seruni 22 Bakery*" })
        Cek "Entri uninstall terhapus" ($regSisa.Count -eq 0) "sisa=$($regSisa.Count)"

        $dataTetap = $true
        $rincian = @()
        foreach ($k in $dataSebelum.Keys) {
            $sesudah = HashBerkas (Join-Path $data $k)
            if ($sesudah -ne $dataSebelum[$k]) { $dataTetap = $false; $rincian += "$k BERUBAH" }
        }
        Cek "Data pengguna utuh setelah uninstall" ($dataTetap -and (Test-Path $dbPath)) $(if ($dataTetap) { "berkas identik; DB ada=$([bool](Test-Path $dbPath))" } else { $rincian -join "; " })
        Catat "Data disimpan di" $data

        if ($kunciPulih) {
            & reg.exe import $kunciPulih | Out-Null
            $pulih = @(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
                Where-Object { $_.DisplayName -like "Seruni 22 Bakery*" })
            Cek "Catatan uninstall instalasi nyata dikembalikan" ($pulih.Count -eq 1) "$($pulih.DisplayVersion) di $($pulih.InstallLocation)"
        }
    } else {
        Catat "Instalasi dibiarkan terpasang" "$dir (data: $data)"
    }

    # ---- 9. Bukti log ----
    # Log selalu ditulis ke folder data bawaan (LOCALAPPDATA), bukan ke folder data uji.
    $log = LogBaru (Join-Path $data "logs")
    if (-not $log) { $log = LogBaru (Join-Path $dataDefault "logs") }
    Catat "Log aplikasi terbaru" $(if ($log) { $log.FullName } else { "tidak ditemukan" })

    if ($Isolated) {
        Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
        Catat "Data uji terisolasi dihapus" (-not (Test-Path $data)) $data
    }
}
catch {
    Cek "Galat tak terduga" $false $_.Exception.Message
}
finally {
    $gagal = @($hasil | Where-Object { $_.status -eq "FAIL" }).Count
    $lulus = @($hasil | Where-Object { $_.status -eq "PASS" }).Count
    $ringkas = [pscustomobject]@{
        mulai = $mulai.ToString("s")
        selesai = (Get-Date).ToString("s")
        lulus = $lulus
        gagal = $gagal
        pemeriksaan = $hasil
        catatan = "Skrip ini menguji lapisan pemasangan + API. Langkah manual: buka shortcut, periksa tampilan, cetak struk bila perangkat tersedia."
    }
    $ringkas | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Evidence "UJI-MESIN-BERSIH-$tag.json") -Encoding UTF8
    ""
    "RINGKASAN: $lulus PASS, $gagal FAIL. Bukti: $Evidence\UJI-MESIN-BERSIH-$tag.json"
    if ($gagal -gt 0) { exit 1 }
}
