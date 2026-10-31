$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

# Pagar keselamatan: skrip ini memanggil shutdown.exe dan menulis ke folder data pengguna.
# Hanya boleh berjalan di dalam Windows Sandbox; di mesin biasa pemanggilan shutdown akan
# mematikan komputer pemilik dan penanda basis data palsu bisa masuk ke folder data nyata.
if ($env:USERNAME -ne 'WDAGUtilityAccount') {
  Write-Host 'BERHENTI: uji ini hanya boleh dijalankan di dalam Windows Sandbox.'
  Write-Host ("Pengguna sekarang: " + $env:USERNAME + ". Tidak ada tindakan yang dijalankan.")
  exit 3
}

# Uji paket 1.2.9 di Windows Sandbox bersih:
#   A. MSI 1.2.7 dipasang, data diisi lewat API; EXE 1.2.9 harus menolak berjalan.
#   B. MSI ditingkatkan 1.2.7 -> 1.2.9; berkas, merek, produk, dan skema 18 diperiksa.
#   C. MSI 1.2.7 tidak boleh dipasang di atas 1.2.9.
#   D. MSI dicopot; EXE 1.2.9 dipasang per pengguna dan dijalankan.
#   E. EXE 1.2.7 tidak boleh dipasang di atas EXE 1.2.9.
#   F. MSI 1.2.9 harus menolak berjalan selama EXE terpasang.
#   G. Copot EXE: folder program hilang, folder data pengguna tetap.

$share  = 'C:\share'
$bukti  = Join-Path $share 'bukti'
$status = Join-Path $bukti 'status.txt'
$selesai = Join-Path $bukti 'selesai.txt'

$msi127 = Join-Path $share 'Seruni22Bakery_v1.2.7_x64.msi'
$msi129 = Join-Path $share 'Seruni22Bakery_v1.2.9_x64.msi'
$exe127 = Join-Path $share 'Seruni22Bakery_Setup_v1.2.7.exe'
$exe129 = Join-Path $share 'Seruni22Bakery_Setup_v1.2.9.exe'
$manifes = Join-Path $share 'hash-publish-129.txt'

$shaMsi127  = '5C4C31F87ECB96E23B5630238BA59D72C9F115904CD98A8AC13D6953FE20DFBB'
$shaMsi129  = '4CF092CE05CFB0301D2740CC136322135DC9F506A482C807BEA020782052D635'
$shaExe127  = 'ADB779C1D6C4CEB1C424AE3700B6F435E6223AE372104827D3EF4B9A943CD36B'
$shaExe129  = 'FD233BE4E4A8441DD2E63349B723466EE4AB803F41E6D6B357B97E05F55143B7'
$ukuranMsi127 = 50373479
$ukuranMsi129 = 50391554
$ukuranExe127 = 42641033
$ukuranExe129 = 42662274

$kode127 = '{A0AEA895-FC09-4312-8E24-8C9E0FEABDC8}'
$kode129 = '{7609C8A3-2349-4606-A02D-652FE121FB78}'

$instal = 'C:\Program Files\Seruni 22 Bakery'
$peluncur = Join-Path $instal 'Seruni22Bakery.exe'
$hostExe  = Join-Path $instal 'Seruni22Bakery.Host.exe'
$instalExe = 'C:\SeruniExe129'
$peluncurExe = Join-Path $instalExe 'Seruni22Bakery.exe'
$hostExeExe  = Join-Path $instalExe 'Seruni22Bakery.Host.exe'

$dbPath  = 'C:\data\uji.db'
$keyPath = 'C:\data\uji.key'
$dbExePath  = 'C:\data\uji-exe.db'
$keyExePath = 'C:\data\uji-exe.key'
$sandiBaru = 'SandboxRoti129!'
$logoPng = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='

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

function Baca-Teks($path) {
  if (-not (Test-Path $path)) { return '' }
  $b = [IO.File]::ReadAllBytes($path)
  if (($b.Length -ge 2) -and ($b[0] -eq 0xFF) -and ($b[1] -eq 0xFE)) {
    return [Text.Encoding]::Unicode.GetString($b, 2, $b.Length - 2)
  }
  if (($b.Length -ge 2) -and ($b[0] -eq 0x3C) -and ($b[1] -eq 0x00)) {
    return [Text.Encoding]::Unicode.GetString($b)
  }
  return [Text.Encoding]::UTF8.GetString($b)
}

function Jalankan-Msi($aksi, $berkas, $kode, $log) {
  if ($aksi -eq '/x') { $arg = @('/x', $kode, '/qn', '/norestart', '/l*v', $log) }
  else { $arg = @($aksi, $berkas, '/qn', '/norestart', '/l*v', $log) }
  $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arg -PassThru
  if (-not $p.WaitForExit(300000)) { try { $p.Kill() } catch {}; return -1 }
  return $p.ExitCode
}

function Jalankan-Inno($berkas, $argumen, $log) {
  $arg = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG=' + $log)) + $argumen
  $p = Start-Process -FilePath $berkas -ArgumentList $arg -PassThru
  if (-not $p.WaitForExit(600000)) { try { $p.Kill() } catch {}; return -1 }
  return $p.ExitCode
}

function Cari-Arp($kode) {
  foreach ($k in @("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$kode",
                   "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$kode",
                   "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$kode")) {
    if (Test-Path $k) { return Get-ItemProperty $k }
  }
  return $null
}

