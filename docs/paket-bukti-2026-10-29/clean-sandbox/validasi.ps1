$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$bukti  = 'C:\share\bukti'
$status = Join-Path $bukti 'status.txt'
$msi    = 'C:\share\Seruni22Bakery_v1.2.4_x64.msi'
$instal = 'C:\Program Files\Seruni 22 Bakery'
$kode   = '{B1734125-F339-4527-AD56-CC888D911FC0}'
$msiSha = '2972B145E3FD77DF2500B654D9AF290421D03CE52E1FBB1416333C8CA7F23114'
$msiSize = 50357095
$idxSha = '5364F0B36969D616E0D5FC2302698D5A7C602F01EDD5355FA9CCEFE40F302042'
$gagal = 0
$lulus = 0

New-Item -ItemType Directory -Force -Path $bukti | Out-Null
Start-Transcript -Path (Join-Path $bukti 'validasi.log') -Force | Out-Null

function Catat($langkah, $hasil, $rincian) {
  $baris = '{0} | {1} | {2} | {3}' -f (Get-Date -Format 'HH:mm:ss'), $langkah, $hasil, $rincian
  Add-Content -Path $status -Value $baris -Encoding UTF8
  Write-Host $baris
  if ($hasil -eq 'PASS') { $script:lulus++ }
  if ($hasil -eq 'FAIL') { $script:gagal++ }
}

function Cek($langkah, $kondisi, $rincianLulus, $rincianGagal) {
  if ($kondisi) { Catat $langkah 'PASS' $rincianLulus }
  else { Catat $langkah 'FAIL' $rincianGagal }
}

function Jalankan-Msi($aksi, $log) {
  if ($aksi -eq '/x') { $arg = @('/x', $kode, '/qn', '/norestart', '/l*v', $log) }
  else { $arg = @($aksi, $msi, '/qn', '/norestart', '/l*v', $log) }

  $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arg -PassThru
  if (-not $p.WaitForExit(240000)) { try { $p.Kill() } catch {}; return -1 }
  $k = $p.ExitCode
  if ($k -eq 0 -or $k -eq 3010) { return $k }

  Catat 'msiexec' 'INFO' ("exit $k tanpa elevasi; ulang via RunAs")
  try {
    $p2 = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arg -Verb RunAs -PassThru
    if (-not $p2.WaitForExit(240000)) { try { $p2.Kill() } catch {}; return -1 }
    return $p2.ExitCode
  } catch {
    Catat 'msiexec' 'INFO' ('elevasi gagal: ' + $_.Exception.Message)
    return $k
  }
}

function Cari-Arp {
  foreach ($k in @("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$kode",
                   "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$kode")) {
    if (Test-Path $k) { return Get-ItemProperty $k }
  }
  return $null
}

function Target-Pintasan($path) {
  if (-not (Test-Path $path)) { return '' }
  $w = New-Object -ComObject WScript.Shell
  return $w.CreateShortcut($path).TargetPath
}

