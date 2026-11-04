# Port kit uji sandbox 1.2.10 -> 1.2.13 menjadi 1.2.9 -> 1.2.13.
# Semua konstanta 1.2.9 dibaca ulang dari paket di dist\installer dan dari tabel MSI,
# bukan dari ingatan. Skrip ini menolak jalan bila satu pun penggantian tidak cocok
# dengan jumlah occurrence yang diharapkan.
import hashlib
import os
import shutil
import sys

SUMBER = r'C:\tmp\kit-uji-1210-1213'
TUJUAN = r'C:\tmp\kit-uji-129-1213'
REPO = r'D:\bakery-desktop'

# --- konstanta yang diverifikasi dari disk / tabel MSI pada sesi ini ---
SHA_MSI_129 = '4CF092CE05CFB0301D2740CC136322135DC9F506A482C807BEA020782052D635'
SHA_EXE_129 = 'FD233BE4E4A8441DD2E63349B723466EE4AB803F41E6D6B357B97E05F55143B7'
UKURAN_MSI_129 = 50391554
UKURAN_EXE_129 = 42662274
KODE_129 = '{7609C8A3-2349-4606-A02D-652FE121FB78}'
BUNDEL_JS_129 = 'index-BksMbgv0.js'
BUNDEL_CSS_129 = 'index-CoYZy02P.css'   # sama dengan 1.2.10; dibaca dari File table MSI 1.2.9
SHA_MSI_1213 = 'EB3F40FB0FA2FE1FB7E09BEE733DB524A8B29FB55AF367B1BD8D0DF1F5AFD132'
SHA_EXE_1213 = 'C1A0AF29BD8F5BF9C439CADEF3C1D8C891DA0C49A3271384192fd682c444ed9d'.upper()


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for blok in iter(lambda: f.read(1024 * 1024), b''):
            h.update(blok)
    return h.hexdigest().upper()


def ganti(teks, lama, baru, jumlah_perkiraan, nama):
    lama = lama.replace('\r\n', '\n')
    baru = baru.replace('\r\n', '\n')
    ketemu = teks.count(lama)
    if ketemu != jumlah_perkiraan:
        sys.exit('GAGAL: penggantian %s menemukan %d occurrence (%d diharapkan): %r'
                 % (nama, ketemu, jumlah_perkiraan, lama[:60]))
    return teks.replace(lama, baru)


# --- 1. folder kit baru ---
if os.path.exists(TUJUAN):
    sys.exit('GAGAL: %s sudah ada; jangan menimpa kit lama.' % TUJUAN)
os.makedirs(TUJUAN)

# --- 2. salin paket dan verifikasi checksum ---
paket = [
    (os.path.join(REPO, 'dist', 'installer', 'Seruni22Bakery_v1.2.9_x64.msi'),
     'Seruni22Bakery_v1.2.9_x64.msi', SHA_MSI_129, UKURAN_MSI_129),
    (os.path.join(REPO, 'dist', 'installer', 'Seruni22Bakery_Setup_v1.2.9.exe'),
     'Seruni22Bakery_Setup_v1.2.9.exe', SHA_EXE_129, UKURAN_EXE_129),
    (os.path.join(SUMBER, 'Seruni22Bakery_v1.2.13_x64.msi'),
     'Seruni22Bakery_v1.2.13_x64.msi', SHA_MSI_1213, 50416132),
    (os.path.join(SUMBER, 'Seruni22Bakery_Setup_v1.2.13.exe'),
     'Seruni22Bakery_Setup_v1.2.13.exe', SHA_EXE_1213, 42672292),
]
for asal, nama, harap, ukuran in paket:
    nyata_hash = sha256(asal)
    nyata_ukuran = os.path.getsize(asal)
    if nyata_hash != harap or nyata_ukuran != ukuran:
        sys.exit('GAGAL: paket %s tidak sesuai harapan (hash %s vs %s, ukuran %d vs %d)'
                 % (nama, nyata_hash, harap, nyata_ukuran, ukuran))
    shutil.copy2(asal, os.path.join(TUJUAN, nama))
    print('OK paket  %s  %d bita  %s' % (nama, nyata_ukuran, nyata_hash.lower()))

manifes_asal = os.path.join(SUMBER, 'hash-publish-1213.txt')
if sha256(manifes_asal) != 'D32CBAC2ED9952E09C3E4F5EFF8D7F70F0EF447EB810A2A0385A683D738B11F1':
    sys.exit('GAGAL: hash-publish-1213.txt tidak dikenali')
shutil.copy2(manifes_asal, os.path.join(TUJUAN, 'hash-publish-1213.txt'))
baris_manifes = [b for b in open(manifes_asal, encoding='utf-8').read().splitlines() if b.strip()]
if len(baris_manifes) != 393:
    sys.exit('GAGAL: manifes punya %d baris, seharusnya 393' % len(baris_manifes))
