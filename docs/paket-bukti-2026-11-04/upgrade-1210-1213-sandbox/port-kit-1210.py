# -*- coding: utf-8 -*-
# Port harness uji sandbox 1.2.12 -> 1.2.13 menjadi 1.2.10 -> 1.2.13.
# Semua penggantian bersifat tekstual dan eksplisit; nilai 1.2.10 dibaca dari isi MSI
# (ProductCode/asset) dan CHANGELOG (hash+ukuran berkas yang sudah dicocokkan byte).
import io, os, re, sys

ASAL_UJI = r'C:\tmp\kit-uji-1213\uji-1213.ps1'
ASAL_RUN = r'C:\tmp\kit-uji-1213\run-sandbox.ps1'
TUJUAN   = r'C:\tmp\kit-uji-1210-1213'

SHA_MSI_1210  = '872A3C39FE6F906DD4CED13CA434FDB7351B266147DBBD26E31A4384FFF4B4AF'
SHA_EXE_1210  = 'C882B69873CBD7DC51EF58761FE6D38FF2559FBE35141399B7023BCED8CD78C5'
UKURAN_MSI_1210 = 50395652
UKURAN_EXE_1210 = 42665720
KODE_1210 = '{F74073DE-74DE-47CB-B9A7-358F0DE5E8FF}'
BUNDEL_JS_LAMA = 'index-B8lfbLcy.js'
BUNDEL_CSS_LAMA = 'index-CoYZy02P.css'


def baca(path):
    with io.open(path, 'r', encoding='utf-8', newline='') as f:
        return f.read()


def tulis(path, teks):
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(teks)


def ganti(teks, lama, baru, jumlah_perkiraan, nama):
    # Berkas harness memakai LF; literal di skrip ini ditulis CRLF agar terbaca jelas.
    lama = lama.replace('\r\n', '\n')
    baru = baru.replace('\r\n', '\n')
    ketemu = teks.count(lama)
    if ketemu != jumlah_perkiraan:
        sys.exit('GAGAL: penggantian %s menemukan %d occurrence (%d diharapkan): %r'
                 % (nama, ketemu, jumlah_perkiraan, lama[:70]))
    return teks.replace(lama, baru)


os.makedirs(TUJUAN, exist_ok=True)

# ============================== uji-1210-1213.ps1 =============================
t = baca(ASAL_UJI)

