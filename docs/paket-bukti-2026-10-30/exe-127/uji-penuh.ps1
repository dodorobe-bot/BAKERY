$ErrorActionPreference = 'Continue'
$out = 'C:\tmp\exe-127'
$status = Join-Path $out 'status.txt'
$dir = 'C:\tmp\seruni-exe-127'
$data = Join-Path $out 'data'
$exe = 'D:\bakery-desktop\dist\installer\Seruni22Bakery_Setup_v1.2.7.exe'
$base = 'http://127.0.0.1:5312'
$targetMsi = 'C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.exe'
$dbNyata = 'C:\Users\Administrator\AppData\Local\Seruni22Bakery\seruni22bakery.db'
$lnkMenuDir = 'C:\Users\Administrator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery'
$lnkDesk = 'C:\Users\Administrator\Desktop\Seruni 22 Bakery.lnk'
$lulus = 0
$gagal = 0

Set-Content -Path $status -Value '' -Encoding UTF8
New-Item -ItemType Directory -Force -Path $data | Out-Null

function Catat($langkah, $hasil, $rincian) {
  $baris = '{0} | {1} | {2} | {3}' -f (Get-Date -Format 'HH:mm:ss'), $langkah, $hasil, $rincian
  Add-Content -Path $status -Value $baris -Encoding UTF8
  Write-Host $baris
  if ($hasil -eq 'PASS') { $script:lulus++ }
  if ($hasil -eq 'FAIL') { $script:gagal++ }
}
function Cek($langkah, $kondisi, $rincianLulus, $rincianGagal) {
  if ($kondisi) { Catat $langkah 'PASS' $rincianLulus } else { Catat $langkah 'FAIL' $rincianGagal }
}
function SidikDbNyata {
  if (-not (Test-Path $dbNyata)) { return 'tidak ada' }
  $d = Get-Item $dbNyata
  $w = if (Test-Path ($dbNyata + '-wal')) { 'wal ADA' } else { 'wal tidak ada' }
  $s = if (Test-Path ($dbNyata + '-shm')) { 'shm ADA' } else { 'shm tidak ada' }
  return ("{0} bita, mtime {1}, {2}, {3}" -f $d.Length, $d.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss.fff'), $w, $s)
}
function TargetPintasan($path) {
  if (-not (Test-Path $path)) { return '' }
  $w = New-Object -ComObject WScript.Shell
  return $w.CreateShortcut($path).TargetPath
}
function DaftarArp {
  $hasil = @()
  foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
                      'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
                      'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
    foreach ($k in @(Get-ChildItem $root -ErrorAction SilentlyContinue)) {
      $pp = Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue
      if ($pp.DisplayName -like '*Seruni*') {
        $hasil += [pscustomobject]@{ Root = $root; Key = $k.PSChildName; DisplayName = $pp.DisplayName; DisplayVersion = $pp.DisplayVersion }
      }
    }
  }
  return $hasil
}

