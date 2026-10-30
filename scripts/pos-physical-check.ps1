<#
  Uji fisik printer thermal Windows untuk SERUNI 22 BAKERY.

  Alasan ada: tes otomatis hanya membuktikan isi struk dan antrean di basis data. Hanya uji di
  PC kasir yang bisa membuktikan kertas benar-benar keluar, tidak terpotong, dan transaksi tetap
  utuh saat printer bermasalah. Skrip ini menjalankan host pada basis data sementara (data usaha
  tidak disentuh), mencatat printer/driver/port/status Windows, lalu mengirim struk uji dan struk
  pesanan ke printer yang dipilih. Semua jawaban server dan status pekerjaan cetak ditulis ke
  berkas bukti.

  Batas penting: "Printed" di sini berarti spooler Windows MENERIMA pekerjaan, bukan bukti kertas
  keluar. Windows tetap mengantrekan pekerjaan saat printer mati. Karena itu skrip selalu
  menutup dengan daftar periksa manual; jangan menandai lulus tanpa melihat kertasnya.

  Pemakaian:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
        -PrinterName "EPSON TM-T82" -PaperWidthMm 80 -AllowPrint

    ... -Mode Failure        # dijalankan saat printer DIMATIKAN atau kertas DIKELUARKAN
    ... -SkipPrint           # hanya merekam lingkungan + pesanan uji, tanpa mencetak
    (tanpa -AllowPrint / -SkipPrint: hanya merekam lingkungan printer, tidak menyentuh apa pun)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PrinterName,
    [ValidateSet(58, 80)][int]$PaperWidthMm = 80,
    [ValidateSet('Print', 'Failure')][string]$Mode = 'Print',
    [switch]$AllowPrint,
    [switch]$SkipPrint,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$buktiDir = Join-Path ([System.IO.Path]::GetTempPath()) "seruni-pos-fisik-$stamp"
if ($buktiDir -notlike '*\seruni-pos-fisik-*') {
    Write-Host "Folder bukti tak terduga: $buktiDir"
    exit 2
}
New-Item -ItemType Directory -Path $buktiDir -Force | Out-Null
$bukti = Join-Path $buktiDir 'bukti.txt'
$baris = New-Object System.Collections.ArrayList
$gagal = 0
$proses = $null

function Tulis([string]$teks) {
    [void]$baris.Add($teks)
    Write-Host $teks
}

function Catat([string]$nama, [string]$nilai) {
    Tulis ('{0}: {1}' -f $nama, $nilai)
}

function SimpanBukti {
    Set-Content -Path $bukti -Value ($baris -join [Environment]::NewLine) -Encoding UTF8
}

function Selesai([int]$kode) {
    SimpanBukti
    Write-Host ''
    Write-Host "Berkas bukti: $bukti"
    exit $kode
}