function Daftar-ArpSeruni {
  $semua = @()
  foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
                      'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
                      'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
    if (-not (Test-Path $root)) { continue }
    foreach ($k in Get-ChildItem $root -ErrorAction SilentlyContinue) {
      $p = Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue
      if ($p -and $p.DisplayName -like '*Seruni*') {
        $semua += ('{0} {1} [{2}]' -f $p.DisplayName, $p.DisplayVersion, $k.PSChildName)
      }
    }
  }
  return $semua
}

function Target-Pintasan($path) {
  if (-not (Test-Path $path)) { return '' }
  $w = New-Object -ComObject WScript.Shell
  return $w.CreateShortcut($path).TargetPath
}

function Tunggu-Health($port, $batasDetik) {
  for ($i = 0; $i -lt $batasDetik; $i += 2) {
    Start-Sleep -Seconds 2
    try {
      $r = Invoke-WebRequest -Uri ("http://127.0.0.1:$port/health") -UseBasicParsing -TimeoutSec 5
      if ($r.StatusCode -eq 200) { return $r }
    } catch {}
  }
  return $null
}

function Api($metode, $url, $token, $badan) {
  $h = @{}
  if ($token) { $h['Authorization'] = "Bearer $token" }
  try {
    if ($null -ne $badan) {
      return Invoke-RestMethod -Uri $url -Method $metode -Headers $h -ContentType 'application/json' -Body $badan -TimeoutSec 30
    }
    return Invoke-RestMethod -Uri $url -Method $metode -Headers $h -TimeoutSec 30
  } catch {
    $kode = ''
    try { $kode = [int]$_.Exception.Response.StatusCode } catch {}
    Catat 'api' 'INFO' ("$metode $url gagal (http $kode): " + $_.Exception.Message)
    return $null
  }
}

function Hentikan-Host($proses) {
  if ($null -eq $proses) { return }
  try { Stop-Process -Id $proses.Id -Force -ErrorAction Stop; Catat 'host dihentikan' 'INFO' 'Stop-Process' }
  catch { Catat 'host dihentikan' 'INFO' ('gagal: ' + $_.Exception.Message) }
  Start-Sleep -Seconds 3
}

function Ambil($obj, $nama) {
  if ($null -eq $obj) { return $null }
  foreach ($p in $obj.PSObject.Properties) { if ($p.Name -ieq $nama) { return $p.Value } }
  return $null
}

function Muat-Manifes {
  $harap = @{}
  foreach ($baris in Get-Content $manifes) {
    $bagian = $baris.Split('|')
    if ($bagian.Count -eq 3) { $harap[$bagian[0]] = '{0}|{1}' -f $bagian[1], $bagian[2] }
  }
  return $harap
}

function Banding-Payload($folder, $wajibSamaPersis) {
  $harap = Muat-Manifes
  $berkas = @(Get-ChildItem -Path $folder -Recurse -File -ErrorAction SilentlyContinue)
  $beda = @(); $hanyaPasang = @(); $cocok = 0
  foreach ($f in $berkas) {
    $rel = $f.FullName.Substring($folder.Length + 1)
    if (-not $harap.ContainsKey($rel)) { $hanyaPasang += $rel; continue }
    $h = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
    if ($harap[$rel] -eq ('{0}|{1}' -f $h, $f.Length)) { $cocok++ } else { $beda += $rel }
    $harap.Remove($rel)
  }
  $hanyaPaket = @($harap.Keys)
  $ok = ($beda.Count -eq 0) -and ($hanyaPaket.Count -eq 0) -and ($cocok -eq 393)
  if ($wajibSamaPersis) { $ok = $ok -and ($hanyaPasang.Count -eq 0) }
  return [pscustomobject]@{
    Ok = $ok; Cocok = $cocok; Beda = $beda; HanyaPasang = $hanyaPasang; HanyaPaket = $hanyaPaket
    Total = $berkas.Count
    Rincian = ("cocok=$cocok beda=" + $beda.Count + " hanya-terpasang=" + $hanyaPasang.Count + " hanya-paket=" + $hanyaPaket.Count + " total=" + $berkas.Count)
  }
}

function Cek-BasisData($base, $token, $label, $port) {
  $cekDb = Api 'GET' "$base/api/system/self-check" $token $null
  $gagalCek = -1; $jmlCek = 0; $versiSkema = ''
  if ($cekDb) {
    $gagalCek = [int](Ambil $cekDb 'jumlahGagal')
    $jmlCek = [int](Ambil $cekDb 'jumlahPemeriksaan')
    foreach ($c in @(Ambil $cekDb 'checks')) {
      if ([string](Ambil $c 'name') -eq 'versi skema') { $versiSkema = [string](Ambil $c 'detail') }
    }
  }
  Cek "$label periksa-diri basis data: 0 gagal" (($gagalCek -eq 0) -and ($jmlCek -ge 9)) ("$jmlCek pemeriksaan, $gagalCek gagal") ("$jmlCek pemeriksaan, $gagalCek gagal")
  Cek "$label skema terpasang 18" ($versiSkema -match 'terpasang 18' -and $versiSkema -match 'terkemas 18') $versiSkema ('detail: ' + $versiSkema)
}