# ── 1. Sebelum pasang ───────────────────────────────────────────────────────
$fi = Get-Item $exe
$h = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
Cek 'EXE 1.2.7 utuh' (($fi.Length -eq 42641033) -and ($h -eq 'adb779c1d6c4ceb1c424ae3700b6f435e6223ae372104827d3ef4b9a943cd36b')) ("$($fi.Length) bita, SHA256 $h") ("$($fi.Length) bita, SHA256 $h")
$prosesAwal = @(Get-Process | Where-Object { $_.ProcessName -like 'Seruni22Bakery*' })
Cek 'tidak ada proses Seruni sebelum uji' ($prosesAwal.Count -eq 0) '0 proses' ("$($prosesAwal.Count) proses")
$arpAwal = @(DaftarArp)
Cek 'ARP sebelum uji: satu entri MSI 1.2.7' (($arpAwal.Count -eq 1) -and ($arpAwal[0].Root -like 'HKLM*') -and ($arpAwal[0].DisplayVersion -eq '1.2.7')) (($arpAwal | ForEach-Object { "$($_.DisplayName) $($_.DisplayVersion)" }) -join '; ') (($arpAwal | ForEach-Object { "$($_.DisplayName) $($_.DisplayVersion) [$($_.Root)]" }) -join '; ')
$sidikDb = SidikDbNyata
Catat 'basis data nyata sebelum uji' 'INFO' $sidikDb
Cek 'pintasan MSI sebelum uji benar' ((TargetPintasan 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk') -eq $targetMsi) 'Start Menu ProgramData menunjuk instalasi MSI' 'pintasan Start Menu ProgramData salah'
Cek 'belum ada instalasi per-pengguna EXE' ((-not (Test-Path $lnkMenuDir)) -and (-not (Test-Path $lnkDesk)) -and (-not (Test-Path $dir))) 'tidak ada grup Start Menu, pintasan Desktop, atau folder uji' 'sudah ada sisa instalasi per-pengguna'

# ── 2. Pasang ───────────────────────────────────────────────────────────────
$logPasang = Join-Path $out 'exe-install.log'
$p = Start-Process -FilePath $exe -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR=' + $dir), ('/LOG=' + $logPasang)) -PassThru
if (-not $p.WaitForExit(600000)) { try { $p.Kill() } catch {}; Catat 'pasang senyap EXE 1.2.7' 'FAIL' 'melewati 10 menit' } else { Cek 'pasang senyap EXE 1.2.7' ($p.ExitCode -eq 0) ("exit " + $p.ExitCode) ("exit " + $p.ExitCode) }

$berkas = @(Get-ChildItem -Path $dir -Recurse -File -ErrorAction SilentlyContinue)
Cek 'jumlah berkas terpasang EXE' ($berkas.Count -eq 397) ("$($berkas.Count) berkas, " + ($berkas | Measure-Object -Property Length -Sum).Sum + ' bita') ("$($berkas.Count) berkas")
$peluncur = Join-Path $dir 'Seruni22Bakery.exe'
$hostExe = Join-Path $dir 'Seruni22Bakery.Host.exe'
Cek 'versi peluncur EXE 1.2.7' ((Get-Item $peluncur).VersionInfo.FileVersion -eq '1.2.7') (Get-Item $peluncur).VersionInfo.FileVersion ("versi " + (Get-Item $peluncur).VersionInfo.FileVersion)
Cek 'versi host EXE 1.2.7.0' ((Get-Item $hostExe).VersionInfo.FileVersion -like '1.2.7*') (Get-Item $hostExe).VersionInfo.FileVersion ("versi " + (Get-Item $hostExe).VersionInfo.FileVersion)
$hDll = (Get-FileHash (Join-Path $dir 'Seruni22Bakery.Host.dll') -Algorithm SHA256).Hash
Cek 'Host.dll sama dengan paket' ($hDll -eq '4773A367836B117A8106E45A2D345B7D15D61FDF774768183C97CD28F07C8B49') $hDll $hDll
$hIdx = (Get-FileHash (Join-Path $dir 'wwwroot\index.html') -Algorithm SHA256).Hash
Cek 'index.html sama dengan paket' ($hIdx -eq 'F4A2FC04CA04E10FD665704C11B63E1BB57677A8AC3B4570DB411DFDB694BD2E') $hIdx $hIdx
$js = Join-Path $dir 'wwwroot\assets\index-DppHnhYj.js'
$sisaLama = @(Get-ChildItem (Join-Path $dir 'wwwroot\assets') -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'index-BPNiweyi*' -or $_.Name -like 'index-Bz7F3T1f*' })
Cek 'bundel 1.2.7 ada, bundel 1.2.6 tidak ikut' ((Test-Path $js) -and ((Get-Item $js).Length -eq 582985) -and (Test-Path (Join-Path $dir 'wwwroot\assets\index-BNYyu2En.css')) -and ($sisaLama.Count -eq 0)) ("js " + (Get-Item $js).Length + ' bita; sisa bundel lama 0') 'bundel tidak sesuai'
$adaDev = @()
foreach ($n in @('appsettings.Development.json', 'Seruni22Bakery.Host.pdb', 'seruni22bakery.db', 'jwt.key')) { if (Test-Path (Join-Path $dir $n)) { $adaDev += $n } }
Cek 'tanpa berkas pengembangan/rahasia' ($adaDev.Count -eq 0) 'tidak ada appsettings.Development.json, PDB, basis data, atau kunci' ($adaDev -join ', ')
Cek 'pintasan Start Menu per-pengguna EXE' (((TargetPintasan (Join-Path $lnkMenuDir 'Seruni 22 Bakery.lnk')) -eq (Join-Path $dir 'Seruni22Bakery.exe')) -and (Test-Path (Join-Path $lnkMenuDir 'Uninstall Seruni 22 Bakery.lnk'))) ("target " + (TargetPintasan (Join-Path $lnkMenuDir 'Seruni 22 Bakery.lnk'))) 'pintasan Start Menu per-pengguna salah'
Cek 'pintasan Desktop per-pengguna EXE' ((TargetPintasan $lnkDesk) -eq (Join-Path $dir 'Seruni22Bakery.exe')) ("target " + (TargetPintasan $lnkDesk)) 'pintasan Desktop per-pengguna salah'
Cek 'pintasan MSI tidak tersentuh saat pasang' (((TargetPintasan 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk') -eq $targetMsi) -and ((TargetPintasan 'C:\Users\Public\Desktop\Seruni 22 Bakery.lnk') -eq $targetMsi)) 'Start Menu ProgramData + Public Desktop tetap menunjuk MSI' 'pintasan MSI berubah'
$arpPasang = @(DaftarArp)
$hkcuPasang = @($arpPasang | Where-Object { $_.Root -like 'HKCU*' })
$hklmPasang = @($arpPasang | Where-Object { $_.Root -like 'HKLM*' })
Cek 'ARP per-pengguna EXE muncul' (($hkcuPasang.Count -eq 1) -and ($hkcuPasang[0].DisplayVersion -eq '1.2.7')) ("$($hkcuPasang[0].DisplayName) $($hkcuPasang[0].DisplayVersion) [$($hkcuPasang[0].Key)]") ("$($hkcuPasang.Count) entri HKCU")
Cek 'ARP MSI tetap satu entri' (($hklmPasang.Count -eq 1) -and ($hklmPasang[0].DisplayVersion -eq '1.2.7')) ("$($hklmPasang[0].DisplayName) $($hklmPasang[0].DisplayVersion)") ("$($hklmPasang.Count) entri HKLM")
$arpPasang | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $out 'arp-sesudah-pasang.json') -Encoding UTF8

# ── 3. Jalankan host dari hasil pasang ──────────────────────────────────────
$pHost = Start-Process -FilePath $hostExe -ArgumentList @(('--Database:Path=' + (Join-Path $data 'uji.db')), ('--Jwt:SigningKeyFile=' + (Join-Path $data 'uji.key')), ('--Urls=' + $base)) -WorkingDirectory $dir -RedirectStandardOutput (Join-Path $out 'host-exe.log') -RedirectStandardError (Join-Path $out 'host-exe-err.log') -PassThru
$jawab = $null
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 2
  try { $r = Invoke-WebRequest -Uri "$base/health" -UseBasicParsing -TimeoutSec 5; if ($r.StatusCode -eq 200) { $jawab = $r; break } } catch {}
}
Cek 'host EXE menjawab /health' ($null -ne $jawab) ("percobaan " + ($i + 1)) 'tidak menjawab dalam 120 detik'
$versi = ''
if ($jawab) { try { $versi = ($jawab.Content | ConvertFrom-Json).version } catch {} }
Cek 'health melaporkan 1.2.7' ($versi -eq '1.2.7') ("version=$versi") ("version=$versi")
$spa = $null
try { $spa = Invoke-WebRequest -Uri "$base/" -UseBasicParsing -TimeoutSec 10 } catch {}
Cek 'SPA memuat bundel 1.2.7' (($null -ne $spa) -and ($spa.StatusCode -eq 200) -and ($spa.Content -match 'index-DppHnhYj\.js')) ("status " + $spa.StatusCode) 'SPA tidak 200 / bundel salah'
$aset = $null
try { $aset = Invoke-WebRequest -Uri "$base/assets/index-DppHnhYj.js" -UseBasicParsing -TimeoutSec 20 } catch {}
Cek 'aset JS 1.2.7 dilayani' (($null -ne $aset) -and ($aset.RawContentLength -eq 582985)) ("status " + $aset.StatusCode + ', ' + $aset.RawContentLength + ' bita') 'aset tidak sesuai'

$login = $null
try { $login = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{ username = 'admin'; password = 'admin123' } | ConvertTo-Json) -TimeoutSec 10 } catch {}
Cek 'login admin bawaan berhasil' ($null -ne $login.token) 'token diterima' 'login gagal'
Cek 'akun bawaan wajib ganti sandi' ($login -and $login.mustChangePassword) ("mustChangePassword=" + $login.mustChangePassword) 'mustChangePassword tidak True'

$sandiBaru = 'UjiExe127!'
$kodeGanti = 0
try {
  Invoke-RestMethod -Uri "$base/api/auth/change-password" -Method Post -ContentType 'application/json' -Headers @{ Authorization = ('Bearer ' + $login.token) } -Body (@{ currentPassword = 'admin123'; newPassword = $sandiBaru } | ConvertTo-Json) -TimeoutSec 10 | Out-Null
  $kodeGanti = 200
} catch { try { $kodeGanti = [int]$_.Exception.Response.StatusCode } catch { $kodeGanti = 0 } }
Cek 'sandi bawaan bisa diganti' ($kodeGanti -eq 200) 'http 200' ("http $kodeGanti")
$login2 = $null
try { $login2 = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{ username = 'admin'; password = $sandiBaru } | ConvertTo-Json) -TimeoutSec 10 } catch {}
Cek 'login dengan sandi baru berhasil' ($null -ne $login2.token) 'token diterima' 'login gagal'
Cek 'wajib-ganti-sandi padam' ($login2 -and (-not $login2.mustChangePassword)) ("mustChangePassword=" + $login2.mustChangePassword) 'masih True'

