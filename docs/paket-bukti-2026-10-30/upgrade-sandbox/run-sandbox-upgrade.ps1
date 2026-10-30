$ErrorActionPreference = 'Continue'
$share = 'C:\tmp\sbx-upgrade-127'
$bukti = Join-Path $share 'bukti'
$selesai = Join-Path $bukti 'selesai.txt'
$status = Join-Path $bukti 'status.txt'
$log = Join-Path $share 'runner.log'

function Tulis($teks) { Add-Content -Path $script:log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $teks) }

# Jangan pernah menimpa bukti yang sedang ditulis guest; hanya bersihkan bila tidak ada sesi aktif.
$sesi = @(Get-Process -Name 'WindowsSandboxServer','WindowsSandboxRemoteSession' -ErrorAction SilentlyContinue)
if ($sesi.Count -gt 0) {
  Tulis 'ada sesi sandbox aktif; runner berhenti tanpa menyentuh bukti'
  exit 2
}

Remove-Item (Join-Path $bukti '*') -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $bukti | Out-Null
Set-Content -Path $log -Value ("mulai " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8

# WindowsSandbox.exe melepas diri ke WindowsSandboxServer, jadi kode keluarnya bukan tanda selesai.
# Sesi baru butuh puluhan detik untuk memunculkan WindowsSandboxServer; kegagalan peluncuran
# dibuktikan lewat tidak adanya proses apa pun selama 120 detik, bukan 5 detik.
Start-Process -FilePath 'C:\Windows\System32\WindowsSandbox.exe' -ArgumentList (Join-Path $share 'uji.wsb') | Out-Null
Tulis 'sandbox diluncurkan'

$batas = (Get-Date).AddMinutes(45)
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
  if ($takAdaProses -ge 120 -and $diamDetik -ge 120) {
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
Tulis ("selesai " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + " ok=" + $ok)
Write-Host ("ok=" + $ok)
if ($ok) { exit 0 }
if ($gagalLuncur) { exit 3 }
exit 1
