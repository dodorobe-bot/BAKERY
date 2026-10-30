$ErrorActionPreference = 'Continue'
$out = 'C:\tmp\exe-127'
$status = Join-Path $out 'status-hash.txt'
$dir = 'C:\tmp\seruni-exe-127'
$exe = 'D:\bakery-desktop\dist\installer\Seruni22Bakery_Setup_v1.2.7.exe'
$hashPublish = 'C:\tmp\sbx-upgrade-127\hash-publish.txt'

Set-Content -Path $status -Value '' -Encoding UTF8
function Catat($langkah, $hasil, $rincian) {
  $baris = '{0} | {1} | {2} | {3}' -f (Get-Date -Format 'HH:mm:ss'), $langkah, $hasil, $rincian
  Add-Content -Path $status -Value $baris -Encoding UTF8
  Write-Host $baris
}
function Cek($langkah, $kondisi, $lulus, $gagal) { if ($kondisi) { Catat $langkah 'PASS' $lulus } else { Catat $langkah 'FAIL' $gagal } }

$p = Start-Process -FilePath $exe -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR=' + $dir), ('/LOG=' + (Join-Path $out 'exe-install-hash.log'))) -PassThru
if (-not $p.WaitForExit(600000)) { try { $p.Kill() } catch {}; Catat 'pasang senyap EXE' 'FAIL' 'melewati 10 menit'; exit 1 }
Cek 'pasang senyap EXE' ($p.ExitCode -eq 0) ("exit " + $p.ExitCode) ("exit " + $p.ExitCode)

$paket = @{}
foreach ($baris in (Get-Content $hashPublish -Encoding UTF8)) {
  if ($baris.Trim() -eq '') { continue }
  $bagian = $baris.Split('|')
  if ($bagian.Count -eq 3) { $paket[$bagian[0]] = @{ Hash = $bagian[1]; Ukuran = [long]$bagian[2] } }
}
$terpasang = @{}
foreach ($f in @(Get-ChildItem $dir -Recurse -File)) {
  $rel = $f.FullName.Substring($dir.Length + 1)
  $terpasang[$rel] = $f
}

$beda = @()
foreach ($rel in $paket.Keys) {
  if (-not $terpasang.ContainsKey($rel)) { $beda += "HILANG $rel"; continue }
  $f = $terpasang[$rel]
  $h = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
  if (($h -ne $paket[$rel].Hash) -or ($f.Length -ne $paket[$rel].Ukuran)) { $beda += "BEDA $rel" }
}
$hanyaTerpasang = @($terpasang.Keys | Where-Object { -not $paket.ContainsKey($_) } | Sort-Object)
Cek 'seluruh berkas payload identik dengan publish' (($beda.Count -eq 0) -and ($paket.Count -eq 393)) ("$($paket.Count) berkas diperiksa, 0 beda") (($beda[0..([Math]::Min(9, $beda.Count - 1))]) -join '; ')
Catat 'berkas tambahan dari installer' 'INFO' ("$($hanyaTerpasang.Count): " + ($hanyaTerpasang -join ', '))
$diharapkan = @('app.ico', 'unins000.dat', 'unins000.exe', 'Uninstall Seruni 22 Bakery.lnk')
$selisih = @($hanyaTerpasang | Where-Object { $diharapkan -notcontains $_ })
$hilangTambahan = @($diharapkan | Where-Object { $hanyaTerpasang -notcontains $_ })
Cek 'berkas tambahan hanya app.ico + 3 berkas Inno' (($selisih.Count -eq 0) -and ($hilangTambahan.Count -eq 0)) ("app.ico, unins000.exe, unins000.dat, Uninstall Seruni 22 Bakery.lnk") ("tak terduga: " + ($selisih -join ', ') + '; hilang: ' + ($hilangTambahan -join ', '))
Catat 'jumlah berkas terpasang' 'INFO' ("$($terpasang.Count) berkas = 393 payload publish + app.ico + unins000.exe + unins000.dat + Uninstall Seruni 22 Bakery.lnk")

$p2 = Start-Process -FilePath (Join-Path $dir 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG=' + (Join-Path $out 'exe-uninstall-hash.log'))) -PassThru
if (-not $p2.WaitForExit(600000)) { try { $p2.Kill() } catch {}; Catat 'copot senyap EXE' 'FAIL' 'melewati 10 menit'; exit 1 }
Cek 'copot senyap EXE' ($p2.ExitCode -eq 0) ("exit " + $p2.ExitCode) ("exit " + $p2.ExitCode)
Start-Sleep -Seconds 3
Cek 'folder uji hilang' (-not (Test-Path $dir)) 'terhapus' 'masih ada'
Cek 'ARP kembali satu entri MSI' (@(Get-ChildItem 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue | ForEach-Object { (Get-ItemProperty $_.PSPath).DisplayName } | Where-Object { $_ -like '*Seruni*' }).Count -eq 0) 'tidak ada entri per-pengguna' 'masih ada entri per-pengguna'