$cekDb = $null
try { $cekDb = Invoke-RestMethod -Uri "$base/api/system/self-check" -Headers @{ Authorization = ('Bearer ' + $login2.token) } -TimeoutSec 30 } catch {}
$gagalCek = -1
$jmlCek = 0
$versiSkema = ''
if ($cekDb) {
  $gagalCek = [int]$cekDb.jumlahGagal
  $jmlCek = [int]$cekDb.jumlahPemeriksaan
  $c = @($cekDb.checks) | Where-Object { $_.name -eq 'versi skema' } | Select-Object -First 1
  if ($c) { $versiSkema = $c.detail }
}
Cek 'periksa-diri basis data scratch: 0 gagal' (($gagalCek -eq 0) -and ($jmlCek -ge 9)) ("$jmlCek pemeriksaan, $gagalCek gagal; $versiSkema") ("$jmlCek pemeriksaan, $gagalCek gagal")
$migrasi = ([regex]::Matches((Get-Content (Join-Path $out 'host-exe.log') -Raw -ErrorAction SilentlyContinue), 'Migrasi \d+ diterapkan')).Count
Cek 'basis data scratch dimigrasi 1..17' ($migrasi -eq 17) ("$migrasi migrasi") ("$migrasi migrasi")
$merek = $null
try { $merek = Invoke-RestMethod -Uri "$base/api/brand" -TimeoutSec 10 } catch {}
Cek 'merek toko terbaca anonim' ($merek -and ($merek.name -eq 'Seruni 22 Bakery') -and (@($merek.PSObject.Properties).Count -eq 3)) ("name=" + $merek.name + ', 3 kunci') 'merek tidak sesuai'
$ukuranDb = 0
if (Test-Path (Join-Path $data 'uji.db')) { $ukuranDb = (Get-Item (Join-Path $data 'uji.db')).Length }
Cek 'basis data scratch terbentuk' ($ukuranDb -gt 0) ("uji.db $ukuranDb bita") 'uji.db tidak terbentuk'
try { Stop-Process -Id $pHost.Id -Force -ErrorAction Stop; Catat 'host EXE dihentikan' 'INFO' 'Stop-Process' } catch { Catat 'host EXE dihentikan' 'INFO' ('gagal: ' + $_.Exception.Message) }
Start-Sleep -Seconds 3
Cek 'tidak ada proses Seruni sebelum copot' (@(Get-Process | Where-Object { $_.ProcessName -like 'Seruni22Bakery*' }).Count -eq 0) '0 proses' 'masih ada proses'

