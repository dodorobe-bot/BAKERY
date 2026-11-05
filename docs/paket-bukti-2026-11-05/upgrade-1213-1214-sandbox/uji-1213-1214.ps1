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

# Uji peningkatan 1.2.13 -> 1.2.14 di Windows Sandbox bersih:
#   A. MSI 1.2.13 dipasang, data diisi lewat API; EXE 1.2.14 harus menolak berjalan.
#   B. MSI ditingkatkan 1.2.13 -> 1.2.14; berkas, merek, produk, skema 18 diperiksa.
#   C. Cetak ESC/POS ke emulator loopback dari biner hasil peningkatan, termasuk nominal
#      contoh struk yang dulu salah dan pengaturan yang bertahan setelah host di-restart.
#   D. MSI 1.2.13 tidak boleh dipasang di atas 1.2.14.
#   E. MSI dicopot; EXE 1.2.14 dipasang per pengguna dan dijalankan.
#   F. EXE 1.2.13 tidak boleh dipasang di atas EXE 1.2.14; MSI 1.2.14 menolak selama EXE ada.
#   G. Copot EXE: folder program hilang, folder data pengguna tetap.

$share  = 'C:\share'
$bukti  = Join-Path $share 'bukti'
$status = Join-Path $bukti 'status.txt'
$selesai = Join-Path $bukti 'selesai.txt'

$msi1213 = Join-Path $share 'Seruni22Bakery_v1.2.13_x64.msi'
$msi1214 = Join-Path $share 'Seruni22Bakery_v1.2.14_x64.msi'
$exe1213 = Join-Path $share 'Seruni22Bakery_Setup_v1.2.13.exe'
$exe1214 = Join-Path $share 'Seruni22Bakery_Setup_v1.2.14.exe'
$manifes = Join-Path $share 'hash-publish-1214.txt'

$shaMsi1213  = 'EB3F40FB0FA2FE1FB7E09BEE733DB524A8B29FB55AF367B1BD8D0DF1F5AFD132'
$shaMsi1214 = 'A8D998A60268EB563A56BCD0A23EF31518B2121E81D33EF30C0649588FD91DEF'
$shaExe1213  = 'C1A0AF29BD8F5BF9C439CADEF3C1D8C891DA0C49A3271384192FD682C444ED9D'
$shaExe1214 = 'F48442D54016841D0AA411DBB63103C59ADDEBC54152CBB92001A5885A318E7C'
$ukuranMsi1213 = 50416132
$ukuranMsi1214 = 50424324
$ukuranExe1213 = 42672292
$ukuranExe1214 = 42680006

$kode1213 = '{CE68DB95-1278-4892-B835-7F875F0DAB63}'
$kode1214 = '{75FF905B-6F22-4051-A437-5F9362F0EA2B}'
$kodeExe = '{5D9D507C-B7C6-4C4B-A6EE-6786A01F909A}_is1'

$instal = 'C:\Program Files\Seruni 22 Bakery'
$peluncur = Join-Path $instal 'Seruni22Bakery.exe'
$hostExe  = Join-Path $instal 'Seruni22Bakery.Host.exe'
$instalExe = 'C:\SeruniExe1214'
$peluncurExe = Join-Path $instalExe 'Seruni22Bakery.exe'
$hostExeExe  = Join-Path $instalExe 'Seruni22Bakery.Host.exe'

$dbPath  = 'C:\data\uji.db'
$keyPath = 'C:\data\uji.key'
$dbExePath  = 'C:\data\uji-exe.db'
$keyExePath = 'C:\data\uji-exe.key'
$sandiBaru = 'SandboxRoti1214!'
$logoPng = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='

$bundelJs = 'index-BmCxycbD.js'
$bundelJsLama = 'index-BZfyiTsC.js'
$bundelCss = 'index-CUfTbA7B.css'
$bundelCssLama = 'index-CUfTbA7B.css'
$hashHostDll = 'F710CCC9A0430AD2DFE0528B535BD18495314EC77A56E8C54389032F20CFCC77'

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

