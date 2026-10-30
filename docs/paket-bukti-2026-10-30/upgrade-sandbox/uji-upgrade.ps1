$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$share  = 'C:\share'
$bukti  = Join-Path $share 'bukti'
$status = Join-Path $bukti 'status.txt'
$selesai = Join-Path $bukti 'selesai.txt'
$msi126 = Join-Path $share 'Seruni22Bakery_v1.2.6_x64.msi'
$msi127 = Join-Path $share 'Seruni22Bakery_v1.2.7_x64.msi'
$manifes = Join-Path $share 'hash-publish.txt'
$instal = 'C:\Program Files\Seruni 22 Bakery'
$peluncur = Join-Path $instal 'Seruni22Bakery.exe'
$hostExe  = Join-Path $instal 'Seruni22Bakery.Host.exe'
$kode126 = '{EDCBBD96-732D-4854-A31B-0A36E6C43E77}'
$kode127 = '{A0AEA895-FC09-4312-8E24-8C9E0FEABDC8}'
$sha126  = '461D1C771A5BA58B0DC944FC9EB3E9768B8640ABD437FC72CA52D65744E353C1'
$sha127  = '5C4C31F87ECB96E23B5630238BA59D72C9F115904CD98A8AC13D6953FE20DFBB'
$ukuran126 = 50357095
$ukuran127 = 50373479
$idx126  = '298108B0BAE6BA8BC550A3F3ADCD41FF65E210BF86C22F4931EF38B46EE946F8'
$idx127  = 'F4A2FC04CA04E10FD665704C11B63E1BB57677A8AC3B4570DB411DFDB694BD2E'
$dll126  = 'C9A5B020386536E2092BF2F7DFA9E0E0E7C611FFF3B8519F158887A83B90020E'
$dll127  = '4773A367836B117A8106E45A2D345B7D15D61FDF774768183C97CD28F07C8B49'
$dbPath  = 'C:\data\uji.db'
$keyPath = 'C:\data\uji.key'
$sandiBaru = 'SandboxRoti127!'
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