print('OK manifes 393 baris')

# --- 3. port skrip tamu ---
path_uji = os.path.join(SUMBER, 'uji-1210-1213.ps1')
teks = open(path_uji, encoding='utf-8').read()

teks = ganti(teks, "'Seruni22Bakery_v1.2.10_x64.msi'", "'Seruni22Bakery_v1.2.9_x64.msi'", 1, 'nama MSI')
teks = ganti(teks, "'Seruni22Bakery_Setup_v1.2.10.exe'", "'Seruni22Bakery_Setup_v1.2.9.exe'", 1, 'nama EXE')
teks = ganti(teks, "'872A3C39FE6F906DD4CED13CA434FDB7351B266147DBBD26E31A4384FFF4B4AF'",
             "'%s'" % SHA_MSI_129, 1, 'SHA MSI')
teks = ganti(teks, "'C882B69873CBD7DC51EF58761FE6D38FF2559FBE35141399B7023BCED8CD78C5'",
             "'%s'" % SHA_EXE_129, 1, 'SHA EXE')
teks = ganti(teks, '$ukuranMsi1210 = 50395652', '$ukuranMsi1210 = %d' % UKURAN_MSI_129, 1, 'ukuran MSI')
teks = ganti(teks, '$ukuranExe1210 = 42665720', '$ukuranExe1210 = %d' % UKURAN_EXE_129, 1, 'ukuran EXE')
teks = ganti(teks, "'{F74073DE-74DE-47CB-B9A7-358F0DE5E8FF}'", "'%s'" % KODE_129, 1, 'ProductCode')
teks = ganti(teks, "'index-B8lfbLcy.js'", "'%s'" % BUNDEL_JS_129, 1, 'bundel JS lama')
teks = ganti(teks, "'index-CoYZy02P.css'", "'%s'" % BUNDEL_CSS_129, 1, 'bundel CSS lama')

n1 = teks.count('1.2.10')
teks = teks.replace('1.2.10', '1.2.9')
n2 = teks.count('1210')
teks = teks.replace('1210', '129')
print('OK label  1.2.10->1.2.9 sebanyak %d, 1210->129 sebanyak %d' % (n1, n2))
for sisa in ['1.2.10', '1210', '872A3C39', 'C882B698', 'F74073DE', 'B8lfbLcy', '50395652', '42665720']:
    if sisa in teks:
        sys.exit('GAGAL: sisa token %r masih ada di skrip hasil port' % sisa)

with open(os.path.join(TUJUAN, 'uji-129-1213.ps1'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(teks)
print('OK skrip tamu %d bita' % len(teks.encode('utf-8')))

# --- 4. port peluncur ---
luncur = open(os.path.join(SUMBER, 'run-sandbox.ps1'), encoding='utf-8').read()
luncur = ganti(luncur, '1.2.10 -> 1.2.13', '1.2.9 -> 1.2.13', 1, 'komentar peluncur')
luncur = ganti(luncur, "'Seruni22Bakery_v1.2.10_x64.msi'", "'Seruni22Bakery_v1.2.9_x64.msi'", 1, 'wajib MSI')
luncur = ganti(luncur, "'Seruni22Bakery_Setup_v1.2.10.exe'", "'Seruni22Bakery_Setup_v1.2.9.exe'", 1, 'wajib EXE')
luncur = ganti(luncur, 'uji-1210-1213.ps1', 'uji-129-1213.ps1', 2, 'nama skrip tamu')
if '1210' in luncur or '1.2.10' in luncur:
    sys.exit('GAGAL: peluncur masih memuat token 1210/1.2.10')
with open(os.path.join(TUJUAN, 'run-sandbox.ps1'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(luncur)
print('OK peluncur %d bita' % len(luncur.encode('utf-8')))

# --- 5. KIT-SHA256SUMS (PANDUAN ditulis setelah ini) ---
daftar = ['Seruni22Bakery_v1.2.9_x64.msi', 'Seruni22Bakery_v1.2.13_x64.msi',
          'Seruni22Bakery_Setup_v1.2.9.exe', 'Seruni22Bakery_Setup_v1.2.13.exe',
          'hash-publish-1213.txt', 'uji-129-1213.ps1', 'run-sandbox.ps1']
baris = ['%s *%s' % (sha256(os.path.join(TUJUAN, n)).lower(), n) for n in daftar]
with open(os.path.join(TUJUAN, 'KIT-SHA256SUMS-sebelum-panduan.txt'), 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(baris) + '\n')
for b in baris:
    print('SUM ' + b)
print('SELESAI port; langkah berikutnya tulis PANDUAN-KIT.md lalu jalankan -Periksa')
