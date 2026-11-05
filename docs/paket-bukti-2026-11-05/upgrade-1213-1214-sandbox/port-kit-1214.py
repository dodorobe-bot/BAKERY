# Memport kit uji sandbox 1.2.9 -> 1.2.13 menjadi 1.2.13 -> 1.2.14.
#
# Tidak ada konstanta yang diketik dari ingatan untuk sisi baru: SHA-256 dan ukuran paket
# dibaca dari disk, ProductCode dan nama bundel asset dibaca dari tabel Property/File MSI,
# dan hash Seruni22Bakery.Host.dll dibaca dari manifes publish hasil build-pro. Konstanta
# sisi lama (1.2.9) memang nilai yang tertanam di harness sumber, karena paket 1.2.9 tidak
# lagi ada di folder keluaran; setiap penggantian diverifikasi jumlah occurrence-nya.
#
# Urutan memakai token antara supaya digit di dalam hash/GUID tidak ikut tersapu rename
# label: (1) semua konstanta -> token, (2) rename label versi dua arah, (3) penjaga
# anti-sisa dijalankan pada teks tanpa konstanta, (4) token diisi nilai akhir.
import hashlib
import os
import re
import shutil
import sys

REPO = r'D:\bakery-desktop'
SUMBER = os.path.join(REPO, 'docs', 'paket-bukti-2026-11-04', 'upgrade-129-1213-sandbox')
TUJUAN = r'C:\tmp\kit-uji-1213-1214'

LAMA = '1.2.13'          # titik awal peningkatan yang diuji
BARU = '1.2.14'          # paket yang dibuktikan
LAMA_NUM = '1213'
BARU_NUM = '1214'

# konstanta 1.2.9 yang sekarang tertanam di harness sumber
SHA_MSI_129 = '4CF092CE05CFB0301D2740CC136322135DC9F506A482C807BEA020782052D635'
SHA_EXE_129 = 'FD233BE4E4A8441DD2E63349B723466EE4AB803F41E6D6B357B97E05F55143B7'
UKURAN_MSI_129 = 50391554
UKURAN_EXE_129 = 42662274
KODE_129 = '{7609C8A3-2349-4606-A02D-652FE121FB78}'
BUNDEL_JS_129 = 'index-BksMbgv0.js'
BUNDEL_CSS_129 = 'index-CoYZy02P.css'


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for blok in iter(lambda: f.read(1024 * 1024), b''):
            h.update(blok)
    return h.hexdigest().upper()


def ganti(teks, lama, baru, jumlah_perkiraan, nama):
    ketemu = teks.count(lama)
    if ketemu != jumlah_perkiraan:
        sys.exit('GAGAL: penggantian %s menemukan %d occurrence (%d diharapkan): %r'
                 % (nama, ketemu, jumlah_perkiraan, lama[:70]))
    return teks.replace(lama, baru)


def baca_msi(path, sql):
    import msilib
    db = msilib.OpenDatabase(path, msilib.MSIDBOPEN_READONLY)
    view = db.OpenView(sql)
    view.Execute(None)
    keluar = []
    rec = view.Fetch()
    while rec:
        keluar.append(rec.GetString(1))
        rec = view.Fetch()
    view.Close()
    return keluar


def properti_msi(path):
    baris = baca_msi(path, "SELECT `Value` FROM `Property` WHERE `Property` = 'ProductCode'")
    if len(baris) != 1:
        sys.exit('GAGAL: ProductCode %s = %r' % (path, baris))
    return baris[0]


def bundel_msi(path):
    # LIKE ditolak oleh dialect SQL MSI di mesin ini (galat 2236), jadi seluruh
    # tabel File dibaca lalu disaring di Python.
    nama = []
    for mentah in baca_msi(path, "SELECT `FileName` FROM `File`"):
        panjang = mentah.split('|')[-1]
        if re.match(r'^index-[A-Za-z0-9]+\.(js|css)$', panjang):
            nama.append(panjang)
    js = sorted(set(n for n in nama if n.endswith('.js')))
    css = sorted(set(n for n in nama if n.endswith('.css')))
    if len(js) != 1 or len(css) != 1:
        sys.exit('GAGAL: bundel asset %s = js %r css %r (harus satu-satu)' % (path, js, css))
    return js[0], css[0]


def fakta_paket(ver):
    msi = os.path.join(REPO, 'dist', 'installer', 'Seruni22Bakery_v%s_x64.msi' % ver)
    exe = os.path.join(REPO, 'dist', 'installer', 'Seruni22Bakery_Setup_v%s.exe' % ver)
    for p in (msi, exe):
        if not os.path.exists(p):
            sys.exit('GAGAL: paket tidak ada: %s' % p)
    js, css = bundel_msi(msi)
    f = {
        'msi': os.path.basename(msi), 'sha_msi': sha256(msi), 'ukuran_msi': os.path.getsize(msi),
        'exe': os.path.basename(exe), 'sha_exe': sha256(exe), 'ukuran_exe': os.path.getsize(exe),
        'kode': properti_msi(msi), 'js': js, 'css': css,
    }
    print('FAKTA %s  %s %d bita %s' % (ver, f['msi'], f['ukuran_msi'], f['sha_msi'].lower()))
    print('        %s %d bita %s' % (f['exe'], f['ukuran_exe'], f['sha_exe'].lower()))
    print('        ProductCode %s  bundel %s + %s' % (f['kode'], f['js'], f['css']))
    return f