function Jalankan-Msi($aksi, $berkas, $kode, $log) {
  if ($aksi -eq '/x') { $arg = @('/x', $kode, '/qn', '/norestart', '/l*v', $log) }
  else { $arg = @($aksi, $berkas, '/qn', '/norestart', '/l*v', $log) }

  $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arg -PassThru
  if (-not $p.WaitForExit(300000)) { try { $p.Kill() } catch {}; return -1 }
  $k = $p.ExitCode
  if ($k -eq 0 -or $k -eq 3010) { return $k }

  Catat 'msiexec' 'INFO' ("exit $k tanpa elevasi; ulang via RunAs")
  try {
    $p2 = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arg -Verb RunAs -PassThru
    if (-not $p2.WaitForExit(300000)) { try { $p2.Kill() } catch {}; return -1 }
    return $p2.ExitCode
  } catch {
    Catat 'msiexec' 'INFO' ('elevasi gagal: ' + $_.Exception.Message)
    return $k
  }
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

function SidikBasisData {
  $bagian = @()
  foreach ($f in @($dbPath, ($dbPath + '-wal'), ($dbPath + '-shm'))) {
    if (Test-Path $f) {
      $bagian += ('{0}={1}/{2}' -f (Split-Path $f -Leaf), (Get-FileHash $f -Algorithm SHA256).Hash, (Get-Item $f).Length)
    }
  }
  return ($bagian -join '; ')
}

# ── 1. Lingkungan ────────────────────────────────────────────────────────────
$os  = Get-CimInstance Win32_OperatingSystem
$id  = [Security.Principal.WindowsIdentity]::GetCurrent()
$adm = (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Catat 'lingkungan' 'INFO' ($os.Caption + ' build ' + $os.BuildNumber + '; pengguna ' + $id.Name + '; elevated=' + $adm + '; RAM ' + [math]::Round($os.TotalVisibleMemorySize / 1024) + ' MB')
Cek 'mesin bersih: tanpa runtime .NET bersama' (-not (Test-Path 'C:\Program Files\dotnet')) 'C:\Program Files\dotnet tidak ada (publish self-contained)' 'C:\Program Files\dotnet ada; klaim mesin bersih gugur'
$arpAwal = @(Daftar-ArpSeruni)
Cek 'mesin bersih: belum ada instalasi Seruni' ((-not (Test-Path $instal)) -and ($arpAwal.Count -eq 0)) 'tidak ada folder program dan tidak ada entri ARP' ('sudah ada sebelum uji: ' + ($arpAwal -join '; '))

# ── 2. Salinan kedua MSI ─────────────────────────────────────────────────────
$f126 = Get-Item $msi126
$h126 = (Get-FileHash $msi126 -Algorithm SHA256).Hash
Cek 'salinan MSI 1.2.6 utuh' (($h126 -eq $sha126) -and ($f126.Length -eq $ukuran126)) ("$($f126.Length) byte, SHA256 $h126") ("ukuran/hash tidak cocok: $($f126.Length) byte $h126")
$f127 = Get-Item $msi127
$h127 = (Get-FileHash $msi127 -Algorithm SHA256).Hash
Cek 'salinan MSI 1.2.7 utuh' (($h127 -eq $sha127) -and ($f127.Length -eq $ukuran127)) ("$($f127.Length) byte, SHA256 $h127") ("ukuran/hash tidak cocok: $($f127.Length) byte $h127")

# ── 3. Pasang 1.2.6 ──────────────────────────────────────────────────────────
$k126 = Jalankan-Msi '/i' $msi126 $null (Join-Path $bukti 'msi-126-install.log')
Cek 'pasang senyap MSI 1.2.6' ($k126 -eq 0) ("exit $k126") ("exit $k126")

$berkas = @(Get-ChildItem -Path $instal -Recurse -File -ErrorAction SilentlyContinue)
Cek 'jumlah berkas terpasang 1.2.6' ($berkas.Count -eq 393) ("$($berkas.Count) berkas") ("$($berkas.Count) berkas, seharusnya 393")
Cek 'runtime mandiri ikut' ((Test-Path (Join-Path $instal 'hostfxr.dll')) -and (Test-Path (Join-Path $instal 'coreclr.dll'))) 'hostfxr.dll + coreclr.dll ada' 'runtime mandiri tidak lengkap'
Cek 'tanpa berkas pengembangan/rahasia' ((-not (Test-Path (Join-Path $instal 'appsettings.Development.json'))) -and (-not (Test-Path (Join-Path $instal 'Seruni22Bakery.Host.pdb'))) -and (-not (Test-Path (Join-Path $instal 'seruni22bakery.db'))) -and (-not (Test-Path (Join-Path $instal 'jwt.key')))) 'tidak ada appsettings.Development.json, PDB, basis data, atau kunci' 'ada berkas pengembangan/rahasia di paket'

$vPeluncur = (Get-Item $peluncur).VersionInfo.FileVersion
$vHost = (Get-Item $hostExe).VersionInfo.FileVersion
Cek 'versi peluncur 1.2.6' ($vPeluncur -like '1.2.6*') $vPeluncur ("versi berkas $vPeluncur")
Cek 'versi host 1.2.6' ($vHost -like '1.2.6*') $vHost ("versi berkas $vHost")
$hDll = (Get-FileHash (Join-Path $instal 'Seruni22Bakery.Host.dll') -Algorithm SHA256).Hash
Cek 'Host.dll 1.2.6 sama dengan paket' ($hDll -eq $dll126) $hDll ("$hDll, seharusnya $dll126")
$hIndex126 = (Get-FileHash (Join-Path $instal 'wwwroot\index.html') -Algorithm SHA256).Hash
Cek 'index.html 1.2.6 sama dengan paket' ($hIndex126 -eq $idx126) $hIndex126 ("$hIndex126, seharusnya $idx126")
Cek 'bundel 1.2.6 terpasang' ((Test-Path (Join-Path $instal 'wwwroot\assets\index-BPNiweyi.js')) -and (Test-Path (Join-Path $instal 'wwwroot\assets\index-Bz7F3T1f.css'))) 'index-BPNiweyi.js + index-Bz7F3T1f.css ada' 'bundel 1.2.6 tidak lengkap'

$arp126 = Cari-Arp $kode126
Cek 'entri ARP 1.2.6 (ProductCode 1.2.6)' (($null -ne $arp126) -and ($arp126.DisplayVersion -eq '1.2.6')) ("{0} {1}" -f $arp126.DisplayName, $arp126.DisplayVersion) 'ARP 1.2.6 tidak ada/versi salah'
$daftar = @(Daftar-ArpSeruni)
Cek 'tepat satu entri Seruni sebelum peningkatan' ($daftar.Count -eq 1) ($daftar -join '; ') ($daftar -join '; ')

$lnkMenu = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Seruni 22 Bakery.lnk'
$lnkDesk = 'C:\Users\Public\Desktop\Seruni 22 Bakery.lnk'
Cek 'pintasan 1.2.6: Start Menu + Desktop publik' (((Target-Pintasan $lnkMenu) -eq $peluncur) -and ((Target-Pintasan $lnkDesk) -eq $peluncur)) ('target ' + (Target-Pintasan $lnkMenu)) 'pintasan tidak ada/target salah'
$tanda = (Get-AuthenticodeSignature $peluncur).Status
Cek 'tanda tangan dilaporkan apa adanya' ($tanda -eq 'NotSigned') 'NotSigned (sesuai paket yang belum ditandatangani)' ("status $tanda; klaim tanda tangan harus diperiksa ulang")

# ── 4. Data sebelum peningkatan (lewat API 1.2.6) ────────────────────────────
New-Item -ItemType Directory -Force -Path 'C:\data' | Out-Null
$outHost = Join-Path $bukti 'host-126.log'
$pHost = Start-Process -FilePath $hostExe -ArgumentList @("--Database:Path=$dbPath", "--Jwt:SigningKeyFile=$keyPath", '--Urls=http://127.0.0.1:5301') -WorkingDirectory $instal -RedirectStandardOutput $outHost -RedirectStandardError (Join-Path $bukti 'host-126-err.log') -PassThru
$jawab = Tunggu-Health 5301 120
Cek 'host 1.2.6 menjawab /health' ($null -ne $jawab) 'health 200' 'host 1.2.6 tidak menjawab dalam 120 detik'
$versiHealth = ''
if ($null -ne $jawab) { try { $versiHealth = ($jawab.Content | ConvertFrom-Json).version } catch {} }
Cek 'health melaporkan 1.2.6' ($versiHealth -eq '1.2.6') ("version=$versiHealth") ("version=$versiHealth")

$base = 'http://127.0.0.1:5301'
$spa = $null
try { $spa = Invoke-WebRequest -Uri "$base/" -UseBasicParsing -TimeoutSec 15 } catch {}
Cek 'SPA 1.2.6 memuat bundel 1.2.6' (($null -ne $spa) -and ($spa.StatusCode -eq 200) -and ($spa.Content -match 'index-BPNiweyi\.js')) ("status $($spa.StatusCode)") 'SPA tidak 200 / tidak merujuk bundel 1.2.6'

$masuk = Api 'POST' "$base/api/auth/login" $null (@{ username = 'admin'; password = 'admin123' } | ConvertTo-Json)
$token = $null
if ($masuk) { $token = $masuk.token }
Cek 'login bawaan admin berhasil di 1.2.6' ($null -ne $token) 'token diterima' 'login gagal'
$wajibGanti = $false
if ($masuk) { $wajibGanti = [bool]$masuk.mustChangePassword }
Cek 'akun bawaan wajib ganti sandi' $wajibGanti "mustChangePassword=$wajibGanti" "mustChangePassword=$wajibGanti"

$ganti = Api 'POST' "$base/api/auth/change-password" $token (@{ currentPassword = 'admin123'; newPassword = $sandiBaru } | ConvertTo-Json)
$masuk2 = Api 'POST' "$base/api/auth/login" $null (@{ username = 'admin'; password = $sandiBaru } | ConvertTo-Json)
$token2 = $null
if ($masuk2) { $token2 = $masuk2.token }
Cek 'sandi diganti lalu login sandi baru' ($null -ne $token2) 'login dengan sandi baru berhasil' 'login dengan sandi baru gagal'

$produk = Api 'POST' "$base/api/products" $token2 (@{ name = 'Roti Uji Peningkatan'; category = 'Roti'; sellingPriceMinorUnits = 15000; lines = @(); barcode = $null } | ConvertTo-Json)
$idProduk = 0
if ($produk) { $idProduk = [int]$produk.id }
Cek 'produk uji dibuat di 1.2.6' ($idProduk -gt 0) ("id=$idProduk") 'produk tidak terbuat'

$kodeBrand126 = 0
try {
  $rBrand = Invoke-WebRequest -Uri "$base/api/brand" -UseBasicParsing -TimeoutSec 10
  $kodeBrand126 = [int]$rBrand.StatusCode
} catch {
  try { $kodeBrand126 = [int]$_.Exception.Response.StatusCode } catch { $kodeBrand126 = 0 }
}
Cek '1.2.6 belum melayani /api/brand (fitur baru 1.2.7)' ($kodeBrand126 -ne 200) ("http $kodeBrand126") ("http $kodeBrand126")

$setMerek = Api 'PUT' "$base/api/settings" $token2 (@{ brand_name = 'Toko Uji Sandbox' } | ConvertTo-Json)
$settings126 = Api 'GET' "$base/api/settings" $token2 $null
$namaMerek126 = [string](Ambil $settings126 'brand_name')
Cek 'merek disetel di 1.2.6 (dibaca lewat /api/settings)' ($namaMerek126 -eq 'Toko Uji Sandbox') ("brand_name=$namaMerek126") ("brand_name=$namaMerek126")

Hentikan-Host $pHost
$ukuranDbPra = 0
if (Test-Path $dbPath) { $ukuranDbPra = (Get-Item $dbPath).Length }
Cek 'basis data pra-peningkatan terbentuk' ($ukuranDbPra -gt 0) ("uji.db $ukuranDbPra bita") 'uji.db tidak terbentuk'
$sidikDbPra = SidikBasisData
Catat 'basis data pra-peningkatan (utama+wal+shm)' 'INFO' $sidikDbPra

# ── 5. Tingkatkan ke 1.2.7 ───────────────────────────────────────────────────
$logUpgrade = Join-Path $bukti 'msi-127-upgrade.log'
$k127 = Jalankan-Msi '/i' $msi127 $null $logUpgrade
Cek 'peningkatan senyap MSI 1.2.7' ($k127 -eq 0) ("exit $k127") ("exit $k127")

$isiLog = ''
if (Test-Path $logUpgrade) {
  $bytes = [IO.File]::ReadAllBytes($logUpgrade)
  if (($bytes.Length -ge 2) -and ($bytes[0] -eq 0xFF) -and ($bytes[1] -eq 0xFE)) {
    $isiLog = [Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
  } elseif (($bytes.Length -ge 2) -and ($bytes[0] -eq 0x3C) -and ($bytes[1] -eq 0x00)) {
    $isiLog = [Text.Encoding]::Unicode.GetString($bytes)
  } else {
    $isiLog = [Text.Encoding]::UTF8.GetString($bytes)
  }
}
Cek 'produk 1.2.6 terdeteksi sebagai sasaran peningkatan' ($isiLog -match [regex]::Escape($kode126)) ("WIX_UPGRADE_DETECTED memuat $kode126") 'ProductCode 1.2.6 tidak muncul di log peningkatan'
Cek 'log MSI menyatakan sukses' ($isiLog -match 'Installation success or error status: 0') 'status 0' 'log tidak menyatakan status 0'
Cek 'RemoveExistingProducts dijalankan' ($isiLog -match 'RemoveExistingProducts') 'aksi pembongkaran produk lama ada di log' 'produk lama tidak dibongkar'

$arp127 = Cari-Arp $kode127
Cek 'entri ARP 1.2.7 (ProductCode 1.2.7)' (($null -ne $arp127) -and ($arp127.DisplayVersion -eq '1.2.7')) ("{0} {1}" -f $arp127.DisplayName, $arp127.DisplayVersion) 'ARP 1.2.7 tidak ada/versi salah'
Cek 'entri ARP 1.2.6 hilang' ($null -eq (Cari-Arp $kode126)) 'kunci 1.2.6 tidak ada lagi' 'kunci 1.2.6 masih ada'
$daftar2 = @(Daftar-ArpSeruni)
Cek 'tepat satu entri Seruni sesudah peningkatan' ($daftar2.Count -eq 1) ($daftar2 -join '; ') ($daftar2 -join '; ')

$berkas2 = @(Get-ChildItem -Path $instal -Recurse -File -ErrorAction SilentlyContinue)
Cek 'jumlah berkas terpasang 1.2.7' ($berkas2.Count -eq 393) ("$($berkas2.Count) berkas") ("$($berkas2.Count) berkas, seharusnya 393")

# Bandingkan 393 berkas terpasang dengan manifes dist\publish-single
$harap = @{}
foreach ($baris in Get-Content $manifes) {
  $bagian = $baris.Split('|')
  if ($bagian.Count -eq 3) { $harap[$bagian[0]] = '{0}|{1}' -f $bagian[1], $bagian[2] }
}
$beda = @()
$hanyaPasang = @()
$hanyaPaket = @()
$terhitung = 0
foreach ($f in $berkas2) {
  $rel = $f.FullName.Substring($instal.Length + 1)
  $h = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
  $terhitung++
  if (-not $harap.ContainsKey($rel)) { $hanyaPasang += $rel; continue }
  if ($harap[$rel] -ne ('{0}|{1}' -f $h, $f.Length)) { $beda += $rel }
  $harap.Remove($rel)
}
foreach ($k in $harap.Keys) { $hanyaPaket += $k }
Cek 'seluruh berkas terpasang identik dengan paket' (($beda.Count -eq 0) -and ($hanyaPasang.Count -eq 0) -and ($hanyaPaket.Count -eq 0) -and ($terhitung -eq 393)) ("$terhitung berkas diperiksa, 0 beda hash/ukuran, 0 selisih nama") ("beda=" + $beda.Count + " hanya-terpasang=" + $hanyaPasang.Count + " hanya-paket=" + $hanyaPaket.Count + " diperiksa=" + $terhitung + " contoh=" + (($beda + $hanyaPasang + $hanyaPaket | Select-Object -First 5) -join ', '))

$vPeluncur2 = (Get-Item $peluncur).VersionInfo.FileVersion
$vHost2 = (Get-Item $hostExe).VersionInfo.FileVersion
Cek 'versi peluncur 1.2.7' ($vPeluncur2 -like '1.2.7*') $vPeluncur2 ("versi berkas $vPeluncur2")
Cek 'versi host 1.2.7' ($vHost2 -like '1.2.7*') $vHost2 ("versi berkas $vHost2")
$hDll2 = (Get-FileHash (Join-Path $instal 'Seruni22Bakery.Host.dll') -Algorithm SHA256).Hash
Cek 'Host.dll 1.2.7 sama dengan paket' ($hDll2 -eq $dll127) $hDll2 ("$hDll2, seharusnya $dll127")
$hIndex127 = (Get-FileHash (Join-Path $instal 'wwwroot\index.html') -Algorithm SHA256).Hash
Cek 'index.html 1.2.7 sama dengan paket' ($hIndex127 -eq $idx127) $hIndex127 ("$hIndex127, seharusnya $idx127")
$asetBaru = (Test-Path (Join-Path $instal 'wwwroot\assets\index-DppHnhYj.js')) -and (Test-Path (Join-Path $instal 'wwwroot\assets\index-BNYyu2En.css'))
$asetLama = (Test-Path (Join-Path $instal 'wwwroot\assets\index-BPNiweyi.js')) -or (Test-Path (Join-Path $instal 'wwwroot\assets\index-Bz7F3T1f.css'))
Cek 'bundel 1.2.7 ada dan bundel 1.2.6 tersapu' ($asetBaru -and (-not $asetLama)) 'index-DppHnhYj.js + index-BNYyu2En.css ada; berkas bundel 1.2.6 tidak tersisa' ('asetBaru=' + $asetBaru + ' asetLama=' + $asetLama)

Cek 'pintasan tetap menunjuk instalasi' (((Target-Pintasan $lnkMenu) -eq $peluncur) -and ((Target-Pintasan $lnkDesk) -eq $peluncur)) ('target ' + (Target-Pintasan $lnkMenu)) 'pintasan hilang/target berubah'

# ── 6. Basis data yang sama dibuka oleh 1.2.7 ────────────────────────────────
$sidikDbSesudahPasang = SidikBasisData
Cek 'basis data tidak disentuh proses pasang' ($sidikDbSesudahPasang -eq $sidikDbPra) $sidikDbSesudahPasang ("berubah: $sidikDbPra -> $sidikDbSesudahPasang")

$outHost2 = Join-Path $bukti 'host-127.log'
$pHost2 = Start-Process -FilePath $hostExe -ArgumentList @("--Database:Path=$dbPath", "--Jwt:SigningKeyFile=$keyPath", '--Urls=http://127.0.0.1:5302') -WorkingDirectory $instal -RedirectStandardOutput $outHost2 -RedirectStandardError (Join-Path $bukti 'host-127-err.log') -PassThru
$jawab2 = Tunggu-Health 5302 120
Cek 'host hasil peningkatan menjawab /health' ($null -ne $jawab2) 'health 200' 'host 1.2.7 tidak menjawab dalam 120 detik'
$versiHealth2 = ''
if ($null -ne $jawab2) { try { $versiHealth2 = ($jawab2.Content | ConvertFrom-Json).version } catch {} }
Cek 'health melaporkan 1.2.7' ($versiHealth2 -eq '1.2.7') ("version=$versiHealth2") ("version=$versiHealth2")

$base2 = 'http://127.0.0.1:5302'
$spa2 = $null
try { $spa2 = Invoke-WebRequest -Uri "$base2/" -UseBasicParsing -TimeoutSec 15 } catch {}
Cek 'SPA 1.2.7 memuat bundel 1.2.7' (($null -ne $spa2) -and ($spa2.StatusCode -eq 200) -and ($spa2.Content -match 'index-DppHnhYj\.js')) ("status $($spa2.StatusCode)") 'SPA tidak 200 / tidak merujuk bundel 1.2.7'
$asetJs = $null
try { $asetJs = Invoke-WebRequest -Uri "$base2/assets/index-DppHnhYj.js" -UseBasicParsing -TimeoutSec 30 } catch {}
Cek 'aset JS 1.2.7 dilayani' (($null -ne $asetJs) -and ($asetJs.StatusCode -eq 200) -and ($asetJs.RawContentLength -eq 582985)) ("status $($asetJs.StatusCode), $($asetJs.RawContentLength) bita") 'aset tidak 200 / ukuran lain'

$masuk3 = Api 'POST' "$base2/api/auth/login" $null (@{ username = 'admin'; password = $sandiBaru } | ConvertTo-Json)
$token3 = $null
if ($masuk3) { $token3 = $masuk3.token }
Cek 'sandi dari 1.2.6 masih berlaku di 1.2.7' ($null -ne $token3) 'login dengan sandi hasil ganti di 1.2.6 berhasil' 'login gagal setelah peningkatan'
$wajibGanti3 = $true
if ($masuk3) { $wajibGanti3 = [bool]$masuk3.mustChangePassword }
Cek 'status wajib-ganti-sandi tidak kembali menyala' (-not $wajibGanti3) "mustChangePassword=$wajibGanti3" "mustChangePassword=$wajibGanti3"

$daftarProduk = Api 'GET' "$base2/api/products" $token3 $null
$adaDiDaftar = $false
if ($null -ne $daftarProduk) {
  foreach ($p in @($daftarProduk)) { if ([int](Ambil $p 'id') -eq $idProduk) { $adaDiDaftar = $true } }
}
Cek 'produk dari 1.2.6 muncul di daftar produk 1.2.7' $adaDiDaftar ("id=$idProduk ada di daftar") ("id=$idProduk tidak ada di daftar")

$produk127 = Api 'GET' "$base2/api/products/$idProduk" $token3 $null
$cocokProduk = $false
$rincianProduk = 'tidak terbaca'
if ($produk127) {
  $idP = [int](Ambil $produk127 'id')
  $namaP = [string](Ambil $produk127 'name')
  $hargaP = [long](Ambil $produk127 'sellingPriceMinorUnits')
  $rincianProduk = "id=$idP name=$namaP harga=$hargaP"
  $cocokProduk = ($idP -eq $idProduk) -and ($namaP -eq 'Roti Uji Peningkatan') -and ($hargaP -eq 15000)
}
Cek 'produk dari 1.2.6 terbaca utuh di 1.2.7' $cocokProduk $rincianProduk 'produk tidak ditemukan/nilainya berubah'

$merek127 = Api 'GET' "$base2/api/brand" $null $null
$namaMerek127 = [string](Ambil $merek127 'name')
$jmlKunci = 0
if ($merek127) { $jmlKunci = @($merek127.PSObject.Properties).Count }
Cek 'merek dari 1.2.6 masih tersimpan' ($namaMerek127 -eq 'Toko Uji Sandbox') ("name=$namaMerek127") ("name=$namaMerek127")
Cek 'endpoint /api/brand 1.2.7 anonim tepat 3 kunci' ($jmlKunci -eq 3) ("$jmlKunci kunci") ("$jmlKunci kunci")

$cekDb = Api 'GET' "$base2/api/system/self-check" $token3 $null
$gagalCek = -1
$jmlCek = 0
$versiSkema = ''
if ($cekDb) {
  $gagalCek = [int](Ambil $cekDb 'jumlahGagal')
  $jmlCek = [int](Ambil $cekDb 'jumlahPemeriksaan')
  foreach ($c in @(Ambil $cekDb 'checks')) {
    if ([string](Ambil $c 'name') -eq 'versi skema') { $versiSkema = [string](Ambil $c 'detail') }
  }
}
Cek 'periksa-diri basis data: 0 gagal' (($gagalCek -eq 0) -and ($jmlCek -ge 9)) ("$jmlCek pemeriksaan, $gagalCek gagal") ("$jmlCek pemeriksaan, $gagalCek gagal")
Cek 'skema basis data tetap 17 setelah peningkatan' ($versiSkema -match 'terpasang 17') $versiSkema ('detail: ' + $versiSkema)

$setMerek2 = Api 'PUT' "$base2/api/settings" $token3 (@{ brand_name = 'Toko Uji Sandbox 127' } | ConvertTo-Json)
$merekBaru = Api 'GET' "$base2/api/brand" $null $null
$namaBaru = [string](Ambil $merekBaru 'name')
Cek 'merek bisa diganti di 1.2.7 hasil peningkatan' ($namaBaru -eq 'Toko Uji Sandbox 127') ("name=$namaBaru") ("name=$namaBaru")

$setLogo = Api 'PUT' "$base2/api/settings" $token3 (@{ brand_logo = $logoPng } | ConvertTo-Json)
$merekLogo = Api 'GET' "$base2/api/brand" $null $null
$logoKembali = [string](Ambil $merekLogo 'logo')
Cek 'logo bisa dipasang di 1.2.7 hasil peningkatan' ($logoKembali -eq $logoPng) ("logo " + $logoKembali.Length + " karakter, sama dengan yang dikirim") ("logo dikembalikan '" + $logoKembali + "'")
$hapusLogo = Api 'PUT' "$base2/api/settings" $token3 (@{ brand_logo = '' } | ConvertTo-Json)
$merekTanpaLogo = Api 'GET' "$base2/api/brand" $null $null
$logoKosong = 'x'
if ($null -ne $merekTanpaLogo) { $logoKosong = [string](Ambil $merekTanpaLogo 'logo') }
Cek 'logo bisa dihapus lagi' ([string]::IsNullOrEmpty($logoKosong)) 'logo kembali kosong' ("logo='" + $logoKosong + "'")

Hentikan-Host $pHost2
$ukuranDbPasca = 0
if (Test-Path $dbPath) { $ukuranDbPasca = (Get-Item $dbPath).Length }
Catat 'basis data pasca-peningkatan' 'INFO' ("$ukuranDbPasca bita")
Cek 'basis data masih ada setelah dipakai 1.2.7' ($ukuranDbPasca -gt 0) ("uji.db $ukuranDbPasca bita") 'uji.db hilang'

# ── 7. Copot 1.2.7 ───────────────────────────────────────────────────────────
$kCopot = Jalankan-Msi '/x' $null $kode127 (Join-Path $bukti 'msi-127-uninstall.log')
Cek 'copot senyap MSI 1.2.7' ($kCopot -eq 0) ("exit $kCopot") ("exit $kCopot")
Start-Sleep -Seconds 3

Cek 'folder instalasi hilang' (-not (Test-Path $instal)) 'folder program terhapus' 'folder program masih ada'
Cek 'pintasan hilang' ((-not (Test-Path $lnkMenu)) -and (-not (Test-Path $lnkDesk))) 'Start Menu dan Desktop publik terhapus' 'pintasan masih ada'
Cek 'tidak ada entri ARP tersisa' (@(Daftar-ArpSeruni).Count -eq 0) 'daftar program bersih' (@(Daftar-ArpSeruni) -join '; ')
Cek 'data di luar folder program tetap utuh' ((Test-Path $dbPath) -and (Test-Path $keyPath)) 'uji.db dan uji.key masih ada setelah copot' 'data uji ikut terhapus'
$sisa = @(Get-Process | Where-Object { $_.ProcessName -like 'Seruni22Bakery*' })
Cek 'tanpa proses yatim' ($sisa.Count -eq 0) 'tidak ada proses Seruni22Bakery tersisa' ("masih " + $sisa.Count + " proses")

Catat 'rekap' $(if ($gagal -eq 0) { 'PASS' } else { 'FAIL' }) ("lulus=$lulus gagal=$gagal")
Set-Content -Path $selesai -Value ('selesai ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8

Stop-Transcript | Out-Null
Copy-Item -Path (Join-Path $bukti 'validasi.log') -Destination (Join-Path $bukti 'validasi-copy.log') -Force -ErrorAction SilentlyContinue
shutdown.exe /s /t 30 /f