# ── 1. Lingkungan ────────────────────────────────────────────────────────────
$os  = Get-CimInstance Win32_OperatingSystem
$id  = [Security.Principal.WindowsIdentity]::GetCurrent()
$adm = (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Catat 'lingkungan' 'INFO' ($os.Caption + ' build ' + $os.BuildNumber + '; pengguna ' + $id.Name + '; elevated=' + $adm)
Cek 'mesin bersih: tanpa runtime .NET bersama' (-not (Test-Path 'C:\Program Files\dotnet')) 'C:\Program Files\dotnet tidak ada (publish self-contained)' 'C:\Program Files\dotnet ada; klaim mesin bersih gugur'
Cek 'mesin bersih: belum ada instalasi Seruni' ((-not (Test-Path $instal)) -and ($null -eq (Cari-Arp))) 'tidak ada folder program dan tidak ada entri ARP' 'sudah ada folder program atau entri ARP sebelum uji'

# ── 2. Salinan MSI ───────────────────────────────────────────────────────────
$fi = Get-Item $msi
$h  = (Get-FileHash $msi -Algorithm SHA256).Hash
Cek 'salinan MSI utuh' (($h -eq $msiSha) -and ($fi.Length -eq $msiSize)) ("$($fi.Length) byte, SHA256 $h") ("ukuran/hash tidak cocok: $($fi.Length) byte $h")

# ── 3. Pasang ────────────────────────────────────────────────────────────────
$kodePasang = Jalankan-Msi '/i' (Join-Path $bukti 'msi-install.log')
Cek 'pasang senyap msiexec /qn' (($kodePasang -eq 0) -or ($kodePasang -eq 3010)) ("exit $kodePasang") ("exit $kodePasang")

# ── 4. Isi instalasi ─────────────────────────────────────────────────────────
Cek 'folder instalasi' (Test-Path $instal) $instal 'folder tidak ada setelah pasang'

$berkas = @(Get-ChildItem -Path $instal -Recurse -File -ErrorAction SilentlyContinue)
Cek 'jumlah berkas terpasang' ($berkas.Count -eq 393) ("$($berkas.Count) berkas") ("$($berkas.Count) berkas, seharusnya 393")

$peluncur = Join-Path $instal 'Seruni22Bakery.exe'
$hostExe  = Join-Path $instal 'Seruni22Bakery.Host.exe'
Cek 'berkas inti ada' ((Test-Path $peluncur) -and (Test-Path $hostExe) -and (Test-Path (Join-Path $instal 'Seruni22Bakery.Infrastructure.Sqlite.dll'))) 'peluncur, host, dan DLL infrastruktur ada' 'ada berkas inti yang tidak terpasang'
Cek 'runtime mandiri ikut' ((Test-Path (Join-Path $instal 'hostfxr.dll')) -and (Test-Path (Join-Path $instal 'coreclr.dll'))) 'hostfxr.dll + coreclr.dll ada' 'runtime mandiri tidak lengkap'
Cek 'tanpa berkas pengembangan' ((-not (Test-Path (Join-Path $instal 'appsettings.Development.json'))) -and (-not (Test-Path (Join-Path $instal 'Seruni22Bakery.Host.pdb'))) -and (-not (Test-Path (Join-Path $instal 'seruni22bakery.db'))) -and (-not (Test-Path (Join-Path $instal 'jwt.key')))) 'tidak ada appsettings.Development.json, PDB, basis data, atau kunci' 'ada berkas pengembangan/rahasia di paket'

$idx = Join-Path $instal 'wwwroot\assets\index-CKgTNzb_.js'
$lenIdx = 0
$hIdx = ''
if (Test-Path $idx) { $lenIdx = (Get-Item $idx).Length; $hIdx = (Get-FileHash $idx -Algorithm SHA256).Hash }
Cek 'bundel baru terpasang' ((Test-Path $idx) -and ($lenIdx -eq 573778)) "index-CKgTNzb_.js $lenIdx bita, SHA256 $hIdx" 'aset bundel baru tidak ada/ukuran lain'
$hIndex = if (Test-Path (Join-Path $instal 'wwwroot\index.html')) { (Get-FileHash (Join-Path $instal 'wwwroot\index.html') -Algorithm SHA256).Hash } else { '' }
Cek 'index.html sama dengan paket' ($hIndex -eq $idxSha) "SHA256 $hIndex" "SHA256 $hIndex, seharusnya $idxSha"

$vPeluncur = (Get-Item $peluncur).VersionInfo.FileVersion
$vHost     = (Get-Item $hostExe).VersionInfo.FileVersion
Cek 'versi peluncur 1.2.4' ($vPeluncur -eq '1.2.4') $vPeluncur ("versi berkas $vPeluncur")
Cek 'versi host 1.2.4' ($vHost -like '1.2.4*') $vHost ("versi berkas $vHost")

$tanda = (Get-AuthenticodeSignature $peluncur).Status
Cek 'tanda tangan dilaporkan apa adanya' ($tanda -eq 'NotSigned') 'NotSigned (sesuai paket yang belum ditandatangani)' ("status $tanda; klaim tanda tangan harus diperiksa ulang")

$arp = Cari-Arp
Cek 'entri ARP 1.2.4' (($null -ne $arp) -and ($arp.DisplayVersion -eq '1.2.4')) ("DisplayName='$($arp.DisplayName)' DisplayVersion='$($arp.DisplayVersion)'") "ARP: $($arp | Out-String)"

$lnkMenu = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk'
$lnkDesk = 'C:\Users\Public\Desktop\Seruni 22 Bakery.lnk'
Cek 'pintasan Start Menu' ((Test-Path $lnkMenu) -and ((Target-Pintasan $lnkMenu) -eq $peluncur)) ("target " + (Target-Pintasan $lnkMenu)) 'pintasan Start Menu tidak ada/target salah'
Cek 'pintasan Desktop publik' ((Test-Path $lnkDesk) -and ((Target-Pintasan $lnkDesk) -eq $peluncur)) ("target " + (Target-Pintasan $lnkDesk)) 'pintasan Desktop publik tidak ada/target salah'

# ── 5. Jalankan host dari hasil pasang ───────────────────────────────────────
New-Item -ItemType Directory -Force -Path 'C:\data' | Out-Null
$outHost = Join-Path $bukti 'host-uji.log'
$errHost = Join-Path $bukti 'host-uji-err.log'
$pHost = Start-Process -FilePath $hostExe -ArgumentList @('--Database:Path=C:\data\uji.db', '--Jwt:SigningKeyFile=C:\data\uji.key', '--Urls=http://127.0.0.1:5292') -WorkingDirectory $instal -RedirectStandardOutput $outHost -RedirectStandardError $errHost -PassThru

$jawab = $null
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 2
  try {
    $r = Invoke-WebRequest -Uri 'http://127.0.0.1:5292/health' -UseBasicParsing -TimeoutSec 5
    if ($r.StatusCode -eq 200) { $jawab = $r; break }
  } catch {}
}
Cek 'host menjawab /health' ($null -ne $jawab) ("percobaan " + ($i + 1)) 'host tidak menjawab dalam 120 detik'