# ── 4. Copot ────────────────────────────────────────────────────────────────
$logCopot = Join-Path $out 'exe-uninstall.log'
$p2 = Start-Process -FilePath (Join-Path $dir 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG=' + $logCopot)) -PassThru
if (-not $p2.WaitForExit(600000)) { try { $p2.Kill() } catch {}; Catat 'copot senyap EXE 1.2.7' 'FAIL' 'melewati 10 menit' } else { Cek 'copot senyap EXE 1.2.7' ($p2.ExitCode -eq 0) ("exit " + $p2.ExitCode) ("exit " + $p2.ExitCode) }
Start-Sleep -Seconds 3
Cek 'folder instalasi EXE hilang' (-not (Test-Path $dir)) 'folder program terhapus' 'folder program masih ada'
Cek 'data uji di luar folder program tetap ada' ((Test-Path (Join-Path $data 'uji.db')) -and (Test-Path (Join-Path $data 'uji.key'))) 'uji.db dan uji.key masih ada' 'data uji ikut terhapus'
Cek 'pintasan per-pengguna EXE hilang' ((-not (Test-Path $lnkMenuDir)) -and (-not (Test-Path $lnkDesk))) 'grup Start Menu dan pintasan Desktop per-pengguna terhapus' 'masih ada pintasan per-pengguna'
Cek 'pintasan MSI tetap utuh setelah copot' (((TargetPintasan 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk') -eq $targetMsi) -and ((TargetPintasan 'C:\Users\Public\Desktop\Seruni 22 Bakery.lnk') -eq $targetMsi)) 'Start Menu ProgramData + Public Desktop tetap menunjuk MSI' 'pintasan MSI hilang/berubah'
$arpCopot = @(DaftarArp)
$arpCopot | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $out 'arp-sesudah-copot.json') -Encoding UTF8
$hkcuCopot = @($arpCopot | Where-Object { $_.Root -like 'HKCU*' })
$hklmCopot = @($arpCopot | Where-Object { $_.Root -like 'HKLM*' })
Cek 'ARP per-pengguna EXE hilang' ($hkcuCopot.Count -eq 0) 'kunci HKCU bersih' (($hkcuCopot | ForEach-Object { $_.DisplayName }) -join ', ')
Cek 'ARP MSI tetap satu entri 1.2.7' (($hklmCopot.Count -eq 1) -and ($hklmCopot[0].DisplayVersion -eq '1.2.7')) ("$($hklmCopot[0].DisplayName) $($hklmCopot[0].DisplayVersion)") (($hklmCopot | ForEach-Object { "$($_.DisplayName) $($_.DisplayVersion)" }) -join '; ')
$sidikDb2 = SidikDbNyata
Cek 'basis data nyata tidak berubah' ($sidikDb -eq $sidikDb2) $sidikDb2 ("sebelum: $sidikDb; sesudah: $sidikDb2")
Cek 'tanpa proses yatim' (@(Get-Process | Where-Object { $_.ProcessName -like 'Seruni22Bakery*' }).Count -eq 0) 'tidak ada proses Seruni22Bakery tersisa' 'masih ada proses'
$jmlMsi = @(Get-ChildItem 'C:\Program Files\Seruni 22 Bakery' -Recurse -File).Count
Cek 'instalasi MSI tetap utuh' (($jmlMsi -eq 393) -and ((Get-Item 'C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.Host.exe').VersionInfo.FileVersion -like '1.2.7*')) ("$jmlMsi berkas, host 1.2.7.0") 'instalasi MSI berubah'

Catat 'rekap' $(if ($gagal -eq 0) { 'PASS' } else { 'FAIL' }) ("lulus=$lulus gagal=$gagal")