def host_dll(ver_baru):
    manifes = os.path.join(REPO, 'dist', 'publish-single.manifest.txt')
    dengan = [b for b in open(manifes, encoding='utf-8').read().splitlines()
              if b.endswith(' Seruni22Bakery.Host.dll')]
    if len(dengan) != 1:
        sys.exit('GAGAL: baris Seruni22Bakery.Host.dll di manifes publish = %d' % len(dengan))
    h, ukuran, nama = dengan[0].split(None, 2)
    print('FAKTA %s  Seruni22Bakery.Host.dll %s (%s bita, dari manifes %s)'
          % (ver_baru, h.upper(), ukuran, os.path.basename(manifes)))
    return h.upper()


if os.path.exists(TUJUAN):
    sys.exit('GAGAL: %s sudah ada; jangan menimpa kit lama.' % TUJUAN)

lama_f = fakta_paket(LAMA)
baru_f = fakta_paket(BARU)
dll_baru = host_dll(BARU)

# --- 1. konstanta harness -> token antara ---
path_uji = os.path.join(SUMBER, 'uji-129-1213.ps1')
teks = open(path_uji, encoding='utf-8').read()
token = []


def ke_token(lama, baru, jumlah, nama):
    global teks
    t = '\x01T%d\x01' % len(token)
    teks = ganti(teks, lama, t, jumlah, nama)
    token.append((t, baru))


# sisi baru harness currently memegang angka 1.2.13 -> jadi angka 1.2.14
ke_token("'%s'" % lama_f['sha_msi'], "'%s'" % baru_f['sha_msi'], 1, 'SHA MSI sisi baru')
ke_token("'%s'" % lama_f['sha_exe'], "'%s'" % baru_f['sha_exe'], 1, 'SHA EXE sisi baru')
ke_token('= %d' % lama_f['ukuran_msi'], '= %d' % baru_f['ukuran_msi'], 1, 'ukuran MSI sisi baru')
ke_token('= %d' % lama_f['ukuran_exe'], '= %d' % baru_f['ukuran_exe'], 1, 'ukuran EXE sisi baru')
ke_token("'%s'" % lama_f['kode'], "'%s'" % baru_f['kode'], 1, 'ProductCode sisi baru')
ke_token("'%s'" % lama_f['js'], "'%s'" % baru_f['js'], 1, 'bundel JS sisi baru')
ke_token("'%s'" % lama_f['css'], "'%s'" % baru_f['css'], 1, 'bundel CSS sisi baru')
ke_token("'D4D1BF68C31C3F66C13B9605B5588A914D0D126655DEFE7F4380DA173FA2D672'",
         "'%s'" % dll_baru, 1, 'hash Host.dll')

# sisi lama harness memegang angka 1.2.9 -> jadi angka 1.2.13
ke_token("'%s'" % SHA_MSI_129, "'%s'" % lama_f['sha_msi'], 1, 'SHA MSI sisi lama')
ke_token("'%s'" % SHA_EXE_129, "'%s'" % lama_f['sha_exe'], 1, 'SHA EXE sisi lama')
ke_token('= %d' % UKURAN_MSI_129, '= %d' % lama_f['ukuran_msi'], 1, 'ukuran MSI sisi lama')
ke_token('= %d' % UKURAN_EXE_129, '= %d' % lama_f['ukuran_exe'], 1, 'ukuran EXE sisi lama')
ke_token("'%s'" % KODE_129, "'%s'" % lama_f['kode'], 1, 'ProductCode sisi lama')
ke_token("'%s'" % BUNDEL_JS_129, "'%s'" % lama_f['js'], 1, 'bundel JS sisi lama')
ke_token("'%s'" % BUNDEL_CSS_129, "'%s'" % lama_f['css'], 1, 'bundel CSS sisi lama')

# --- 2. rename label versi dua arah ---
# Jumlah label dibaca dulu supaya hasil rename bisa diverifikasi persis, bukan ditebak.
hitung_asal = {k: teks.count(k) for k in (LAMA, BARU, '1.2.9', LAMA_NUM, BARU_NUM, '129')}
print('HITUNG sumber  %r' % (hitung_asal,))
teks = teks.replace(LAMA, '\x02V\x02').replace(LAMA_NUM, '\x02N\x02')
teks = teks.replace('1.2.9', LAMA)
teks = teks.replace('129', LAMA_NUM)
teks = teks.replace('\x02V\x02', BARU).replace('\x02N\x02', BARU_NUM)
print('LABEL  1.2.9 -> %s, 129 -> %s, %s -> %s, %s -> %s'
      % (LAMA, LAMA_NUM, LAMA, BARU, LAMA_NUM, BARU_NUM))