Tulis 'SERUNI 22 BAKERY - UJI FISIK PERANGKAT KASIR'
Catat 'Waktu' (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
Catat 'Mode' $Mode
Catat 'Printer' $PrinterName
Catat 'Lebar kertas' "$PaperWidthMm mm"
Tulis ''

# ── Lingkungan Windows ───────────────────────────────────────────────────────
$os = Get-CimInstance Win32_OperatingSystem
Catat 'Windows' ($os.Caption + ' ' + $os.Version + ' ' + $os.OSArchitecture)
$runtime = & dotnet --list-runtimes 2>$null | Where-Object { $_ -like 'Microsoft.NETCore.App 8.*' }
Catat 'Runtime .NET 8' (($runtime -join '; ') -replace '\s+', ' ')

$printer = Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue
if ($null -eq $printer) {
    Tulis ''
    Tulis "GAGAL: printer '$PrinterName' tidak terpasang di Windows ini."
    Tulis 'Pasang driver resmi printer lebih dulu, lalu ulangi. Aplikasi tidak memasang driver sendiri.'
    Tulis ''
    Tulis 'Printer yang terpasang saat ini:'
    Get-Printer | Select-Object Name, DriverName, PortName, PrinterStatus | Format-Table -AutoSize |
        Out-String | ForEach-Object { Tulis $_.TrimEnd() }
    $gagal = 1
    Selesai 1
}

Catat 'Driver printer' $printer.DriverName
Catat 'Port printer' $printer.PortName
Catat 'Status printer' $printer.PrinterStatus
try {
    $konfig = Get-PrintConfiguration -PrinterName $PrinterName -ErrorAction Stop
    Catat 'Ukuran kertas Windows' ($konfig.PaperSize + ' / ' + $konfig.Duplexing)
} catch {
    Catat 'Ukuran kertas Windows' '(driver tidak melaporkan; atur dari layar Perangkat Kasir)'
}
Tulis ''

if (-not $AllowPrint -and -not $SkipPrint) {
    Tulis 'Pencetakan DILEWATI karena -AllowPrint tidak diberikan.'
    Tulis 'Jalankan ulang dengan -AllowPrint saat printer siap, atau -SkipPrint untuk uji alur tanpa mencetak.'
    Selesai 0
}

# ── Host + basis data sementara ──────────────────────────────────────────────
$hostDll = Join-Path $root "src\Host.Service\bin\$Configuration\net8.0\Seruni22Bakery.Host.dll"
if (-not (Test-Path $hostDll)) {
    Tulis "GAGAL: $hostDll belum ada. Jalankan dotnet build lebih dulu."
    Selesai 1
}
$desktop = Join-Path $root 'desktop'
if (-not (Test-Path (Join-Path $desktop 'index.html'))) {
    Tulis "GAGAL: bundel frontend tidak ada di $desktop\index.html."
    Selesai 1
}

$db = Join-Path $buktiDir 'uji.db'
$kunci = Join-Path $buktiDir 'jwt.key'
$out = Join-Path $buktiDir 'host.out'
$err = Join-Path $buktiDir 'host.err'

$pendengar = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
$pendengar.Start()
$port = $pendengar.LocalEndpoint.Port
$pendengar.Stop()
$baseUrl = "http://127.0.0.1:$port"
Catat 'Host uji' "$baseUrl (basis data sementara $db)"

$argumen = @(
    $hostDll,
    "--Database:Path=$db",
    "--Desktop:StaticFilesPath=$desktop",
    "--Jwt:SigningKeyFile=$kunci",
    "--urls=$baseUrl"
)
$proses = Start-Process -FilePath 'dotnet' -ArgumentList $argumen -PassThru -NoNewWindow `
    -RedirectStandardOutput $out -RedirectStandardError $err

try {
    $siap = $false
    $batas = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $batas) {
        if ($proses.HasExited) { break }
        try {
            $cek = Invoke-WebRequest -Uri "$baseUrl/health" -UseBasicParsing -TimeoutSec 3
            if ($cek.StatusCode -eq 200) { $siap = $true; break }
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $siap) {
        Tulis 'GAGAL: host uji tidak siap dalam 60 detik.'
        $gagal = 1
        Selesai 1
    }

    function Kirim([string]$method, [string]$path, $body, [string]$token) {
        $headers = @{}
        if (-not [string]::IsNullOrWhiteSpace($token)) { $headers['Authorization'] = "Bearer $token" }
        $params = @{
            Uri = "$baseUrl$path"; Method = $method; Headers = $headers
            UseBasicParsing = $true; TimeoutSec = 40
        }
        if ($null -ne $body) {
            $params['ContentType'] = 'application/json'
            $params['Body'] = ($body | ConvertTo-Json -Depth 6 -Compress)
        }
        try {
            $r = Invoke-WebRequest @params
            return [pscustomobject]@{ Status = [int]$r.StatusCode; Teks = $r.Content }
        } catch {
            $resp = $_.Exception.Response
            if ($null -eq $resp) { throw }
            $pembaca = New-Object System.IO.StreamReader($resp.GetResponseStream())
            return [pscustomobject]@{ Status = [int]$resp.StatusCode; Teks = $pembaca.ReadToEnd() }
        }
    }

    # ── Masuk sebagai Owner (gerbang ganti sandi dipenuhi lewat API) ─────────
    $masuk = Kirim 'Post' '/api/auth/login' @{ username = 'admin'; password = 'admin123' } $null
    if ($masuk.Status -ne 200) {
        Tulis "GAGAL: login admin (HTTP $($masuk.Status))."
        $gagal = 1
        Selesai 1
    }
    $sesi = $masuk.Teks | ConvertFrom-Json
    $token = $sesi.token
    if ($sesi.mustChangePassword) {
        $sandiBaru = 'SandiUjiFisik2026'
        $ganti = Kirim 'Post' '/api/auth/change-password' `
            @{ currentPassword = 'admin123'; newPassword = $sandiBaru } $token
        Catat 'Ganti sandi awal' "HTTP $($ganti.Status)"
        $masuk = Kirim 'Post' '/api/auth/login' @{ username = 'admin'; password = $sandiBaru } $null
        $token = ($masuk.Teks | ConvertFrom-Json).token
    }
    Catat 'Login Owner' "HTTP $($masuk.Status)"

    # ── Pengaturan perangkat pada basis data sementara ───────────────────────
    $setelan = @{
        'device.printer.name' = $PrinterName
        'device.paper.width_mm' = [string]$PaperWidthMm
        'device.printer.auto_print' = '0'
    }
    $simpan = Kirim 'Put' '/api/devices/settings' $setelan $token
    Catat 'Simpan pengaturan' "HTTP $($simpan.Status)"
    if ($simpan.Status -ne 200) {
        Tulis "GAGAL: pengaturan ditolak: $($simpan.Teks)"
        $gagal = 1
        Selesai 1
    }

    # ── Pesanan uji tanpa menyentuh data usaha ───────────────────────────────
    $nama = "Uji Fisik $stamp"
    $pesananReq = @{
        customerName = $nama
        description = 'Bolu pisang dan nastar untuk uji cetak'
        pickupDate = (Get-Date).AddDays(2).ToString('yyyy-MM-ddTHH:mm:ss')
        totalAmount = 2500000
        downPayment = 500000
        quantity = 2
    }
    $buat = Kirim 'Post' '/api/orders' $pesananReq $token
    if ($buat.Status -ne 201) {
        Tulis "GAGAL: pesanan uji tidak bisa dibuat (HTTP $($buat.Status)): $($buat.Teks)"
        $gagal = 1
        Selesai 1
    }
    $pesananId = ($buat.Teks | ConvertFrom-Json).id
    Catat 'Pesanan uji' "#$pesananId dibuat (HTTP 201)"
    $sebelum = (Kirim 'Get' "/api/orders/$pesananId" $null $token).Teks | ConvertFrom-Json

    $jobId = $null
    if ($AllowPrint -and -not $SkipPrint) {
        $uji = Kirim 'Post' '/api/devices/test-print' $null $token
        Catat 'Cetak struk uji' "HTTP $($uji.Status) $($uji.Teks)"
        if ($uji.Status -eq 200) { $jobId = ($uji.Teks | ConvertFrom-Json).jobId }
        else {
            $galatUji = $uji.Teks | ConvertFrom-Json
            if ($null -ne $galatUji.jobId) { $jobId = $galatUji.jobId }
        }

        $struk = Kirim 'Post' "/api/orders/$pesananId/receipt" @{ cetakUlang = $false } $token
        Catat 'Cetak struk pesanan' "HTTP $($struk.Status) $($struk.Teks)"
    } else {
        Tulis 'Pencetakan dilewati (-SkipPrint). Alur host, login, pengaturan, dan pesanan tetap diuji.'
    }

    # ── Pekerjaan cetak yang tercatat ────────────────────────────────────────
    $daftarJob = Kirim 'Get' '/api/devices/print-jobs?limit=20' $null $token
    if ($daftarJob.Status -eq 200) {
        # PS 5.1 mengubah [] menjadi $null dan satu baris menjadi objek: samakan ke array.
        $terbaca = $daftarJob.Teks | ConvertFrom-Json
        $jobs = @()
        if ($null -ne $terbaca) { $jobs = @($terbaca) }
        Catat 'Jumlah pekerjaan cetak' $jobs.Count
        foreach ($job in @($jobs | Where-Object { $null -ne $_ })) {
            Tulis ('  job #{0} {1} {2} status={3} attempts={4} galat={5}' -f `
                $job.id, $job.kind, $job.printerName, $job.status, $job.attempts, $job.lastError)
        }
        $untukPesanan = @($jobs | Where-Object { $null -ne $_ -and $_.orderId -eq $pesananId })
        if ($AllowPrint -and -not $SkipPrint -and $untukPesanan.Count -ne 1) {
            Tulis "GAGAL: pekerjaan cetak untuk pesanan #$pesananId harus tepat satu, ditemukan $($untukPesanan.Count)."
            $gagal = 1
        }
    } else {
        Tulis "GAGAL: riwayat pekerjaan cetak tidak terbaca (HTTP $($daftarJob.Status))."
        $gagal = 1
    }

    # ── Pesanan tidak boleh berubah karena pencetakan ────────────────────────
    $sesudah = (Kirim 'Get' "/api/orders/$pesananId" $null $token).Teks | ConvertFrom-Json
    Catat 'Total pesanan sebelum/sesudah' "$($sebelum.totalAmount) / $($sesudah.totalAmount)"
    Catat 'Status pesanan sebelum/sesudah' "$($sebelum.status) / $($sesudah.status)"
    if ($sebelum.totalAmount -ne $sesudah.totalAmount -or $sebelum.status -ne $sesudah.status) {
        Tulis 'GAGAL: pencetakan mengubah pesanan. Ini tidak boleh terjadi.'
        $gagal = 1
    }
    $semuaPesanan = (Kirim 'Get' '/api/orders' $null $token).Teks | ConvertFrom-Json
    $jumlahPesanan = @($semuaPesanan | Where-Object { $null -ne $_ -and $_.customerNameSnapshot -eq $nama }).Count
    Catat 'Pesanan dengan nama uji' $jumlahPesanan
    if ($jumlahPesanan -ne 1) {
        Tulis 'GAGAL: pesanan uji tercatat lebih dari sekali.'
        $gagal = 1
    }
} catch {
    Tulis "GAGAL tak terduga: $($_.Exception.Message)"
    $gagal = 1
} finally {
    if ($null -ne $proses -and -not $proses.HasExited) {
        Stop-Process -Id $proses.Id -Force -ErrorAction SilentlyContinue
        $proses.WaitForExit(5000) | Out-Null
    }
}

Tulis ''
Tulis 'PERIKSA MANUAL (wajib, tidak bisa dibuktikan skrip):'
Tulis '  1. Kertas struk uji keluar utuh: nama toko, garis, Total, rupiah, tidak terpotong.'
Tulis '  2. Jumlah kolom sesuai lebar: 80 mm = 48 kolom, 58 mm = 32 kolom.'
Tulis '  3. Potongan kertas rapi bila printer punya pemotong otomatis.'
Tulis '  4. Mode Failure: saat printer mati/kertas habis, catat apa yang tampil di layar dan di'
Tulis '     antrean Windows. Windows dapat menerima pekerjaan walau kertas tidak keluar; itu bukan PASS.'
Tulis '  5. Nyalakan printer/pasang kertas, cetak ulang dari layar Penjualan, pastikan struk kedua'
Tulis '     keluar dan pesanan tidak bertambah.'
Tulis ''

if ($gagal -eq 0) {
    if ($AllowPrint -and -not $SkipPrint) {
        Tulis 'HASIL OTOMATIS: PASS - pekerjaan tercatat, pesanan utuh. Hasil cetak fisik menunggu periksa manual.'
    } else {
        Tulis 'HASIL OTOMATIS: PASS - alur host, login, pengaturan, dan pesanan. TIDAK ADA pencetakan dilakukan.'
    }
} else {
    Tulis 'HASIL OTOMATIS: FAIL - lihat baris GAGAL di atas.'
}
Selesai $gagal