# (1) Blok pagar versi EXE diganti lebih dulu, sebelum rename generik, memakai teks asli.
BLOK_LAMA = (
    "# -- 12. Pagar versi EXE dan pagar kanal MSI vs EXE -------------------------\r\n"
    "# Paket EXE 1.2.12 yang lebih lama belum memiliki pagar versi, jadi skenario \"turun versi\r\n"
    "# EXE\" hanya bisa diuji dengan menaikkan penanda versi terpasang (kontrol negatif untuk\r\n"
    "# kode pagar di 1.2.13), lalu penanda dikembalikan.\r\n"
    "Set-ItemProperty -Path 'HKCU:\\Software\\Seruni22Bakery' -Name 'Version' -Value '9.9.9'\r\n"
    "$logGuardVersi = Join-Path $bukti 'exe-1213-versi-guard.log'\r\n"
    "$kGuardVersi = Jalankan-Inno $exe1213 @(('/DIR=' + $instalExe)) $logGuardVersi\r\n"
    "$isiGuardVersi = Baca-Teks $logGuardVersi\r\n"
    "Set-ItemProperty -Path 'HKCU:\\Software\\Seruni22Bakery' -Name 'Version' -Value '1.2.13'\r\n"
    "Cek 'EXE 1.2.13 menolak dipasang bila versi lebih baru tercatat' ($kGuardVersi -ne 0) (\"exit $kGuardVersi\") (\"exit $kGuardVersi, seharusnya bukan 0\")\r\n"
    "Cek 'log penolakan EXE menyebut versi lebih baru' (($isiGuardVersi -match 'Pagar versi') -and ($isiGuardVersi -match '9\\.9\\.9')) 'alasan versi tercatat' 'alasan versi tidak tercatat'\r\n"
    "Cek 'EXE 1.2.13 tetap terpasang' (((Get-Item $peluncurExe).VersionInfo.FileVersion) -like '1.2.13*') ((Get-Item $peluncurExe).VersionInfo.FileVersion) 'versi EXE berubah setelah penolakan'\r\n"
    "Cek 'penanda versi dikembalikan ke 1.2.13' ((Get-ItemProperty 'HKCU:\\Software\\Seruni22Bakery').Version -eq '1.2.13') 'Version=1.2.13' ((Get-ItemProperty 'HKCU:\\Software\\Seruni22Bakery').Version)\r\n"
)
BLOK_BARU = (
    "# -- 12. Pagar versi EXE dan pagar kanal MSI vs EXE -------------------------\r\n"
    "# EXE 1.2.10 sudah memuat pagar versi (BandingkanVersi di InitializeSetup), jadi skenario\r\n"
    "# \"turun versi EXE\" diuji dengan paket EXE 1.2.10 yang sebenarnya di atas EXE 1.2.13;\r\n"
    "# penanda HKCU tidak dikarang-karang.\r\n"
    "$logGuardVersi = Join-Path $bukti 'exe-1210-versi-guard.log'\r\n"
    "$kGuardVersi = Jalankan-Inno $exe1210 @(('/DIR=' + $instalExe)) $logGuardVersi\r\n"
    "$isiGuardVersi = Baca-Teks $logGuardVersi\r\n"
    "Cek 'EXE 1.2.10 ditolak di atas EXE 1.2.13' ($kGuardVersi -ne 0) (\"exit $kGuardVersi\") (\"exit $kGuardVersi, seharusnya bukan 0\")\r\n"
    "Cek 'log penolakan EXE menyebut pagar versi terhadap 1.2.13' (($isiGuardVersi -match 'Pagar versi') -and ($isiGuardVersi -match '1\\.2\\.13')) 'alasan versi tercatat' 'alasan versi tidak tercatat'\r\n"
    "Cek 'EXE 1.2.13 tetap terpasang' (((Get-Item $peluncurExe).VersionInfo.FileVersion) -like '1.2.13*') ((Get-Item $peluncurExe).VersionInfo.FileVersion) 'versi EXE berubah setelah penolakan'\r\n"
    "Cek 'penanda versi HKCU tetap 1.2.13' ((Get-ItemProperty 'HKCU:\\Software\\Seruni22Bakery').Version -eq '1.2.13') 'Version=1.2.13' ((Get-ItemProperty 'HKCU:\\Software\\Seruni22Bakery').Version)\r\n"
)
t = ganti(t, BLOK_LAMA, BLOK_BARU, 1, 'blok pagar versi EXE')

# (2) Pemeriksaan aset sebelum peningkatan, supaya klaim "aset lama tersapu" berpijak.
ANCHOR_ASET = (
    "Cek 'versi peluncur 1.2.12' ((Get-Item $peluncur).VersionInfo.FileVersion -like '1.2.12*') "
    "((Get-Item $peluncur).VersionInfo.FileVersion) 'versi peluncur 1.2.12 tidak sesuai'\r\n"
)
ANCHOR_ASET_BARU = ANCHOR_ASET + (
    "$asetPra = @(Get-ChildItem (Join-Path $instal 'wwwroot\\assets') -File -ErrorAction SilentlyContinue)\r\n"
    "Cek 'aset 1.2.10 terpasang sebelum peningkatan' `\r\n"
    "  (($asetPra.Count -eq 2) -and (Test-Path (Join-Path $instal ('wwwroot\\assets\\' + $bundelJsLama))) -and (Test-Path (Join-Path $instal ('wwwroot\\assets\\' + $bundelCssLama)))) `\r\n"
    "  (\"aset=\" + (($asetPra | ForEach-Object { $_.Name }) -join ', ')) (\"aset tidak sesuai: \" + (($asetPra | ForEach-Object { $_.Name }) -join ', '))\r\n"
)
t = ganti(t, ANCHOR_ASET, ANCHOR_ASET_BARU, 1, 'anchor aset pra-peningkatan')