# --- 3. penjaga: tidak boleh ada sisa penanda 1.2.9 (konstanta masih token) ---
# '1.2.13' dan '1213' TIDAK dicekat: sisi lama hasil port memang menjadi 1.2.13.
for sisa in ['1.2.9', '129', '\x02']:
    if sisa in teks:
        sys.exit('GAGAL: sisa penanda %r masih ada setelah rename label' % sisa)

# Rename harus memindahkan label 1.2.9/129 ke sisi lama dan label 1.2.13/1213 ke sisi baru
# tanpa menambah atau mengurangi satu occurrence pun. Diperiksa di teks bertoken, sebelum
# konstanta diisi, supaya digit di dalam hash/ukuran tidak ikut dihitung sebagai label.
for label, sumber_label in ((LAMA, '1.2.9'), (BARU, LAMA), (BARU_NUM, LAMA_NUM)):
    harap = hitung_asal[sumber_label]
    nyata = teks.count(label)
    if nyata != harap:
        sys.exit('GAGAL: label %r muncul %d kali, seharusnya %d (sumber %r)'
                 % (label, nyata, harap, sumber_label))
print('OK identitas label  %s=%d  %s=%d  %s=%d'
      % (LAMA, teks.count(LAMA), BARU, teks.count(BARU), BARU_NUM, teks.count(BARU_NUM)))

# --- 4. token diisi nilai akhir ---
for t, nilai in token:
    if teks.count(t) != 1:
        sys.exit('GAGAL: token %r muncul %d kali' % (t, teks.count(t)))
    teks = teks.replace(t, nilai)
for basi in [SHA_MSI_129, SHA_EXE_129, KODE_129, BUNDEL_JS_129, BUNDEL_CSS_129,
             str(UKURAN_MSI_129), str(UKURAN_EXE_129)]:
    if basi in teks:
        sys.exit('GAGAL: konstanta 1.2.9 %r masih tertinggal' % basi)

os.makedirs(TUJUAN)
nama_uji = 'uji-%s-%s.ps1' % (LAMA_NUM, BARU_NUM)
with open(os.path.join(TUJUAN, nama_uji), 'w', encoding='utf-8', newline='\n') as f:
    f.write(teks)
print('OK skrip tamu %s %d bita' % (nama_uji, len(teks.encode('utf-8'))))

# --- 5. peluncur: rename yang sama, tanpa konstanta ---
luncur = open(os.path.join(SUMBER, 'run-sandbox.ps1'), encoding='utf-8').read()
luncur = luncur.replace(LAMA, '\x02V\x02').replace(LAMA_NUM, '\x02N\x02')
luncur = luncur.replace('1.2.9', LAMA).replace('129', LAMA_NUM)
luncur = luncur.replace('\x02V\x02', BARU).replace('\x02N\x02', BARU_NUM)
for sisa in ['1.2.9', '129', '\x02']:
    if sisa in luncur:
        sys.exit('GAGAL: peluncur masih memuat %r' % sisa)
with open(os.path.join(TUJUAN, 'run-sandbox.ps1'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(luncur)
print('OK peluncur %d bita' % len(luncur.encode('utf-8')))

# --- 6. manifes payload versi baru (format name|HASH|ukuran seperti kit lama) ---
manifes_baru = os.path.join(REPO, 'dist', 'publish-single.manifest.txt')
baris = []
for b in open(manifes_baru, encoding='utf-8').read().splitlines():
    if not b.strip():
        continue
    h, ukuran, nama = b.split(None, 2)
    baris.append('%s|%s|%s' % (nama, h.upper(), ukuran))
if len(baris) != 393:
    sys.exit('GAGAL: manifes punya %d baris, seharusnya 393' % len(baris))
with open(os.path.join(TUJUAN, 'hash-publish-%s.txt' % BARU_NUM), 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(baris) + '\n')
print('OK hash-publish-%s.txt 393 baris' % BARU_NUM)

# --- 7. salin paket yang diuji ---
for f in (lama_f, baru_f):
    for kind in ('msi', 'exe'):
        shutil.copy2(os.path.join(REPO, 'dist', 'installer', f[kind]), os.path.join(TUJUAN, f[kind]))
        print('OK salin %s' % f[kind])

# --- 8. KIT-SHA256SUMS (PANDUAN-KIT.md ditulis setelah langkah ini) ---
daftar = [lama_f['msi'], baru_f['msi'], lama_f['exe'], baru_f['exe'],
          'hash-publish-%s.txt' % BARU_NUM, nama_uji, 'run-sandbox.ps1']
sumbaris = ['%s *%s' % (sha256(os.path.join(TUJUAN, n)).lower(), n) for n in daftar]
with open(os.path.join(TUJUAN, 'KIT-SHA256SUMS-sebelum-panduan.txt'), 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(sumbaris) + '\n')
for b in sumbaris:
    print('SUM ' + b)

print('SELESAI port %s -> %s. Langkah berikutnya: tulis PANDUAN-KIT.md lalu '
      'run-sandbox.ps1 -Periksa, baru jalankan uji.' % (LAMA, BARU))
