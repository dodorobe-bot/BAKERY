$ErrorActionPreference = 'Continue'
$out = 'C:\tmp\exe-127'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$status = Join-Path $out 'status.txt'
Set-Content -Path $status -Value '' -Encoding UTF8

function Catat($langkah, $hasil, $rincian) {
  $baris = '{0} | {1} | {2} | {3}' -f (Get-Date -Format 'HH:mm:ss'), $langkah, $hasil, $rincian
  Add-Content -Path $status -Value $baris -Encoding UTF8
  Write-Host $baris
}

$exe = 'D:\bakery-desktop\dist\installer\Seruni22Bakery_Setup_v1.2.7.exe'
$fi = Get-Item $exe
$h = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
Catat 'EXE 1.2.7 utuh' $(if (($fi.Length -eq 42641033) -and ($h -eq 'adb779c1d6c4ceb1c424ae3700b6f435e6223ae372104827d3ef4b9a943cd36b')) { 'PASS' } else { 'FAIL' }) ("$($fi.Length) bita, SHA256 $h")

$proses = @(Get-Process | Where-Object { $_.ProcessName -like 'Seruni22Bakery*' })
Catat 'tidak ada proses Seruni berjalan sebelum uji' $(if ($proses.Count -eq 0) { 'PASS' } else { 'FAIL' }) ("$($proses.Count) proses: " + (($proses | ForEach-Object { $_.ProcessName + ':' + $_.Id }) -join ', '))

$arp = @()
foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
                    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
                    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
  foreach ($k in @(Get-ChildItem $root -ErrorAction SilentlyContinue)) {
    $p = Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue
    if ($p.DisplayName -like '*Seruni*') {
      $arp += [pscustomobject]@{ Root = $root; Key = $k.PSChildName; DisplayName = $p.DisplayName; DisplayVersion = $p.DisplayVersion }
    }
  }
}
Catat 'ARP sebelum uji: tepat satu entri MSI 1.2.7' $(if (($arp.Count -eq 1) -and ($arp[0].DisplayVersion -eq '1.2.7') -and ($arp[0].Root -like 'HKLM*')) { 'PASS' } else { 'FAIL' }) (($arp | ForEach-Object { "$($_.DisplayName) $($_.DisplayVersion) [$($_.Key)]" }) -join '; ')
$arp | ConvertTo-Json | Set-Content -Path (Join-Path $out 'arp-sebelum.json') -Encoding UTF8

$w = New-Object -ComObject WScript.Shell
foreach ($lnk in @('C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk',
                   'C:\Users\Public\Desktop\Seruni 22 Bakery.lnk',
                   'C:\Users\Administrator\Desktop\Seruni 22 Bakery.lnk',
                   'C:\Users\Administrator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery\Seruni 22 Bakery.lnk',
                   'C:\Users\Administrator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery\Uninstall Seruni 22 Bakery.lnk')) {
  if (Test-Path $lnk) {
    $t = $w.CreateShortcut($lnk).TargetPath
    Catat 'pintasan sebelum uji' 'INFO' ("$lnk -> $t")
  } else {
    Catat 'pintasan sebelum uji' 'INFO' ("$lnk TIDAK ADA")
  }
}
foreach ($d in @('C:\Users\Administrator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery',
                 'C:\Users\Administrator\AppData\Local\Programs\Seruni22Bakery',
                 'C:\tmp\seruni-exe-127')) {
  Catat 'jalur sebelum uji' 'INFO' ("$d " + $(if (Test-Path $d) { 'ADA' } else { 'tidak ada' }))
}

$db = 'C:\Users\Administrator\AppData\Local\Seruni22Bakery\seruni22bakery.db'
if (Test-Path $db) {
  $dbi = Get-Item $db
  Catat 'basis data nyata sebelum uji' 'INFO' ("$($dbi.Length) bita, mtime " + $dbi.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss.fff') + ', -wal ' + $(if (Test-Path ($db + '-wal')) { 'ADA' } else { 'tidak ada' }) + ', -shm ' + $(if (Test-Path ($db + '-shm')) { 'ADA' } else { 'tidak ada' }))
} else {
  Catat 'basis data nyata sebelum uji' 'INFO' 'tidak ditemukan'
}

$hostExe = 'C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.Host.exe'
if (Test-Path $hostExe) {
  Catat 'instalasi MSI sebelum uji' 'INFO' ((Get-Item $hostExe).VersionInfo.FileVersion + '; ' + @(Get-ChildItem 'C:\Program Files\Seruni 22 Bakery' -Recurse -File).Count + ' berkas')
}
Catat 'ruang disk C:' 'INFO' ((Get-PSDrive C).Free/1GB).ToString('0.0') + ' GB bebas'