# (3) Konstanta 1.2.10 menggantikan konstanta 1.2.12 (nama variabel masih 1212 sampai tahap 4).
t = ganti(t, "$shaMsi1212  = '5EE109F9ECE7CD24B4125ADB5F999AEDD8CD90D701A77BB9B2D8E9A8BD8FC859'",
          "$shaMsi1212  = '%s'" % SHA_MSI_1210, 1, 'hash MSI lama')
t = ganti(t, "$shaExe1212  = 'F43F4E8583CC1DF84354039925F45046AAFE93A6F3996B112C80501229A506C1'",
          "$shaExe1212  = '%s'" % SHA_EXE_1210, 1, 'hash EXE lama')
t = ganti(t, "$ukuranMsi1212 = 50399748", "$ukuranMsi1212 = %d" % UKURAN_MSI_1210, 1, 'ukuran MSI lama')
t = ganti(t, "$ukuranExe1212 = 42675344", "$ukuranExe1212 = %d" % UKURAN_EXE_1210, 1, 'ukuran EXE lama')
t = ganti(t, "$kode1212 = '{1D0B5C27-C2B9-49AD-A675-BA390B33E3E3}'",
          "$kode1212 = '%s'" % KODE_1210, 1, 'ProductCode lama')

# (4) Nama bundel lama dipakai oleh pemeriksaan pra-peningkatan.
t = ganti(t, "$bundelJs = 'index-BZfyiTsC.js'\r\n",
          "$bundelJs = 'index-BZfyiTsC.js'\r\n$bundelJsLama = '%s'\r\n" % BUNDEL_JS_LAMA, 1, 'konstanta bundelJs')
t = ganti(t, "$bundelCss = 'index-CUfTbA7B.css'\r\n",
          "$bundelCss = 'index-CUfTbA7B.css'\r\n$bundelCssLama = '%s'\r\n" % BUNDEL_CSS_LAMA, 1, 'konstanta bundelCss')

# (5) Rename generik: hanya token versi lama yang tersentuh.
n1212 = t.count('1.2.12')
n1212num = len(re.findall(r'1212', t))
t = t.replace('1.2.12', '1.2.10')
t = t.replace('1212', '1210')
assert '1.2.12' not in t and '1212' not in t, 'sisa token 1.2.12/1212'

# (6) Judul berkas + komentar kit.
t = t.replace('# Uji paket 1.2.13 di Windows Sandbox bersih:',
              '# Uji peningkatan 1.2.10 -> 1.2.13 di Windows Sandbox bersih:')
# "1.2.10 -> 1.2.13" pada baris header B sudah benar dari rename.
tulis(os.path.join(TUJUAN, 'uji-1210-1213.ps1'), t)

# ============================== run-sandbox.ps1 ==============================
r = baca(ASAL_RUN)
r = r.replace('# Peluncur uji pasang/peningkatan 1.2.13 di Windows Sandbox.',
              '# Peluncur uji pasang/peningkatan 1.2.10 -> 1.2.13 di Windows Sandbox.')
r = r.replace("'Seruni22Bakery_v1.2.12_x64.msi',", "'Seruni22Bakery_v1.2.10_x64.msi',")
r = r.replace("'Seruni22Bakery_Setup_v1.2.12.exe',", "'Seruni22Bakery_Setup_v1.2.10.exe',")
r = r.replace("'uji-1213.ps1'", "'uji-1210-1213.ps1'")
r = r.replace('C:\\share\\uji-1213.ps1', 'C:\\share\\uji-1210-1213.ps1')
assert '1.2.12' not in r and '1212' not in r and 'uji-1213.ps1' not in r, 'sisa token pada run-sandbox'
tulis(os.path.join(TUJUAN, 'run-sandbox.ps1'), r)

print('port OK: %d label 1.2.12 -> 1.2.10, %d token 1212 -> 1210' % (n1212, n1212num))
print('file:', os.listdir(TUJUAN))