# Emulator printer jaringan: mendengarkan di 127.0.0.1, menerima satu sambungan, lalu
# menyimpan byte mentah. Ini perangkat lunak uji di dalam tamu, bukan printer fisik.
function Mulai-Emulator($port, $berkas, $siap) {
  Remove-Item $berkas, $siap -Force -ErrorAction SilentlyContinue
  $job = Start-Job -ScriptBlock {
    param($port, $berkas, $siap)
    $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
    $l.Start()
    Set-Content -Path $siap -Value 'siap' -Encoding ASCII
    try {
      $c = $l.AcceptTcpClient()
      $s = $c.GetStream()
      $ms = New-Object System.IO.MemoryStream
      $buf = New-Object byte[] 4096
      while (($n = $s.Read($buf, 0, $buf.Length)) -gt 0) { $ms.Write($buf, 0, $n) }
      [IO.File]::WriteAllBytes($berkas, $ms.ToArray())
      $c.Close()
    } finally {
      $l.Stop()
    }
  } -ArgumentList $port, $berkas, $siap
  return $job
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
Cek-Berkas $msi1213 $shaMsi1213 $ukuranMsi1213 'salinan MSI 1.2.13 utuh'
Cek-Berkas $msi1214 $shaMsi1214 $ukuranMsi1214 'salinan MSI 1.2.14 utuh'
Cek-Berkas $exe1213 $shaExe1213 $ukuranExe1213 'salinan EXE 1.2.13 utuh'
Cek-Berkas $exe1214 $shaExe1214 $ukuranExe1214 'salinan EXE 1.2.14 utuh'
$jmlManifes = (Get-Content $manifes -ErrorAction SilentlyContinue).Count
Cek 'manifes payload 1.2.14 terbaca' ($jmlManifes -eq 393) ("$jmlManifes baris") 'manifes tidak berisi 393 baris'

# -- 3. Pasang MSI 1.2.13 -----------------------------------------------------
$k1213 = Jalankan-Msi '/i' $msi1213 $null (Join-Path $bukti 'msi-1213-install.log')
Cek 'pasang senyap MSI 1.2.13' ($k1213 -eq 0) ("exit $k1213") ("exit $k1213")
$arp1213 = Cari-Arp $kode1213
Cek 'entri ARP 1.2.13' (($null -ne $arp1213) -and ($arp1213.DisplayVersion -eq '1.2.13')) ("{0} {1}" -f $arp1213.DisplayName, $arp1213.DisplayVersion) 'ARP 1.2.13 tidak ada/versi salah'
Cek 'versi peluncur 1.2.13' ((Get-Item $peluncur).VersionInfo.FileVersion -like '1.2.13*') ((Get-Item $peluncur).VersionInfo.FileVersion) 'versi peluncur 1.2.13 tidak sesuai'
$asetPra = @(Get-ChildItem (Join-Path $instal 'wwwroot\assets') -File -ErrorAction SilentlyContinue)
Cek 'aset 1.2.13 terpasang sebelum peningkatan' `
  (($asetPra.Count -eq 2) -and (Test-Path (Join-Path $instal ('wwwroot\assets\' + $bundelJsLama))) -and (Test-Path (Join-Path $instal ('wwwroot\assets\' + $bundelCssLama)))) `
  ("aset=" + (($asetPra | ForEach-Object { $_.Name }) -join ', ')) ("aset tidak sesuai: " + (($asetPra | ForEach-Object { $_.Name }) -join ', '))

# -- 4. Isi data lewat host 1.2.13 --------------------------------------------
New-Item -ItemType Directory -Force -Path 'C:\data' | Out-Null
$outHost = Join-Path $bukti 'host-1213.log'
$pHost = Start-Process -FilePath $hostExe -ArgumentList @("--Database:Path=$dbPath", "--Jwt:SigningKeyFile=$keyPath", '--Urls=http://127.0.0.1:5311') -WorkingDirectory $instal -RedirectStandardOutput $outHost -RedirectStandardError (Join-Path $bukti 'host-1213-err.log') -PassThru
$jawab = Tunggu-Health 5311 120
Cek 'host 1.2.13 menjawab /health' ($null -ne $jawab) 'health 200' 'host 1.2.13 tidak menjawab dalam 120 detik'
$versiHealth = ''
if ($null -ne $jawab) { try { $versiHealth = ($jawab.Content | ConvertFrom-Json).version } catch {} }
Cek 'health melaporkan 1.2.13' ($versiHealth -eq '1.2.13') ("version=$versiHealth") ("version=$versiHealth")

$base = 'http://127.0.0.1:5311'
$masuk = Api 'POST' "$base/api/auth/login" $null (@{ username = 'admin'; password = 'admin123' } | ConvertTo-Json)
$token = $null
if ($masuk) { $token = $masuk.token }
Cek 'login bawaan admin berhasil di 1.2.13' ($null -ne $token) 'token diterima' 'login gagal'
$ganti = Api 'POST' "$base/api/auth/change-password" $token (@{ currentPassword = 'admin123'; newPassword = $sandiBaru } | ConvertTo-Json)
$masuk2 = Api 'POST' "$base/api/auth/login" $null (@{ username = 'admin'; password = $sandiBaru } | ConvertTo-Json)
$token2 = $null
if ($masuk2) { $token2 = $masuk2.token }
Cek 'sandi diganti lalu login sandi baru' ($null -ne $token2) 'login dengan sandi baru berhasil' 'login dengan sandi baru gagal'

$produk = Api 'POST' "$base/api/products" $token2 (@{ name = 'Roti Uji 1214'; category = 'Roti'; sellingPriceMinorUnits = 15000; lines = @(); barcode = $null } | ConvertTo-Json)
$idProduk = 0
if ($produk) { $idProduk = [int]$produk.id }
Cek 'produk uji dibuat di 1.2.13' ($idProduk -gt 0) ("id=$idProduk") 'produk tidak terbuat'
$setMerek = Api 'PUT' "$base/api/settings" $token2 (@{ brand_name = 'Toko Uji Sandbox 1214' } | ConvertTo-Json)
$merek1213 = Api 'GET' "$base/api/brand" $null $null
$namaMerek1213 = [string](Ambil $merek1213 'name')
$kunci1213 = 0
if ($merek1213) { $kunci1213 = @($merek1213.PSObject.Properties).Count }
Cek 'merek disetel di 1.2.13' ($namaMerek1213 -eq 'Toko Uji Sandbox 1214') ("name=$namaMerek1213") ("name=$namaMerek1213")
Catat 'kunci /api/brand 1.2.13' 'INFO' ("$kunci1213 kunci")
Hentikan-Host $pHost
$ukuranDbPra = 0
if (Test-Path $dbPath) { $ukuranDbPra = (Get-Item $dbPath).Length }
Cek 'basis data pra-peningkatan terbentuk' ($ukuranDbPra -gt 0) ("uji.db $ukuranDbPra bita") 'uji.db tidak terbentuk'
# -wal dan -shm TIDAK dihapus: host dihentikan paksa, sehingga transaksi yang sudah commit
# bisa masih berada di WAL. Menghapus WAL di sini akan membuang sandi, produk, dan merek
# yang baru dibuat dan membuat pemeriksaan peningkatan gagal secara palsu.

# -- 5. EXE 1.2.14 harus menolak berjalan selama MSI terpasang ----------------
$logGuardExe = Join-Path $bukti 'exe-1214-guard.log'
$kGuard = Jalankan-Inno $exe1214 @(('/DIR=' + $instalExe)) $logGuardExe
$isiGuard = Baca-Teks $logGuardExe
Cek 'EXE 1.2.14 menolak dipasang selama MSI 1.2.13 ada' ($kGuard -ne 0) ("exit $kGuard") ("exit $kGuard, seharusnya bukan 0")
Cek 'log EXE menyebut MSI terdeteksi' (($isiGuard -match 'Pagar kanal') -and ($isiGuard -match 'Channel = msi')) 'alasan kanal tercatat di log' 'log tidak memuat alasan kanal'
Cek 'tidak ada folder EXE setelah penolakan' (-not (Test-Path $instalExe)) 'folder tidak dibuat' 'folder uji EXE terbentuk'
Cek 'tidak ada entri ARP EXE setelah penolakan' ($null -eq (Cari-Arp $kodeExe)) 'ARP EXE tidak ada' 'ARP EXE terbentuk'
Cek 'penolakan tidak menulis penanda HKCU' (-not (Test-Path 'HKCU:\Software\Seruni22Bakery\Channel')) 'Channel EXE tidak ditulis' 'penanda HKCU tertulis walau ditolak'
Cek 'MSI 1.2.13 tetap satu-satunya instalasi' (@(Daftar-ArpSeruni).Count -eq 1) (@(Daftar-ArpSeruni) -join '; ') (@(Daftar-ArpSeruni) -join '; ')

# -- 6. Tingkatkan MSI 1.2.13 -> 1.2.14 ---------------------------------------
$logUpgrade = Join-Path $bukti 'msi-1214-upgrade.log'
$kUp = Jalankan-Msi '/i' $msi1214 $null $logUpgrade
$isiLog = Baca-Teks $logUpgrade
Cek 'peningkatan senyap MSI 1.2.14' ($kUp -eq 0) ("exit $kUp") ("exit $kUp")
Cek 'produk 1.2.13 terdeteksi sebagai sasaran peningkatan' ($isiLog -match [regex]::Escape($kode1213)) ("WIX_UPGRADE_DETECTED memuat $kode1213") 'ProductCode 1.2.13 tidak muncul di log peningkatan'
Cek 'log MSI menyatakan sukses' ($isiLog -match 'Installation success or error status: 0') 'status 0' 'log tidak menyatakan status 0'
Cek 'RemoveExistingProducts dijalankan' ($isiLog -match 'RemoveExistingProducts') 'produk lama dibongkar' 'produk lama tidak dibongkar'
$arp1214 = Cari-Arp $kode1214
Cek 'entri ARP 1.2.14' (($null -ne $arp1214) -and ($arp1214.DisplayVersion -eq '1.2.14')) ("{0} {1}" -f $arp1214.DisplayName, $arp1214.DisplayVersion) 'ARP 1.2.14 tidak ada/versi salah'
Cek 'entri ARP 1.2.13 hilang' ($null -eq (Cari-Arp $kode1213)) 'kunci 1.2.13 tidak ada lagi' 'kunci 1.2.13 masih ada'
Cek 'tepat satu entri Seruni sesudah peningkatan' (@(Daftar-ArpSeruni).Count -eq 1) (@(Daftar-ArpSeruni) -join '; ') (@(Daftar-ArpSeruni) -join '; ')

$payload = Banding-Payload $instal $true
Cek 'seluruh 393 berkas MSI 1.2.14 identik dengan manifes' $payload.Ok $payload.Rincian $payload.Rincian
Cek 'versi peluncur 1.2.14' ((Get-Item $peluncur).VersionInfo.FileVersion -like '1.2.14*') ((Get-Item $peluncur).VersionInfo.FileVersion) 'versi peluncur tidak sesuai'
Cek 'versi host 1.2.14.0' ((Get-Item $hostExe).VersionInfo.FileVersion -like '1.2.14*') ((Get-Item $hostExe).VersionInfo.FileVersion) 'versi host tidak sesuai'
Cek 'Host.dll sama dengan manifes' ((Get-FileHash (Join-Path $instal 'Seruni22Bakery.Host.dll') -Algorithm SHA256).Hash -eq $hashHostDll) 'hash Host.dll cocok' 'hash Host.dll berbeda'
$aset = @(Get-ChildItem (Join-Path $instal 'wwwroot\assets') -File -ErrorAction SilentlyContinue)
Cek 'bundel 1.2.14 terpasang, aset lama tersapu' `
  (($aset.Count -eq 2) -and (Test-Path (Join-Path $instal "wwwroot\assets\$bundelJs")) -and (Test-Path (Join-Path $instal "wwwroot\assets\$bundelCss"))) `
  ("aset=" + (($aset | ForEach-Object { $_.Name }) -join ', ')) ("aset tidak sesuai: " + (($aset | ForEach-Object { $_.Name }) -join ', '))
$marker = Get-ItemProperty 'HKLM:\Software\Seruni22Bakery' -ErrorAction SilentlyContinue
Cek 'penanda kanal HKLM milik MSI' (($null -ne $marker) -and ($marker.Channel -eq 'msi') -and ($marker.Version -eq '1.2.14')) ("Channel=$($marker.Channel) Version=$($marker.Version) InstallDir=$($marker.InstallDir)") 'penanda HKLM tidak sesuai'
$lnkMenu = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk'
$lnkDesk = 'C:\Users\Public\Desktop\Seruni 22 Bakery.lnk'
Cek 'pintasan menunjuk instalasi 1.2.14' (((Target-Pintasan $lnkMenu) -eq $peluncur) -and ((Target-Pintasan $lnkDesk) -eq $peluncur)) ('target ' + (Target-Pintasan $lnkMenu)) 'pintasan hilang/target berubah'

# -- 7. Data dan skema sesudah peningkatan -----------------------------------
$outHost2 = Join-Path $bukti 'host-1214.log'
$pHost2 = Start-Process -FilePath $hostExe -ArgumentList @("--Database:Path=$dbPath", "--Jwt:SigningKeyFile=$keyPath", '--Urls=http://127.0.0.1:5312') -WorkingDirectory $instal -RedirectStandardOutput $outHost2 -RedirectStandardError (Join-Path $bukti 'host-1214-err.log') -PassThru
$jawab2 = Tunggu-Health 5312 120
Cek 'host hasil peningkatan menjawab /health' ($null -ne $jawab2) 'health 200' 'host 1.2.14 tidak menjawab dalam 120 detik'
$versiHealth2 = ''
if ($null -ne $jawab2) { try { $versiHealth2 = ($jawab2.Content | ConvertFrom-Json).version } catch {} }
Cek 'health melaporkan 1.2.14' ($versiHealth2 -eq '1.2.14') ("version=$versiHealth2") ("version=$versiHealth2")
$base2 = 'http://127.0.0.1:5312'
$masuk3 = Api 'POST' "$base2/api/auth/login" $null (@{ username = 'admin'; password = $sandiBaru } | ConvertTo-Json)
$token3 = $null
if ($masuk3) { $token3 = $masuk3.token }
Cek 'sandi dari 1.2.13 masih berlaku di 1.2.14' ($null -ne $token3) 'login dengan sandi hasil ganti berhasil' 'login gagal setelah peningkatan'
$wajibGanti3 = $true
if ($masuk3) { $wajibGanti3 = [bool]$masuk3.mustChangePassword }
Cek 'status wajib-ganti-sandi tidak menyala lagi' (-not $wajibGanti3) "mustChangePassword=$wajibGanti3" "mustChangePassword=$wajibGanti3"
$produk1214 = Api 'GET' "$base2/api/products/$idProduk" $token3 $null
$cocokProduk = $false
$rincianProduk = 'tidak terbaca'
if ($produk1214) {
  $idP = [int](Ambil $produk1214 'id'); $namaP = [string](Ambil $produk1214 'name'); $hargaP = [long](Ambil $produk1214 'sellingPriceMinorUnits')
  $rincianProduk = "id=$idP name=$namaP harga=$hargaP"
  $cocokProduk = ($idP -eq $idProduk) -and ($namaP -eq 'Roti Uji 1214') -and ($hargaP -eq 15000)
}
Cek 'produk dari 1.2.13 terbaca utuh di 1.2.14' $cocokProduk $rincianProduk 'produk tidak ditemukan/nilainya berubah'
$merek1214 = Api 'GET' "$base2/api/brand" $null $null
$namaMerek1214 = [string](Ambil $merek1214 'name')
$kunci1214 = 0
if ($merek1214) { $kunci1214 = @($merek1214.PSObject.Properties).Count }
Cek 'merek dari 1.2.13 masih tersimpan' ($namaMerek1214 -eq 'Toko Uji Sandbox 1214') ("name=$namaMerek1214") ("name=$namaMerek1214")
Cek 'endpoint /api/brand anonim 4 kunci' ($kunci1214 -eq 4) ("$kunci1214 kunci") ("$kunci1214 kunci")
$setLogo = Api 'PUT' "$base2/api/settings" $token3 (@{ brand_logo = $logoPng } | ConvertTo-Json)
$merekLogo = Api 'GET' "$base2/api/brand" $null $null
Cek 'logo bisa dipasang di 1.2.14 hasil peningkatan' ([string](Ambil $merekLogo 'logo') -eq $logoPng) 'logo kembali sama persis' 'logo tidak kembali sama'
Cek-BasisData $base2 $token3 'peningkatan' 5312

# -- 7b. ESC/POS dari biner hasil peningkatan --------------------------------
$berkasCetak = Join-Path $bukti 'escpos-diterima.bin'
$siapCetak = Join-Path $bukti 'emulator-siap.txt'
$jobEmu = Mulai-Emulator 9100 $berkasCetak $siapCetak
$emulatorSiap = $false
for ($i = 0; $i -lt 20; $i++) {
  Start-Sleep -Milliseconds 500
  if (Test-Path $siapCetak) { $emulatorSiap = $true; break }
}
Cek 'emulator ESC/POS loopback siap di port 9100' $emulatorSiap 'listener aktif' 'listener tidak siap dalam 10 detik'
$simpanPerangkat = Api 'PUT' "$base2/api/devices/settings" $token3 (@{
  'device.printer.mode' = 'escpos-raw'
  'device.escpos.host' = '127.0.0.1'
  'device.escpos.port' = '9100'
  'device.escpos.cut' = '0'
} | ConvertTo-Json)
$modeSimpan = [string](Ambil $simpanPerangkat 'mode')
Cek 'pengaturan escpos-raw tersimpan' ($modeSimpan -eq 'escpos-raw') "mode=$modeSimpan" "mode=$modeSimpan"
$job = Api 'POST' "$base2/api/devices/test-print" $token3 $null
$jobId = 0
if ($job) { $jobId = [int](Ambil $job 'jobId') }
Cek 'cetak uji diterima host hasil peningkatan' ($jobId -gt 0) "jobId=$jobId" 'cetak uji gagal diterima'
$selesaiJob = $false
if ($null -ne $jobEmu) { $selesaiJob = [bool](Wait-Job -Job $jobEmu -Timeout 60) }
$panjang = 0
if (Test-Path $berkasCetak) { $panjang = (Get-Item $berkasCetak).Length }
Cek 'emulator menerima byte cetak' (($selesaiJob) -and ($panjang -gt 0)) ("$panjang bita") ("selesai=$selesaiJob bita=$panjang")
$b = @()
if ($panjang -gt 0) { $b = [IO.File]::ReadAllBytes($berkasCetak) }
$teksStruk = [Text.Encoding]::ASCII.GetString($b)
$awalEscPos = ($b.Length -ge 2) -and ($b[0] -eq 0x1B) -and ($b[1] -eq 0x40)
Cek 'struk diawali ESC @' $awalEscPos '0x1B 0x40' 'awal bukan ESC @'
$nominalBenar = $teksStruk -match 'Rp 30\.000'
Cek 'nominal contoh struk Rp 30.000' $nominalBenar 'baris "2 x Rp 15.000 ... Rp 30.000" ada' 'nominal contoh tidak ada'
$nominalLama = $teksStruk -match 'Rp 300(\D|$)'
Cek 'nominal lama Rp 300 tidak dikirim lagi' (-not $nominalLama) 'tidak ada "Rp 300"' 'masih ada "Rp 300"'
$akhirUmpan = ($b.Length -ge 3) -and ($b[$b.Length - 3] -eq 0x1B) -and ($b[$b.Length - 2] -eq 0x64) -and ($b[$b.Length - 1] -eq 0x03)
Cek 'struk diakhiri ESC d 3' $akhirUmpan '0x1B 0x64 0x03' 'akhiran bukan ESC d 3'
$adaPotong = $false
for ($i = 0; $i -lt ($b.Length - 1); $i++) { if (($b[$i] -eq 0x1D) -and ($b[$i + 1] -eq 0x56)) { $adaPotong = $true; break } }
Cek 'tidak ada perintah potong saat cut=0' (-not $adaPotong) 'GS V tidak dikirim' 'GS V terkirim padahal cut=0'
if ($null -ne $jobEmu) { Remove-Job -Job $jobEmu -Force -ErrorAction SilentlyContinue }
$statusPerangkat = Api 'GET' "$base2/api/devices/status" $token3 $null
$modeStatus = [string](Ambil $statusPerangkat 'mode')
$tujuanStatus = [string](Ambil $statusPerangkat 'printerName')
Cek 'status perangkat melaporkan mode escpos-raw' ($modeStatus -eq 'escpos-raw') "mode=$modeStatus" "mode=$modeStatus"
Cek 'status perangkat melaporkan tujuan tcp' ($tujuanStatus -eq 'tcp://127.0.0.1:9100') "tujuan=$tujuanStatus" "tujuan=$tujuanStatus"
$pekerjaan = Api 'GET' "$base2/api/devices/print-jobs" $token3 $null
$jobPertama = $null
if ($pekerjaan) { $jobPertama = @($pekerjaan)[0] }
$statusJob = [string](Ambil $jobPertama 'status')
$printerJob = [string](Ambil $jobPertama 'printerName')
$jenisJob = [string](Ambil $jobPertama 'kind')
Cek 'riwayat cetak mencatat pekerjaan uji berstatus Printed' (($statusJob -eq 'Printed') -and ($jenisJob -eq 'Test')) "status=$statusJob kind=$jenisJob" "status=$statusJob kind=$jenisJob"
Cek 'riwayat cetak mencatat tujuan tcp' ($printerJob -eq 'tcp://127.0.0.1:9100') "printer=$printerJob" "printer=$printerJob"
# Pengaturan perangkat harus bertahan di basis data, bukan hanya di memori proses.
Hentikan-Host $pHost2
$outHost2b = Join-Path $bukti 'host-1214-restart.log'
$pHost2b = Start-Process -FilePath $hostExe -ArgumentList @("--Database:Path=$dbPath", "--Jwt:SigningKeyFile=$keyPath", '--Urls=http://127.0.0.1:5312') -WorkingDirectory $instal -RedirectStandardOutput $outHost2b -RedirectStandardError (Join-Path $bukti 'host-1214-restart-err.log') -PassThru
$jawab2b = Tunggu-Health 5312 120
$statusUlang = $null
if ($null -ne $jawab2b) { $statusUlang = Api 'GET' "$base2/api/devices/status" $token3 $null }
$modeUlang = [string](Ambil $statusUlang 'mode')
$tujuanUlang = [string](Ambil $statusUlang 'printerName')
Cek 'pilihan escpos-raw bertahan setelah host di-restart' (($modeUlang -eq 'escpos-raw') -and ($tujuanUlang -eq 'tcp://127.0.0.1:9100')) "mode=$modeUlang tujuan=$tujuanUlang" "mode=$modeUlang tujuan=$tujuanUlang"
Hentikan-Host $pHost2b
# Berkas stdout baru bisa dibaca utuh setelah host berhenti; membaca saat proses masih
# menulis menghasilkan berkas kosong dan pemeriksaan gagal secara palsu.
$migrasiBaru = ([regex]::Matches((Baca-Teks $outHost2) + (Baca-Teks $outHost2b), 'Migrasi \d+ diterapkan')).Count
Cek 'tidak ada migrasi baru di atas skema 18' ($migrasiBaru -eq 0) "migrasi dijalankan x$migrasiBaru (diharapkan 0)" "migrasi dijalankan x$migrasiBaru, seharusnya 0"
Cek 'basis data tetap ada setelah dipakai 1.2.14' ((Test-Path $dbPath) -and ((Get-Item $dbPath).Length -gt 0)) ("uji.db $((Get-Item $dbPath).Length) bita") 'uji.db hilang'

# -- 8. Turun versi MSI harus ditolak ---------------------------------------
$logTurunMsi = Join-Path $bukti 'msi-1213-downgrade.log'
$kTurunMsi = Jalankan-Msi '/i' $msi1213 $null $logTurunMsi
$isiTurunMsi = Baca-Teks $logTurunMsi
Cek 'MSI 1.2.13 ditolak di atas 1.2.14' ($kTurunMsi -ne 0) ("exit $kTurunMsi") ("exit $kTurunMsi, seharusnya bukan 0")
Cek 'log penolakan menyebut alasan turun versi' ($isiTurunMsi -match 'WIX_DOWNGRADE_DETECTED|Versi yang lebih baru') 'syarat turun versi tercatat' 'syarat turun versi tidak tercatat'
Cek 'versi terpasang tetap 1.2.14' (((Cari-Arp $kode1214).DisplayVersion) -eq '1.2.14') ((Cari-Arp $kode1214).DisplayVersion) 'versi berubah setelah percobaan turun'

# -- 9. Copot MSI 1.2.14 -----------------------------------------------------
$kCopot = Jalankan-Msi '/x' $null $kode1214 (Join-Path $bukti 'msi-1214-uninstall.log')
Cek 'copot senyap MSI 1.2.14' ($kCopot -eq 0) ("exit $kCopot") ("exit $kCopot")
Start-Sleep -Seconds 3
Cek 'folder instalasi MSI hilang' (-not (Test-Path $instal)) 'folder program terhapus' 'folder program masih ada'
Cek 'pintasan MSI hilang' ((-not (Test-Path $lnkMenu)) -and (-not (Test-Path $lnkDesk))) 'Start Menu dan Desktop publik terhapus' 'pintasan masih ada'
Cek 'tidak ada entri ARP tersisa' (@(Daftar-ArpSeruni).Count -eq 0) 'daftar program bersih' (@(Daftar-ArpSeruni) -join '; ')
Cek 'penanda HKLM ikut terhapus' (-not (Test-Path 'HKLM:\Software\Seruni22Bakery')) 'HKLM Software\Seruni22Bakery hilang' 'penanda HKLM masih ada'
Cek 'data di luar folder program tetap utuh' ((Test-Path $dbPath) -and (Test-Path $keyPath)) 'uji.db dan uji.key masih ada setelah copot' 'data uji ikut terhapus'

# -- 10. Pasang EXE 1.2.14 per pengguna --------------------------------------
$logExe = Join-Path $bukti 'exe-1214-install.log'
$kExe = Jalankan-Inno $exe1214 @(('/DIR=' + $instalExe)) $logExe
Cek 'pasang senyap EXE 1.2.14' ($kExe -eq 0) ("exit $kExe") ("exit $kExe")
$payloadExe = Banding-Payload $instalExe $false
Cek '393 berkas EXE 1.2.14 identik dengan manifes' $payloadExe.Ok $payloadExe.Rincian $payloadExe.Rincian
Cek 'versi peluncur EXE 1.2.14' ((Get-Item $peluncurExe).VersionInfo.FileVersion -like '1.2.14*') ((Get-Item $peluncurExe).VersionInfo.FileVersion) 'versi peluncur EXE tidak sesuai'
Cek 'versi host EXE 1.2.14.0' ((Get-Item $hostExeExe).VersionInfo.FileVersion -like '1.2.14*') ((Get-Item $hostExeExe).VersionInfo.FileVersion) 'versi host EXE tidak sesuai'
$markerExe = Get-ItemProperty 'HKCU:\Software\Seruni22Bakery' -ErrorAction SilentlyContinue
Cek 'penanda kanal HKCU milik EXE' (($null -ne $markerExe) -and ($markerExe.Channel -eq 'exe') -and ($markerExe.Version -eq '1.2.14')) ("Channel=$($markerExe.Channel) Version=$($markerExe.Version) InstallDir=$($markerExe.InstallDir)") 'penanda HKCU tidak sesuai'
$lnkMenuExe = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery\Seruni 22 Bakery.lnk'
$lnkDeskExe = Join-Path $env:USERPROFILE 'Desktop\Seruni 22 Bakery.lnk'
Cek 'pintasan per-pengguna menunjuk folder uji' (((Target-Pintasan $lnkMenuExe) -eq $peluncurExe) -and ((Target-Pintasan $lnkDeskExe) -eq $peluncurExe)) ('target ' + (Target-Pintasan $lnkMenuExe)) 'pintasan per-pengguna salah'
$arpExe = Cari-Arp $kodeExe
Cek 'entri ARP EXE 1.2.14' (($null -ne $arpExe) -and ($arpExe.DisplayVersion -eq '1.2.14')) ("{0} {1}" -f $arpExe.DisplayName, $arpExe.DisplayVersion) 'ARP EXE tidak ada/versi salah'

# -- 11. Host dari folder EXE dijalankan pada basis data baru ---------------
$outHost3 = Join-Path $bukti 'host-exe-1214.log'
$pHost3 = Start-Process -FilePath $hostExeExe -ArgumentList @("--Database:Path=$dbExePath", "--Jwt:SigningKeyFile=$keyExePath", '--Urls=http://127.0.0.1:5313') -WorkingDirectory $instalExe -RedirectStandardOutput $outHost3 -RedirectStandardError (Join-Path $bukti 'host-exe-1214-err.log') -PassThru
$jawab3 = Tunggu-Health 5313 120
Cek 'host dari folder EXE menjawab /health' ($null -ne $jawab3) 'health 200' 'host EXE tidak menjawab dalam 120 detik'
$versiHealth3 = ''
if ($null -ne $jawab3) { try { $versiHealth3 = ($jawab3.Content | ConvertFrom-Json).version } catch {} }
Cek 'health EXE melaporkan 1.2.14' ($versiHealth3 -eq '1.2.14') ("version=$versiHealth3") ("version=$versiHealth3")
$spa3 = $null
try { $spa3 = Invoke-WebRequest -Uri 'http://127.0.0.1:5313/' -UseBasicParsing -TimeoutSec 15 } catch {}
Cek 'SPA EXE memuat bundel 1.2.14' (($null -ne $spa3) -and ($spa3.StatusCode -eq 200) -and ($spa3.Content -match [regex]::Escape($bundelJs))) ("status $($spa3.StatusCode)") 'SPA tidak 200 / tidak merujuk bundel 1.2.14'
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
Hentikan-Host $pHost3
$migrasiExe = ([regex]::Matches((Baca-Teks $outHost3), 'Migrasi \d+ diterapkan')).Count
Cek 'basis data EXE baru menjalankan 18 migrasi' ($migrasiExe -eq 18) "18 migrasi" "migrasi=$migrasiExe"

# -- 12. Pagar versi EXE dan pagar kanal MSI vs EXE -------------------------
# EXE 1.2.13 sudah memuat pagar versi (BandingkanVersi di InitializeSetup), jadi skenario
# "turun versi EXE" diuji dengan paket EXE 1.2.13 yang sebenarnya di atas EXE 1.2.14;
# penanda HKCU tidak dikarang-karang.
$logGuardVersi = Join-Path $bukti 'exe-1213-versi-guard.log'
$kGuardVersi = Jalankan-Inno $exe1213 @(('/DIR=' + $instalExe)) $logGuardVersi
$isiGuardVersi = Baca-Teks $logGuardVersi
Cek 'EXE 1.2.13 ditolak di atas EXE 1.2.14' ($kGuardVersi -ne 0) ("exit $kGuardVersi") ("exit $kGuardVersi, seharusnya bukan 0")
Cek 'log penolakan EXE menyebut pagar versi terhadap 1.2.14' (($isiGuardVersi -match 'Pagar versi') -and ($isiGuardVersi -match '1\.2\.13')) 'alasan versi tercatat' 'alasan versi tidak tercatat'
Cek 'EXE 1.2.14 tetap terpasang' (((Get-Item $peluncurExe).VersionInfo.FileVersion) -like '1.2.14*') ((Get-Item $peluncurExe).VersionInfo.FileVersion) 'versi EXE berubah setelah penolakan'
Cek 'penanda versi HKCU tetap 1.2.14' ((Get-ItemProperty 'HKCU:\Software\Seruni22Bakery').Version -eq '1.2.14') 'Version=1.2.14' ((Get-ItemProperty 'HKCU:\Software\Seruni22Bakery').Version)

$logMsiGuard = Join-Path $bukti 'msi-1214-guard.log'
$kMsiGuard = Jalankan-Msi '/i' $msi1214 $null $logMsiGuard
$isiMsiGuard = Baca-Teks $logMsiGuard
Cek 'MSI 1.2.14 ditolak selama EXE terpasang' ($kMsiGuard -ne 0) ("exit $kMsiGuard") ("exit $kMsiGuard, seharusnya bukan 0")
Cek 'log MSI menyebut kanal EXE' ($isiMsiGuard -match 'versi EXE masih terpasang') 'alasan kanal tercatat' 'alasan kanal tidak tercatat'
Cek 'tidak ada folder MSI setelah penolakan' (-not (Test-Path $instal)) 'folder Program Files tidak dibuat' 'folder Program Files terbentuk'
Cek 'tidak ada entri ARP MSI setelah penolakan' ($null -eq (Cari-Arp $kode1214)) 'ARP MSI tidak ada' 'ARP MSI terbentuk'
Cek 'EXE tetap satu-satunya instalasi' (@(Daftar-ArpSeruni).Count -eq 1) (@(Daftar-ArpSeruni) -join '; ') (@(Daftar-ArpSeruni) -join '; ')

# -- 13. Copot EXE: data pengguna wajib tetap -------------------------------
$folderData = Join-Path $env:LOCALAPPDATA 'Seruni22Bakery'
New-Item -ItemType Directory -Force -Path $folderData | Out-Null
Set-Content -Path (Join-Path $folderData 'penanda-data.txt') -Value 'dibuat sebelum copot; harus tetap ada' -Encoding UTF8
Set-Content -Path (Join-Path $folderData 'seruni22bakery.db') -Value 'basis data penanda' -Encoding UTF8
$unins = Join-Path $instalExe 'unins000.exe'
$kUnins = -1
if (Test-Path $unins) {
  $pUn = Start-Process -FilePath $unins -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG=' + (Join-Path $bukti 'exe-1214-uninstall.log'))) -PassThru
  if ($pUn.WaitForExit(600000)) { $kUnins = $pUn.ExitCode } else { try { $pUn.Kill() } catch {} }
}
Cek 'copot senyap EXE 1.2.14' ($kUnins -eq 0) ("exit $kUnins") ("exit $kUnins")
Start-Sleep -Seconds 5
Cek 'folder EXE hilang' (-not (Test-Path $instalExe)) 'folder program EXE terhapus' 'folder EXE masih ada'
Cek 'pintasan EXE hilang' ((-not (Test-Path $lnkMenuExe)) -and (-not (Test-Path $lnkDeskExe))) 'pintasan per-pengguna terhapus' 'pintasan EXE masih ada'
Cek 'entri ARP EXE hilang' ($null -eq (Cari-Arp $kodeExe)) 'ARP EXE terhapus' 'ARP EXE masih ada'
Cek 'penanda HKCU EXE hilang' (-not (Test-Path 'HKCU:\Software\Seruni22Bakery\Channel')) 'Channel EXE terhapus' 'Channel EXE masih ada'
Cek 'folder data pengguna tetap ada' (Test-Path (Join-Path $folderData 'penanda-data.txt')) 'penanda-data.txt masih ada' 'folder data pengguna terhapus'
Cek 'berkas basis data pengguna tetap ada' (Test-Path (Join-Path $folderData 'seruni22bakery.db')) 'seruni22bakery.db penanda masih ada' 'berkas basis data pengguna ikut terhapus'
$sisa = @(Get-Process | Where-Object { $_.ProcessName -like 'Seruni22Bakery*' })
Cek 'tanpa proses yatim' ($sisa.Count -eq 0) 'tidak ada proses Seruni22Bakery tersisa' ("masih " + $sisa.Count + " proses")

# Run yang terpotong (mis. sandbox mati di tengah jalan) bisa menghasilkan rekap dengan
# sedikit pemeriksaan; itu bukan lulus. Ambang 80 menjaga arti rekap tetap sama.
$totalCek = $lulus + $gagal
if ($gagal -eq 0 -and $totalCek -ge 80) {
  Catat 'rekap' 'PASS' ("lulus=$lulus gagal=$gagal total=$totalCek")
} else {
  Catat 'rekap' 'FAIL' ("lulus=$lulus gagal=$gagal total=$totalCek minimal=80")
}
Set-Content -Path $selesai -Value ('selesai ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
Stop-Transcript | Out-Null
shutdown.exe /s /t 30 /f