$versiHealth = ''
if ($null -ne $jawab) { try { $versiHealth = ($jawab.Content | ConvertFrom-Json).version } catch {} }
Cek 'health melaporkan 1.2.4' ($versiHealth -eq '1.2.4') ("version=$versiHealth") ("version=$versiHealth")

$spa = $null
try { $spa = Invoke-WebRequest -Uri 'http://127.0.0.1:5292/' -UseBasicParsing -TimeoutSec 10 } catch {}
Cek 'SPA dimuat' (($null -ne $spa) -and ($spa.StatusCode -eq 200) -and ($spa.Content -match 'index-CKgTNzb_\.js')) ("status $($spa.StatusCode), $($spa.RawContentLength) bita") 'SPA tidak 200 / tidak merujuk bundel baru'

$aset = $null
try { $aset = Invoke-WebRequest -Uri 'http://127.0.0.1:5292/assets/index-CKgTNzb_.js' -UseBasicParsing -TimeoutSec 20 } catch {}
Cek 'aset JS bundel baru dilayani' (($null -ne $aset) -and ($aset.StatusCode -eq 200) -and ($aset.RawContentLength -eq 573778)) ("status $($aset.StatusCode), $($aset.RawContentLength) bita") 'aset tidak 200 / ukuran lain'

Start-Sleep -Seconds 2
$logHost = ''
if (Test-Path $outHost) { $logHost = Get-Content $outHost -Raw }
$migrasi = ([regex]::Matches($logHost, 'Migrasi \d+ diterapkan')).Count
Cek 'basis data baru dimigrasi 1..17' (($migrasi -eq 17) -and ($logHost -match 'Migrasi 17 diterapkan')) ("$migrasi migrasi diterapkan") ("$migrasi migrasi diterapkan")
$ukuranDb = 0
if (Test-Path 'C:\data\uji.db') { $ukuranDb = (Get-Item 'C:\data\uji.db').Length }
Cek 'basis data scratch terbentuk' ($ukuranDb -gt 0) ("uji.db $ukuranDb bita") 'uji.db tidak terbentuk'

try { Stop-Process -Id $pHost.Id -Force -ErrorAction Stop; Catat 'host dihentikan' 'INFO' 'Stop-Process' } catch { Catat 'host dihentikan' 'INFO' ('gagal: ' + $_.Exception.Message) }
Start-Sleep -Seconds 3

# ── 6. Copot ─────────────────────────────────────────────────────────────────
$kodeCopot = Jalankan-Msi '/x' (Join-Path $bukti 'msi-uninstall.log')
Cek 'copot senyap msiexec /qn' (($kodeCopot -eq 0) -or ($kodeCopot -eq 3010)) ("exit $kodeCopot") ("exit $kodeCopot")
Start-Sleep -Seconds 3

Cek 'folder instalasi hilang' (-not (Test-Path $instal)) 'folder program terhapus' 'folder program masih ada'
Cek 'pintasan Start Menu hilang' (-not (Test-Path $lnkMenu)) 'terhapus' 'masih ada'
Cek 'pintasan Desktop publik hilang' (-not (Test-Path $lnkDesk)) 'terhapus' 'masih ada'
Cek 'entri ARP hilang' ($null -eq (Cari-Arp)) 'ARP bersih' 'ARP masih ada'
Cek 'data di luar folder program tetap utuh' ((Test-Path 'C:\data\uji.db') -and (Test-Path 'C:\data\uji.key')) 'uji.db dan uji.key masih ada setelah copot' 'data uji ikut terhapus'
$sisa = @(Get-Process | Where-Object { $_.ProcessName -like 'Seruni22Bakery*' })
Cek 'tanpa proses yatim' ($sisa.Count -eq 0) 'tidak ada proses Seruni22Bakery tersisa' ("masih " + $sisa.Count + " proses")

Catat 'rekap' $(if ($gagal -eq 0) { 'PASS' } else { 'FAIL' }) ("lulus=$lulus gagal=$gagal")

Stop-Transcript | Out-Null
Copy-Item -Path (Join-Path $bukti 'validasi.log') -Destination (Join-Path $bukti 'validasi-copy.log') -Force -ErrorAction SilentlyContinue
shutdown.exe /s /t 20 /f