# -- 1. Lingkungan bersih ----------------------------------------------------
$os  = Get-CimInstance Win32_OperatingSystem
$id  = [Security.Principal.WindowsIdentity]::GetCurrent()
$adm = (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Catat 'lingkungan' 'INFO' ($os.Caption + ' build ' + $os.BuildNumber + '; pengguna ' + $id.Name + '; elevated=' + $adm + '; RAM ' + [math]::Round($os.TotalVisibleMemorySize / 1024) + ' MB')
Cek 'mesin bersih: tanpa runtime .NET bersama' (-not (Test-Path 'C:\Program Files\dotnet')) 'C:\Program Files\dotnet tidak ada (publish self-contained)' 'C:\Program Files\dotnet ada; klaim mesin bersih gugur'
$arpAwal = @(Daftar-ArpSeruni)
Cek 'mesin bersih: belum ada instalasi Seruni' ((-not (Test-Path $instal)) -and (-not (Test-Path $instalExe)) -and ($arpAwal.Count -eq 0)) 'tidak ada folder program dan tidak ada entri ARP' ('sudah ada sebelum uji: ' + ($arpAwal -join '; '))
Cek 'mesin bersih: tanpa penanda kanal' `
  ((-not (Test-Path 'HKCU:\Software\Seruni22Bakery')) -and (-not (Test-Path 'HKLM:\Software\Seruni22Bakery'))) `
  'HKCU dan HKLM Software\Seruni22Bakery tidak ada' 'penanda kanal sudah ada sebelum uji'

# -- 2. Keutuhan salinan berkas paket ----------------------------------------
function Cek-Berkas($path, $sha, $ukuran, $label) {
  if (-not (Test-Path $path)) { Cek $label $false "$label tidak ada" "$path tidak ditemukan"; return }
  $f = Get-Item $path
  $h = (Get-FileHash $path -Algorithm SHA256).Hash
  Cek $label (($h -eq $sha) -and ($f.Length -eq $ukuran)) ("$($f.Length) byte, SHA256 $h") ("ukuran/hash tidak cocok: $($f.Length) byte $h")
}
Cek-Berkas $msi127 $shaMsi127 $ukuranMsi127 'salinan MSI 1.2.7 utuh'
Cek-Berkas $msi129 $shaMsi129 $ukuranMsi129 'salinan MSI 1.2.9 utuh'
Cek-Berkas $exe127 $shaExe127 $ukuranExe127 'salinan EXE 1.2.7 utuh'
Cek-Berkas $exe129 $shaExe129 $ukuranExe129 'salinan EXE 1.2.9 utuh'
Cek 'manifes payload 1.2.9 terbaca' ((Get-Content $manifes -ErrorAction SilentlyContinue).Count -eq 393) ("$((Get-Content $manifes -ErrorAction SilentlyContinue).Count) baris") 'manifes tidak berisi 393 baris'

# -- 3. Pasang MSI 1.2.7 -----------------------------------------------------
$k127 = Jalankan-Msi '/i' $msi127 $null (Join-Path $bukti 'msi-127-install.log')
Cek 'pasang senyap MSI 1.2.7' ($k127 -eq 0) ("exit $k127") ("exit $k127")
$arp127 = Cari-Arp $kode127
Cek 'entri ARP 1.2.7' (($null -ne $arp127) -and ($arp127.DisplayVersion -eq '1.2.7')) ("{0} {1}" -f $arp127.DisplayName, $arp127.DisplayVersion) 'ARP 1.2.7 tidak ada/versi salah'
Cek 'versi peluncur 1.2.7' ((Get-Item $peluncur).VersionInfo.FileVersion -like '1.2.7*') ((Get-Item $peluncur).VersionInfo.FileVersion) 'versi peluncur 1.2.7 tidak sesuai'

# -- 4. Isi data lewat host 1.2.7 --------------------------------------------
New-Item -ItemType Directory -Force -Path 'C:\data' | Out-Null
$outHost = Join-Path $bukti 'host-127.log'
$pHost = Start-Process -FilePath $hostExe -ArgumentList @("--Database:Path=$dbPath", "--Jwt:SigningKeyFile=$keyPath", '--Urls=http://127.0.0.1:5311') -WorkingDirectory $instal -RedirectStandardOutput $outHost -RedirectStandardError (Join-Path $bukti 'host-127-err.log') -PassThru
$jawab = Tunggu-Health 5311 120
Cek 'host 1.2.7 menjawab /health' ($null -ne $jawab) 'health 200' 'host 1.2.7 tidak menjawab dalam 120 detik'
$versiHealth = ''
if ($null -ne $jawab) { try { $versiHealth = ($jawab.Content | ConvertFrom-Json).version } catch {} }
Cek 'health melaporkan 1.2.7' ($versiHealth -eq '1.2.7') ("version=$versiHealth") ("version=$versiHealth")

$base = 'http://127.0.0.1:5311'
$masuk = Api 'POST' "$base/api/auth/login" $null (@{ username = 'admin'; password = 'admin123' } | ConvertTo-Json)
$token = $null
if ($masuk) { $token = $masuk.token }
Cek 'login bawaan admin berhasil di 1.2.7' ($null -ne $token) 'token diterima' 'login gagal'
$ganti = Api 'POST' "$base/api/auth/change-password" $token (@{ currentPassword = 'admin123'; newPassword = $sandiBaru } | ConvertTo-Json)
$masuk2 = Api 'POST' "$base/api/auth/login" $null (@{ username = 'admin'; password = $sandiBaru } | ConvertTo-Json)
$token2 = $null
if ($masuk2) { $token2 = $masuk2.token }
Cek 'sandi diganti lalu login sandi baru' ($null -ne $token2) 'login dengan sandi baru berhasil' 'login dengan sandi baru gagal'

$produk = Api 'POST' "$base/api/products" $token2 (@{ name = 'Roti Uji 129'; category = 'Roti'; sellingPriceMinorUnits = 15000; lines = @(); barcode = $null } | ConvertTo-Json)
$idProduk = 0
if ($produk) { $idProduk = [int]$produk.id }
Cek 'produk uji dibuat di 1.2.7' ($idProduk -gt 0) ("id=$idProduk") 'produk tidak terbuat'
$setMerek = Api 'PUT' "$base/api/settings" $token2 (@{ brand_name = 'Toko Uji Sandbox 129' } | ConvertTo-Json)
$merek127 = Api 'GET' "$base/api/brand" $null $null
$namaMerek127 = [string](Ambil $merek127 'name')
$kunci127 = 0
if ($merek127) { $kunci127 = @($merek127.PSObject.Properties).Count }
Cek 'merek disetel di 1.2.7' ($namaMerek127 -eq 'Toko Uji Sandbox 129') ("name=$namaMerek127") ("name=$namaMerek127")
Catat 'kunci /api/brand 1.2.7' 'INFO' ("$kunci127 kunci")
Hentikan-Host $pHost
$ukuranDbPra = 0
if (Test-Path $dbPath) { $ukuranDbPra = (Get-Item $dbPath).Length }
Cek 'basis data pra-peningkatan terbentuk' ($ukuranDbPra -gt 0) ("uji.db $ukuranDbPra bita") 'uji.db tidak terbentuk'
# -wal dan -shm TIDAK dihapus: host dihentikan paksa, sehingga transaksi yang sudah commit
# bisa masih berada di WAL. Menghapus WAL di sini akan membuang sandi, produk, dan merek
# yang baru dibuat dan membuat pemeriksaan peningkatan gagal secara palsu.

# -- 5. EXE 1.2.9 harus menolak berjalan selama MSI terpasang ----------------
$logGuardExe = Join-Path $bukti 'exe-129-guard.log'
$kGuard = Jalankan-Inno $exe129 @(('/DIR=' + $instalExe)) $logGuardExe
$isiGuard = Baca-Teks $logGuardExe
Cek 'EXE 1.2.9 menolak dipasang selama MSI 1.2.7 ada' ($kGuard -ne 0) ("exit $kGuard") ("exit $kGuard, seharusnya bukan 0")
Cek 'log EXE menyebut MSI terdeteksi' ($isiGuard -match 'MSI sudah terpasang|Program dan Fitur MSI ditemukan') 'alasan kanal tercatat di log' 'log tidak memuat alasan kanal'
Cek 'tidak ada folder EXE setelah penolakan' (-not (Test-Path $instalExe)) 'folder tidak dibuat' 'folder uji EXE terbentuk'
Cek 'tidak ada entri ARP EXE setelah penolakan' ($null -eq (Cari-Arp '{5D9D507C-B7C6-4C4B-A6EE-6786A01F909A}_is1')) 'ARP EXE tidak ada' 'ARP EXE terbentuk'
Cek 'penolakan tidak menulis penanda HKCU' (-not (Test-Path 'HKCU:\Software\Seruni22Bakery\Channel')) 'Channel EXE tidak ditulis' 'penanda HKCU tertulis walau ditolak'
Cek 'MSI 1.2.7 tetap satu-satunya instalasi' (@(Daftar-ArpSeruni).Count -eq 1) (@(Daftar-ArpSeruni) -join '; ') (@(Daftar-ArpSeruni) -join '; ')

# -- 6. Tingkatkan MSI 1.2.7 -> 1.2.9 ----------------------------------------
$logUpgrade = Join-Path $bukti 'msi-129-upgrade.log'
$kUp = Jalankan-Msi '/i' $msi129 $null $logUpgrade
$isiLog = Baca-Teks $logUpgrade
Cek 'peningkatan senyap MSI 1.2.9' ($kUp -eq 0) ("exit $kUp") ("exit $kUp")
Cek 'produk 1.2.7 terdeteksi sebagai sasaran peningkatan' ($isiLog -match [regex]::Escape($kode127)) ("WIX_UPGRADE_DETECTED memuat $kode127") 'ProductCode 1.2.7 tidak muncul di log peningkatan'
Cek 'log MSI menyatakan sukses' ($isiLog -match 'Installation success or error status: 0') 'status 0' 'log tidak menyatakan status 0'
Cek 'RemoveExistingProducts dijalankan' ($isiLog -match 'RemoveExistingProducts') 'produk lama dibongkar' 'produk lama tidak dibongkar'
$arp129 = Cari-Arp $kode129
Cek 'entri ARP 1.2.9' (($null -ne $arp129) -and ($arp129.DisplayVersion -eq '1.2.9')) ("{0} {1}" -f $arp129.DisplayName, $arp129.DisplayVersion) 'ARP 1.2.9 tidak ada/versi salah'
Cek 'entri ARP 1.2.7 hilang' ($null -eq (Cari-Arp $kode127)) 'kunci 1.2.7 tidak ada lagi' 'kunci 1.2.7 masih ada'
Cek 'tepat satu entri Seruni sesudah peningkatan' (@(Daftar-ArpSeruni).Count -eq 1) (@(Daftar-ArpSeruni) -join '; ') (@(Daftar-ArpSeruni) -join '; ')

$payload = Banding-Payload $instal $true
Cek 'seluruh 393 berkas MSI 1.2.9 identik dengan manifes' $payload.Ok $payload.Rincian $payload.Rincian
Cek 'versi peluncur 1.2.9' ((Get-Item $peluncur).VersionInfo.FileVersion -like '1.2.9*') ((Get-Item $peluncur).VersionInfo.FileVersion) 'versi peluncur tidak sesuai'
Cek 'versi host 1.2.9.0' ((Get-Item $hostExe).VersionInfo.FileVersion -like '1.2.9*') ((Get-Item $hostExe).VersionInfo.FileVersion) 'versi host tidak sesuai'
Cek 'Host.dll sama dengan manifes' ((Get-FileHash (Join-Path $instal 'Seruni22Bakery.Host.dll') -Algorithm SHA256).Hash -eq 'EEE0641F2816436741E98C00F378B284E96E77766B378D7884D09B3FC1C828DB') 'hash Host.dll cocok' 'hash Host.dll berbeda'
Cek 'bundel 1.2.9 terpasang, bundel 1.2.7 tersapu' `
  ((Test-Path (Join-Path $instal 'wwwroot\assets\index-BksMbgv0.js')) -and (Test-Path (Join-Path $instal 'wwwroot\assets\index-CoYZy02P.css')) -and (-not (Test-Path (Join-Path $instal 'wwwroot\assets\index-DppHnhYj.js')))) `
  'index-BksMbgv0.js + index-CoYZy02P.css ada; index-DppHnhYj.js tidak tersisa' 'bundel tidak sesuai'
$marker = Get-ItemProperty 'HKLM:\Software\Seruni22Bakery' -ErrorAction SilentlyContinue
Cek 'penanda kanal HKLM milik MSI' (($null -ne $marker) -and ($marker.Channel -eq 'msi') -and ($marker.Version -eq '1.2.9')) ("Channel=$($marker.Channel) Version=$($marker.Version) InstallDir=$($marker.InstallDir)") 'penanda HKLM tidak sesuai'
$lnkMenu = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk'
$lnkDesk = 'C:\Users\Public\Desktop\Seruni 22 Bakery.lnk'
Cek 'pintasan menunjuk instalasi 1.2.9' (((Target-Pintasan $lnkMenu) -eq $peluncur) -and ((Target-Pintasan $lnkDesk) -eq $peluncur)) ('target ' + (Target-Pintasan $lnkMenu)) 'pintasan hilang/target berubah'

# -- 7. Data dan skema sesudah peningkatan -----------------------------------
$outHost2 = Join-Path $bukti 'host-129.log'
$pHost2 = Start-Process -FilePath $hostExe -ArgumentList @("--Database:Path=$dbPath", "--Jwt:SigningKeyFile=$keyPath", '--Urls=http://127.0.0.1:5312') -WorkingDirectory $instal -RedirectStandardOutput $outHost2 -RedirectStandardError (Join-Path $bukti 'host-129-err.log') -PassThru
$jawab2 = Tunggu-Health 5312 120
Cek 'host hasil peningkatan menjawab /health' ($null -ne $jawab2) 'health 200' 'host 1.2.9 tidak menjawab dalam 120 detik'
$versiHealth2 = ''
if ($null -ne $jawab2) { try { $versiHealth2 = ($jawab2.Content | ConvertFrom-Json).version } catch {} }
Cek 'health melaporkan 1.2.9' ($versiHealth2 -eq '1.2.9') ("version=$versiHealth2") ("version=$versiHealth2")
$base2 = 'http://127.0.0.1:5312'
$masuk3 = Api 'POST' "$base2/api/auth/login" $null (@{ username = 'admin'; password = $sandiBaru } | ConvertTo-Json)
$token3 = $null
if ($masuk3) { $token3 = $masuk3.token }
Cek 'sandi dari 1.2.7 masih berlaku di 1.2.9' ($null -ne $token3) 'login dengan sandi hasil ganti berhasil' 'login gagal setelah peningkatan'
$wajibGanti3 = $true
if ($masuk3) { $wajibGanti3 = [bool]$masuk3.mustChangePassword }
Cek 'status wajib-ganti-sandi tidak menyala lagi' (-not $wajibGanti3) "mustChangePassword=$wajibGanti3" "mustChangePassword=$wajibGanti3"
$produk129 = Api 'GET' "$base2/api/products/$idProduk" $token3 $null
$cocokProduk = $false
$rincianProduk = 'tidak terbaca'
if ($produk129) {
  $idP = [int](Ambil $produk129 'id'); $namaP = [string](Ambil $produk129 'name'); $hargaP = [long](Ambil $produk129 'sellingPriceMinorUnits')
  $rincianProduk = "id=$idP name=$namaP harga=$hargaP"
  $cocokProduk = ($idP -eq $idProduk) -and ($namaP -eq 'Roti Uji 129') -and ($hargaP -eq 15000)
}
Cek 'produk dari 1.2.7 terbaca utuh di 1.2.9' $cocokProduk $rincianProduk 'produk tidak ditemukan/nilainya berubah'
$merek129 = Api 'GET' "$base2/api/brand" $null $null
$namaMerek129 = [string](Ambil $merek129 'name')
$kunci129 = 0
if ($merek129) { $kunci129 = @($merek129.PSObject.Properties).Count }
Cek 'merek dari 1.2.7 masih tersimpan' ($namaMerek129 -eq 'Toko Uji Sandbox 129') ("name=$namaMerek129") ("name=$namaMerek129")
Cek 'endpoint /api/brand anonim 4 kunci' ($kunci129 -eq 4) ("$kunci129 kunci") ("$kunci129 kunci")
$setLogo = Api 'PUT' "$base2/api/settings" $token3 (@{ brand_logo = $logoPng } | ConvertTo-Json)
$merekLogo = Api 'GET' "$base2/api/brand" $null $null
Cek 'logo bisa dipasang di 1.2.9 hasil peningkatan' ([string](Ambil $merekLogo 'logo') -eq $logoPng) 'logo kembali sama persis' 'logo tidak kembali sama'
Cek-BasisData $base2 $token3 'peningkatan' 5312
$migrasiBaru = ([regex]::Matches((Baca-Teks $outHost2), 'Migrasi 18 diterapkan')).Count
Cek 'migrasi 18 dijalankan sekali pada basis data lama' ($migrasiBaru -eq 1) "Migrasi 18 diterapkan x$migrasiBaru" "Migrasi 18 diterapkan x$migrasiBaru, seharusnya 1"
Hentikan-Host $pHost2
Cek 'basis data tetap ada setelah dipakai 1.2.9' ((Test-Path $dbPath) -and ((Get-Item $dbPath).Length -gt 0)) ("uji.db $((Get-Item $dbPath).Length) bita") 'uji.db hilang'

# -- 8. Turun versi MSI harus ditolak ---------------------------------------
$logTurunMsi = Join-Path $bukti 'msi-127-downgrade.log'
$kTurunMsi = Jalankan-Msi '/i' $msi127 $null $logTurunMsi
$isiTurunMsi = Baca-Teks $logTurunMsi
Cek 'MSI 1.2.7 ditolak di atas 1.2.9' ($kTurunMsi -ne 0) ("exit $kTurunMsi") ("exit $kTurunMsi, seharusnya bukan 0")
Cek 'log penolakan menyebut alasan turun versi' ($isiTurunMsi -match 'WIX_DOWNGRADE_DETECTED|Versi yang lebih baru') 'syarat turun versi tercatat' 'syarat turun versi tidak tercatat'
Cek 'versi terpasang tetap 1.2.9' (((Cari-Arp $kode129).DisplayVersion) -eq '1.2.9') ((Cari-Arp $kode129).DisplayVersion) 'versi berubah setelah percobaan turun'

# -- 9. Copot MSI 1.2.9 -----------------------------------------------------
$kCopot = Jalankan-Msi '/x' $null $kode129 (Join-Path $bukti 'msi-129-uninstall.log')
Cek 'copot senyap MSI 1.2.9' ($kCopot -eq 0) ("exit $kCopot") ("exit $kCopot")
Start-Sleep -Seconds 3
Cek 'folder instalasi MSI hilang' (-not (Test-Path $instal)) 'folder program terhapus' 'folder program masih ada'
Cek 'pintasan MSI hilang' ((-not (Test-Path $lnkMenu)) -and (-not (Test-Path $lnkDesk))) 'Start Menu dan Desktop publik terhapus' 'pintasan masih ada'
Cek 'tidak ada entri ARP tersisa' (@(Daftar-ArpSeruni).Count -eq 0) 'daftar program bersih' (@(Daftar-ArpSeruni) -join '; ')
Cek 'penanda HKLM ikut terhapus' (-not (Test-Path 'HKLM:\Software\Seruni22Bakery')) 'HKLM Software\Seruni22Bakery hilang' 'penanda HKLM masih ada'
Cek 'data di luar folder program tetap utuh' ((Test-Path $dbPath) -and (Test-Path $keyPath)) 'uji.db dan uji.key masih ada setelah copot' 'data uji ikut terhapus'

# -- 10. Pasang EXE 1.2.9 per pengguna --------------------------------------
$logExe = Join-Path $bukti 'exe-129-install.log'
$kExe = Jalankan-Inno $exe129 @(('/DIR=' + $instalExe)) $logExe
Cek 'pasang senyap EXE 1.2.9' ($kExe -eq 0) ("exit $kExe") ("exit $kExe")
$payloadExe = Banding-Payload $instalExe $false
Cek '393 berkas EXE 1.2.9 identik dengan manifes' $payloadExe.Ok $payloadExe.Rincian $payloadExe.Rincian
Cek 'versi peluncur EXE 1.2.9' ((Get-Item $peluncurExe).VersionInfo.FileVersion -like '1.2.9*') ((Get-Item $peluncurExe).VersionInfo.FileVersion) 'versi peluncur EXE tidak sesuai'
Cek 'versi host EXE 1.2.9.0' ((Get-Item $hostExeExe).VersionInfo.FileVersion -like '1.2.9*') ((Get-Item $hostExeExe).VersionInfo.FileVersion) 'versi host EXE tidak sesuai'
$markerExe = Get-ItemProperty 'HKCU:\Software\Seruni22Bakery' -ErrorAction SilentlyContinue
Cek 'penanda kanal HKCU milik EXE' (($null -ne $markerExe) -and ($markerExe.Channel -eq 'exe') -and ($markerExe.Version -eq '1.2.9')) ("Channel=$($markerExe.Channel) Version=$($markerExe.Version) InstallDir=$($markerExe.InstallDir)") 'penanda HKCU tidak sesuai'
$lnkMenuExe = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk'
$lnkDeskExe = Join-Path $env:USERPROFILE 'Desktop\Seruni 22 Bakery.lnk'
Cek 'pintasan per-pengguna menunjuk folder uji' (((Target-Pintasan $lnkMenuExe) -eq $peluncurExe) -and ((Target-Pintasan $lnkDeskExe) -eq $peluncurExe)) ('target ' + (Target-Pintasan $lnkMenuExe)) 'pintasan per-pengguna salah'
$arpExe = Cari-Arp '{5D9D507C-B7C6-4C4B-A6EE-6786A01F909A}_is1'
Cek 'entri ARP EXE 1.2.9' (($null -ne $arpExe) -and ($arpExe.DisplayVersion -eq '1.2.9')) ("{0} {1}" -f $arpExe.DisplayName, $arpExe.DisplayVersion) 'ARP EXE tidak ada/versi salah'

# -- 11. Host dari folder EXE dijalankan pada basis data baru ---------------
$outHost3 = Join-Path $bukti 'host-exe-129.log'
$pHost3 = Start-Process -FilePath $hostExeExe -ArgumentList @("--Database:Path=$dbExePath", "--Jwt:SigningKeyFile=$keyExePath", '--Urls=http://127.0.0.1:5313') -WorkingDirectory $instalExe -RedirectStandardOutput $outHost3 -RedirectStandardError (Join-Path $bukti 'host-exe-129-err.log') -PassThru
$jawab3 = Tunggu-Health 5313 120
Cek 'host dari folder EXE menjawab /health' ($null -ne $jawab3) 'health 200' 'host EXE tidak menjawab dalam 120 detik'
$versiHealth3 = ''
if ($null -ne $jawab3) { try { $versiHealth3 = ($jawab3.Content | ConvertFrom-Json).version } catch {} }
Cek 'health EXE melaporkan 1.2.9' ($versiHealth3 -eq '1.2.9') ("version=$versiHealth3") ("version=$versiHealth3")
$spa3 = $null
try { $spa3 = Invoke-WebRequest -Uri 'http://127.0.0.1:5313/' -UseBasicParsing -TimeoutSec 15 } catch {}
Cek 'SPA EXE memuat bundel 1.2.9' (($null -ne $spa3) -and ($spa3.StatusCode -eq 200) -and ($spa3.Content -match 'index-BksMbgv0\.js')) ("status $($spa3.StatusCode)") 'SPA tidak 200 / tidak merujuk bundel 1.2.9'
$base3 = 'http://127.0.0.1:5313'
$masukExe = Api 'POST' "$base3/api/auth/login" $null (@{ username = 'admin'; password = 'admin123' } | ConvertTo-Json)
$tokenExe = $null
if ($masukExe) { $tokenExe = $masukExe.token }
Cek 'login bawaan pada basis data EXE' ($null -ne $tokenExe) 'token diterima' 'login gagal'
$gantiExe = Api 'POST' "$base3/api/auth/change-password" $tokenExe (@{ currentPassword = 'admin123'; newPassword = $sandiBaru } | ConvertTo-Json)
$masukExe2 = Api 'POST' "$base3/api/auth/login" $null (@{ username = 'admin'; password = $sandiBaru } | ConvertTo-Json)
$tokenExe2 = $null
if ($masukExe2) { $tokenExe2 = $masukExe2.token }
Cek 'ganti sandi pada basis data EXE' ($null -ne $tokenExe2) 'login dengan sandi baru berhasil' 'login gagal'
Cek-BasisData $base3 $tokenExe2 'EXE' 5313
$migrasiExe = ([regex]::Matches((Baca-Teks $outHost3), 'Migrasi \d+ diterapkan')).Count
Cek 'basis data EXE baru menjalankan 18 migrasi' ($migrasiExe -eq 18) "18 migrasi" "migrasi=$migrasiExe"
Hentikan-Host $pHost3

# -- 12. Pagar versi EXE dan pagar kanal MSI vs EXE -------------------------
# Paket EXE 1.2.7/1.2.8 yang lebih lama belum memiliki pagar versi, jadi skenario
# "turun versi EXE" hanya bisa diuji dengan menaikkan penanda versi terpasang
# (kontrol negatif untuk kode pagar di 1.2.9), lalu penanda dikembalikan.
Set-ItemProperty -Path 'HKCU:\Software\Seruni22Bakery' -Name 'Version' -Value '9.9.9'
$logGuardVersi = Join-Path $bukti 'exe-129-versi-guard.log'
$kGuardVersi = Jalankan-Inno $exe129 @(('/DIR=' + $instalExe)) $logGuardVersi
$isiGuardVersi = Baca-Teks $logGuardVersi
Set-ItemProperty -Path 'HKCU:\Software\Seruni22Bakery' -Name 'Version' -Value '1.2.9'
Cek 'EXE 1.2.9 menolak dipasang bila versi lebih baru tercatat' ($kGuardVersi -ne 0) ("exit $kGuardVersi") ("exit $kGuardVersi, seharusnya bukan 0")
Cek 'log penolakan EXE menyebut versi lebih baru' ($isiGuardVersi -match 'Versi 9\.9\.9 sudah terpasang') 'alasan versi tercatat' 'alasan versi tidak tercatat'
Cek 'EXE 1.2.9 tetap terpasang' (((Get-Item $peluncurExe).VersionInfo.FileVersion) -like '1.2.9*') ((Get-Item $peluncurExe).VersionInfo.FileVersion) 'versi EXE berubah setelah penolakan'
Cek 'penanda versi dikembalikan ke 1.2.9' ((Get-ItemProperty 'HKCU:\Software\Seruni22Bakery').Version -eq '1.2.9') 'Version=1.2.9' ((Get-ItemProperty 'HKCU:\Software\Seruni22Bakery').Version)

$logMsiGuard = Join-Path $bukti 'msi-129-guard.log'
$kMsiGuard = Jalankan-Msi '/i' $msi129 $null $logMsiGuard
$isiMsiGuard = Baca-Teks $logMsiGuard
Cek 'MSI 1.2.9 ditolak selama EXE terpasang' ($kMsiGuard -ne 0) ("exit $kMsiGuard") ("exit $kMsiGuard, seharusnya bukan 0")
Cek 'log MSI menyebut kanal EXE' ($isiMsiGuard -match 'versi EXE masih terpasang') 'alasan kanal tercatat' 'alasan kanal tidak tercatat'
Cek 'tidak ada folder MSI setelah penolakan' (-not (Test-Path $instal)) 'folder Program Files tidak dibuat' 'folder Program Files terbentuk'
Cek 'tidak ada entri ARP MSI setelah penolakan' ($null -eq (Cari-Arp $kode129)) 'ARP MSI tidak ada' 'ARP MSI terbentuk'
Cek 'EXE tetap satu-satunya instalasi' (@(Daftar-ArpSeruni).Count -eq 1) (@(Daftar-ArpSeruni) -join '; ') (@(Daftar-ArpSeruni) -join '; ')

# -- 13. Copot EXE: data pengguna wajib tetap -------------------------------
$folderData = Join-Path $env:LOCALAPPDATA 'Seruni22Bakery'
New-Item -ItemType Directory -Force -Path $folderData | Out-Null
Set-Content -Path (Join-Path $folderData 'penanda-data.txt') -Value 'dibuat sebelum copot; harus tetap ada' -Encoding UTF8
Set-Content -Path (Join-Path $folderData 'seruni22bakery.db') -Value 'basis data penanda' -Encoding UTF8
$unins = Join-Path $instalExe 'unins000.exe'
$kUnins = -1
if (Test-Path $unins) {
  $pUn = Start-Process -FilePath $unins -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG=' + (Join-Path $bukti 'exe-129-uninstall.log'))) -PassThru
  if ($pUn.WaitForExit(600000)) { $kUnins = $pUn.ExitCode } else { try { $pUn.Kill() } catch {} }
}
Cek 'copot senyap EXE 1.2.9' ($kUnins -eq 0) ("exit $kUnins") ("exit $kUnins")
Start-Sleep -Seconds 5
Cek 'folder EXE hilang' (-not (Test-Path $instalExe)) 'folder program EXE terhapus' 'folder EXE masih ada'
Cek 'pintasan EXE hilang' ((-not (Test-Path $lnkMenuExe)) -and (-not (Test-Path $lnkDeskExe))) 'pintasan per-pengguna terhapus' 'pintasan EXE masih ada'
Cek 'entri ARP EXE hilang' ($null -eq (Cari-Arp '{5D9D507C-B7C6-4C4B-A6EE-6786A01F909A}_is1')) 'ARP EXE terhapus' 'ARP EXE masih ada'
Cek 'penanda HKCU EXE hilang' (-not (Test-Path 'HKCU:\Software\Seruni22Bakery\Channel')) 'Channel EXE terhapus' 'Channel EXE masih ada'
Cek 'folder data pengguna tetap ada' (Test-Path (Join-Path $folderData 'penanda-data.txt')) 'penanda-data.txt masih ada' 'folder data pengguna terhapus'
Cek 'berkas basis data pengguna tetap ada' (Test-Path (Join-Path $folderData 'seruni22bakery.db')) 'seruni22bakery.db penanda masih ada' 'berkas basis data pengguna ikut terhapus'
$sisa = @(Get-Process | Where-Object { $_.ProcessName -like 'Seruni22Bakery*' })
Cek 'tanpa proses yatim' ($sisa.Count -eq 0) 'tidak ada proses Seruni22Bakery tersisa' ("masih " + $sisa.Count + " proses")

# Skrip ini memanggil Cek lebih dari 80 kali. Run yang terpotong (mis. sandbox mati di
# tengah jalan) bisa menghasilkan rekap dengan sedikit pemeriksaan; itu bukan lulus.
$totalCek = $lulus + $gagal
if ($gagal -eq 0 -and $totalCek -ge 60) {
  Catat 'rekap' 'PASS' ("lulus=$lulus gagal=$gagal total=$totalCek")
} else {
  Catat 'rekap' 'FAIL' ("lulus=$lulus gagal=$gagal total=$totalCek minimal=60")
}
Set-Content -Path $selesai -Value ('selesai ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
Stop-Transcript | Out-Null
shutdown.exe /s /t 30 /f
