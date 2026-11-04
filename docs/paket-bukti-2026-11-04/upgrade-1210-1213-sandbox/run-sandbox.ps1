# Peluncur uji pasang/peningkatan 1.2.10 -> 1.2.13 di Windows Sandbox.
# Jalankan dari folder kit: powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1
# Folder kit dipetakan apa adanya ke C:\share; tamu menulis bukti ke .\bukti.
# Tidak memerlukan hak administrator dan tidak menyentuh instalasi atau data di mesin host.
# -Periksa hanya memvalidasi kit dan prasyarat, tanpa meluncurkan sandbox.

[CmdletBinding()]
param(
    [switch]$Periksa
)

$ErrorActionPreference = 'Continue'
$kit = $PSScriptRoot
$bukti = Join-Path $kit 'bukti'
$status = Join-Path $bukti 'status.txt'
$selesai = Join-Path $bukti 'selesai.txt'
$log = Join-Path $kit 'runner.log'
$sandboxExe = 'C:\Windows\System32\WindowsSandbox.exe'

function Tulis($teks) {
  Add-Content -Path $script:log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $teks)
}

$wajib = @(
  'Seruni22Bakery_v1.2.10_x64.msi',
  'Seruni22Bakery_v1.2.13_x64.msi',
  'Seruni22Bakery_Setup_v1.2.10.exe',
  'Seruni22Bakery_Setup_v1.2.13.exe',
  'hash-publish-1213.txt',
  'uji-1210-1213.ps1'
)
foreach ($f in $wajib) {
  if (-not (Test-Path (Join-Path $kit $f))) {
    Write-Host ("BERHENTI: berkas kit tidak lengkap: " + $f)
    exit 3
  }
}
if (-not (Test-Path $sandboxExe)) {
  Write-Host 'BERHENTI: Windows Sandbox tidak tersedia di mesin ini.'
  Write-Host 'Aktifkan lewat: Control Panel > Programs > Turn Windows features on or off > Windows Sandbox.'
  Write-Host 'Windows Sandbox tidak tersedia di Windows Home.'
  exit 3
}

# Checksum kit memastikan berkas tidak rusak/berubah saat disalin ke mesin uji.
$sumFile = Join-Path $kit 'KIT-SHA256SUMS.txt'
$sumDiperiksa = $false
if (Test-Path $sumFile) {
  $salah = @()
  foreach ($baris in Get-Content $sumFile) {
    if ($baris -match '^([0-9a-f]{64}) \*(.+)$') {
      $harap = $matches[1]; $nama = $matches[2]
      $path = Join-Path $kit $nama
      if (-not (Test-Path $path)) { $salah += "$nama hilang"; continue }
      $nyata = (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
      if ($nyata -ne $harap) { $salah += "$nama checksum berbeda" }
    }
  }
  if ($salah.Count -gt 0) {
    Write-Host 'BERHENTI: kit tidak utuh.'
    $salah | ForEach-Object { Write-Host ('  ' + $_) }
    exit 3
  }
  Write-Host 'Checksum kit: cocok semua.'
  $sumDiperiksa = $true
} else {
  Write-Host 'PERINGATAN: KIT-SHA256SUMS.txt tidak ada; keutuhan kit tidak diperiksa di sisi host.'
}

$ramMB = [math]::Round((Get-CimInstance Win32_OperatingSystem).TotalVisibleMemorySize / 1024)
if ($Periksa) {
  Write-Host ("Kit      : " + $kit)
  Write-Host ("RAM host : " + $ramMB + " MB")
  if ($sumDiperiksa) {
    Write-Host 'Prasyarat: Windows Sandbox tersedia, berkas lengkap, checksum cocok.'
  } else {
    Write-Host 'Prasyarat: Windows Sandbox tersedia, berkas lengkap; checksum TIDAK diperiksa.'
  }
  if ($ramMB -lt 4096) {
    Write-Host 'PERINGATAN: RAM host di bawah 4 GB. Tamu memakai 1,5 GB; uji bisa gagal boot.'
  }
  Write-Host 'Mode -Periksa: sandbox tidak diluncurkan.'
  exit 0
}
if ($ramMB -lt 3584) {
  Write-Host ("BERHENTI: RAM host hanya " + $ramMB + " MB, di bawah batas 3584 MB untuk tamu 1,5 GB.")
  exit 3
}

# Jangan pernah menimpa bukti yang sedang ditulis tamu; hanya bersihkan bila tidak ada sesi aktif.
$sesi = @(Get-Process -Name 'WindowsSandboxServer','WindowsSandboxRemoteSession' -ErrorAction SilentlyContinue)
if ($sesi.Count -gt 0) {
  Write-Host 'BERHENTI: ada sesi Windows Sandbox yang masih aktif. Tutup dulu, lalu jalankan ulang.'
  exit 2
}

Remove-Item (Join-Path $bukti '*') -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $bukti | Out-Null
Set-Content -Path $log -Value ("mulai " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8

# Folder kit dipetakan langsung, jadi tidak perlu menyalin 185 MB ke C:\tmp.
$kitXml = [System.Security.SecurityElement]::Escape($kit)
$wsb = Join-Path $kit 'uji-jalan.wsb'
# 1536 MB: mesin uji ini hanya punya 3,8 GB RAM. Nilai ini sudah terbukti cukup untuk
# uji 1.2.6 -> 1.2.7 pada mesin yang sama; 2048 MB tidak muat.
$isiWsb = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Disable</Networking>
  <MemoryInMB>1536</MemoryInMB>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$kitXml</HostFolder>
      <SandboxFolder>C:\share</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -File C:\share\uji-1210-1213.ps1</Command>
  </LogonCommand>
</Configuration>
"@
[IO.File]::WriteAllText($wsb, $isiWsb, (New-Object Text.UTF8Encoding($false)))

Write-Host ("Kit     : " + $kit)
Write-Host ("Sandbox : RAM 1536 MB, jaringan mati, folder kit dipetakan ke C:\share")
Write-Host 'Uji berjalan 13 bagian dan dapat memakan 20-40 menit. Jendela Sandbox akan menutup sendiri.'
try {
  Start-Process -FilePath $sandboxExe -ArgumentList ('"' + $wsb + '"') -ErrorAction Stop | Out-Null
} catch {
  Write-Host ('BERHENTI: Windows Sandbox gagal diluncurkan: ' + $_.Exception.Message)
  Write-Host 'Pastikan fitur Windows Sandbox aktif dan Hyper-V tersedia.'
  exit 3
}
Tulis 'sandbox diluncurkan'

$batas = (Get-Date).AddMinutes(60)
$takAdaProses = 0
$mtimeTerakhir = Get-Date
$gagalLuncur = $false
while ((Get-Date) -lt $batas) {
  if (Test-Path $selesai) { break }

  $vm = @(Get-Process -Name 'WindowsSandboxServer','WindowsSandboxRemoteSession','WindowsSandbox' -ErrorAction SilentlyContinue)
  if ($vm.Count -eq 0) { $takAdaProses += 10 } else { $takAdaProses = 0 }

  if (Test-Path $status) {
    $m = (Get-Item $status).LastWriteTime
    if ($m -gt $mtimeTerakhir) { $mtimeTerakhir = $m }
  }

  $diamDetik = [int]((Get-Date) - $mtimeTerakhir).TotalSeconds
  if ($takAdaProses -ge 240 -and $diamDetik -ge 240) {
    Tulis ("peluncuran gagal: tidak ada proses sandbox selama " + $takAdaProses + " detik, status tidak bertambah selama " + $diamDetik + " detik")
    $gagalLuncur = $true
    break
  }

  Start-Sleep -Seconds 10
}

$ok = $false
if (Test-Path $status) {
  Get-Content $status -Encoding UTF8 | Add-Content -Path $log
  $barisRekap = Get-Content $status -Encoding UTF8 | Where-Object { $_ -match '\| rekap \|' }
  if ($barisRekap -match '\| PASS \|') { $ok = $true }
} else {
  Tulis 'status.txt tidak terbentuk'
}

if (-not $ok) {
  foreach ($p in @(Get-Process -Name 'WindowsSandboxServer','WindowsSandboxRemoteSession' -ErrorAction SilentlyContinue)) {
    try { Stop-Process -Id $p.Id -Force } catch {}
  }
}

Write-Host ''
if (Test-Path $status) {
  Write-Host 'Ringkasan pemeriksaan:'
  Get-Content $status -Encoding UTF8 | ForEach-Object { Write-Host ('  ' + $_) }
  Write-Host ''
  Write-Host ("Bukti lengkap: " + $bukti)
} else {
  Write-Host ("Tidak ada status dari sandbox. Periksa " + $log)
}
Tulis ("selesai " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + " ok=" + $ok)
Write-Host ("ok=" + $ok)
if ($ok) { exit 0 }
if ($gagalLuncur) { exit 3 }
exit 1
