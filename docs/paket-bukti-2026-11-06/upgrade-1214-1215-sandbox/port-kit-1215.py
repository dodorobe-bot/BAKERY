# Memport kit uji sandbox 1.2.13 -> 1.2.14 menjadi 1.2.14 -> 1.2.15.
#
# Sisi baru (1.2.15) dan sisi lama (1.2.14) tidak diketik dari ingatan: SHA-256 dan
# ukuran paket dibaca dari disk, ProductCode dan nama bundel dibaca dari tabel
# Property/File kedua MSI, dan hash Seruni22Bakery.Host.dll dibaca dari manifes
# publish hasil build-pro. Setiap penggantian konstanta diverifikasi jumlahnya.
#
# Skema berubah pada rilis ini: basis data 1.2.14 ada di skema 18, sedangkan 1.2.15
# membawa migrasi 019-023 (FIFO, diskon/PPN/piutang, retur pemasok, pagar periode,
# satuan bahan). Peningkatan karena itu WAJIB menjalankan lima migrasi dan berakhir
# di skema 23, bukan nol migrasi seperti port sebelumnya.
#
# Urutan memakai token antara supaya digit di dalam hash/GUID tidak ikut tersapu
# rename label: (1) konstanta -> token, (2) rename label versi dua arah,
# (3) penyesuaian skema, (4) penjaga anti-sisa, (5) token diisi nilai akhir.
import hashlib
import os
import re
import shutil
import sys

REPO = r'D:\bakery-desktop'
SUMBER = os.path.join(REPO, 'docs', 'paket-bukti-2026-11-05', 'upgrade-1213-1214-sandbox')
TUJUAN = r'C:\tmp\kit-uji-1214-1215-v3'

LAMA = '1.2.14'
BARU = '1.2.15'
LAMA_NUM = '1214'
BARU_NUM = '1215'


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for blok in iter(lambda: f.read(1024 * 1024), b''):
            h.update(blok)
    return h.hexdigest().upper()


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


def fakta_paket(ver):
    msi = os.path.join(REPO, 'dist', 'installer', 'Seruni22Bakery_v%s_x64.msi' % ver)
    exe = os.path.join(REPO, 'dist', 'installer', 'Seruni22Bakery_Setup_v%s.exe' % ver)
    for p in (msi, exe):
        if not os.path.exists(p):
            sys.exit('GAGAL: paket tidak ada: %s' % p)
    kode = baca_msi(msi, "SELECT `Value` FROM `Property` WHERE `Property` = 'ProductCode'")
    if len(kode) != 1:
        sys.exit('GAGAL: ProductCode %s = %r' % (msi, kode))
    nama = []
    for mentah in baca_msi(msi, "SELECT `FileName` FROM `File`"):
        panjang = mentah.split('|')[-1]
        if re.match(r'^index-[A-Za-z0-9]+\.(js|css)$', panjang):
            nama.append(panjang)
    js = sorted(set(n for n in nama if n.endswith('.js')))
    css = sorted(set(n for n in nama if n.endswith('.css')))
    if len(js) != 1 or len(css) != 1:
        sys.exit('GAGAL: bundel asset %s = js %r css %r' % (msi, js, css))
    f = {
        'msi': os.path.basename(msi), 'sha_msi': sha256(msi), 'ukuran_msi': os.path.getsize(msi),
        'exe': os.path.basename(exe), 'sha_exe': sha256(exe), 'ukuran_exe': os.path.getsize(exe),
        'kode': kode[0], 'js': js[0], 'css': css[0],
    }
    print('FAKTA %s  %s %d bita %s' % (ver, f['msi'], f['ukuran_msi'], f['sha_msi'].lower()))
    print('        %s %d bita %s' % (f['exe'], f['ukuran_exe'], f['sha_exe'].lower()))
    print('        ProductCode %s  bundel %s + %s' % (f['kode'], f['js'], f['css']))
    return f


def host_dll(ver):
    manifes = os.path.join(REPO, 'dist', 'publish-single.manifest.txt')
    dengan = [b for b in open(manifes, encoding='utf-8').read().splitlines()
              if b.endswith(' Seruni22Bakery.Host.dll')]
    if len(dengan) != 1:
        sys.exit('GAGAL: baris Seruni22Bakery.Host.dll di manifes publish = %d' % len(dengan))
    h, ukuran, nama = dengan[0].split(None, 2)
    print('FAKTA %s  Seruni22Bakery.Host.dll %s (%s bita)' % (ver, h.upper(), ukuran))
    return h.upper()


if os.path.exists(TUJUAN):
    sys.exit('GAGAL: %s sudah ada; jangan menimpa kit lama.' % TUJUAN)

lama_f = fakta_paket(LAMA)
baru_f = fakta_paket(BARU)
dll_baru = host_dll(BARU)

teks = open(os.path.join(SUMBER, 'uji-1213-1214.ps1'), encoding='utf-8').read()
token = []


def konstanta(nama_var, nilai, nama):
    """Ganti satu baris assignment konstanta dengan token, tanpa menyentuh nama mirip."""
    global teks
    pola = re.compile(r'^(\$%s\s*=\s*)(\'[^\']*\'|\d+)[ \t]*$' % re.escape(nama_var), re.M)
    t = '\x01T%d\x01' % len(token)
    teks, n = pola.subn(lambda m: m.group(1) + t, teks)
    if n != 1:
        sys.exit('GAGAL: konstanta %s cocok %d baris (diharapkan 1)' % (nama, n))
    token.append((t, nilai))


# Sisi baru harness memegang angka 1.2.14 -> sekarang menjadi 1.2.15.
konstanta('shaMsi1214', "'%s'" % baru_f['sha_msi'], 'SHA MSI sisi baru')
konstanta('shaExe1214', "'%s'" % baru_f['sha_exe'], 'SHA EXE sisi baru')
konstanta('ukuranMsi1214', str(baru_f['ukuran_msi']), 'ukuran MSI sisi baru')
konstanta('ukuranExe1214', str(baru_f['ukuran_exe']), 'ukuran EXE sisi baru')
konstanta('kode1214', "'%s'" % baru_f['kode'], 'ProductCode sisi baru')
konstanta('bundelJs', "'%s'" % baru_f['js'], 'bundel JS sisi baru')
konstanta('bundelCss', "'%s'" % baru_f['css'], 'bundel CSS sisi baru')

# Sisi lama harness memegang angka 1.2.13 -> sekarang menjadi 1.2.14.
konstanta('shaMsi1213', "'%s'" % lama_f['sha_msi'], 'SHA MSI sisi lama')
konstanta('shaExe1213', "'%s'" % lama_f['sha_exe'], 'SHA EXE sisi lama')
konstanta('ukuranMsi1213', str(lama_f['ukuran_msi']), 'ukuran MSI sisi lama')
konstanta('ukuranExe1213', str(lama_f['ukuran_exe']), 'ukuran EXE sisi lama')
konstanta('kode1213', "'%s'" % lama_f['kode'], 'ProductCode sisi lama')
konstanta('bundelJsLama', "'%s'" % lama_f['js'], 'bundel JS sisi lama')
konstanta('bundelCssLama', "'%s'" % lama_f['css'], 'bundel CSS sisi lama')

konstanta('hashHostDll', "'%s'" % dll_baru, 'hash Host.dll sisi baru')

# --- rename label versi dua arah ---
hitung_asal = {k: teks.count(k) for k in (LAMA, BARU, '1.2.13', LAMA_NUM, '1213')}
print('HITUNG sumber  %r' % (hitung_asal,))
teks = teks.replace(BARU, '\x02V\x02').replace(LAMA_NUM, '\x02N\x02')
teks = teks.replace(LAMA, BARU).replace('1.2.13', LAMA)
teks = teks.replace('1213', LAMA_NUM)
teks = teks.replace('\x02V\x02', BARU).replace('\x02N\x02', BARU_NUM)
print('LABEL  1.2.13 -> %s, 1213 -> %s, %s -> %s, %s -> %s'
      % (LAMA, LAMA_NUM, LAMA, BARU, LAMA_NUM, BARU_NUM))

for label, sumber_label in ((LAMA, '1.2.13'), (BARU, LAMA), (BARU_NUM, LAMA_NUM)):
    harap = hitung_asal[sumber_label]
    nyata = teks.count(label)
    if nyata != harap:
        sys.exit('GAGAL: label %r muncul %d kali, seharusnya %d (sumber %r)'
                 % (label, nyata, harap, sumber_label))
print('OK identitas label  %s=%d  %s=%d  %s=%d'
      % (LAMA, teks.count(LAMA), BARU, teks.count(BARU), BARU_NUM, teks.count(BARU_NUM)))

# --- penyesuaian skema: 18 -> 23, nol migrasi -> lima migrasi ---
def tetap(lama, baru, jumlah, nama):
    global teks
    n = teks.count(lama)
    if n != jumlah:
        sys.exit('GAGAL: penyesuaian %s menemukan %d occurrence (%d diharapkan): %r'
                 % (nama, n, jumlah, lama[:80]))
    teks = teks.replace(lama, baru)


# Pola regex ber-escape ('1\.2\.13') tidak tersentuh rename teks biasa.
teks = teks.replace('1\\.2\\.15', '\x02E\x02')
teks = teks.replace('1\\.2\\.14', '\x02F\x02')
teks = teks.replace('1\\.2\\.13', '1\\.2\\.14')
teks = teks.replace('\x02F\x02', '1\\.2\\.15')
teks = teks.replace('\x02E\x02', '1\\.2\\.15')
if '1\\.2\\.13' in teks:
    sys.exit('GAGAL: pola regex 1\\.2\\.13 masih tertinggal')

# Sandbox 1,5 GB kadang butuh belasan detik sebelum Start-Job membuka listener;
# batas 10 detik membuat pemeriksaan kesiapan merah padahal byte cetak tetap tiba.
tetap("for ($i = 0; $i -lt 20; $i++) {\n  Start-Sleep -Milliseconds 500\n  if (Test-Path $siapCetak) { $emulatorSiap = $true; break }\n}\nCek 'emulator ESC/POS loopback siap di port 9100' $emulatorSiap 'listener aktif' 'listener tidak siap dalam 10 detik'",
      "for ($i = 0; $i -lt 120; $i++) {\n  Start-Sleep -Milliseconds 500\n  if (Test-Path $siapCetak) { $emulatorSiap = $true; break }\n}\nCek 'emulator ESC/POS loopback siap di port 9100' $emulatorSiap 'listener aktif' 'listener tidak siap dalam 60 detik'",
      1, 'batas tunggu emulator')

tetap('skema 18 diperiksa', 'skema 23 diperiksa (lima migrasi baru 019-023)', 1, 'komentar tujuan uji')
tetap("\"$label skema terpasang 18\" ($versiSkema -match 'terpasang 18' -and $versiSkema -match 'terkemas 18')",
      "\"$label skema terpasang 23\" ($versiSkema -match 'terpasang 23' -and $versiSkema -match 'terkemas 23')",
      1, 'pemeriksaan skema periksa-diri')
tetap("Cek 'tidak ada migrasi baru di atas skema 18' ($migrasiBaru -eq 0) \"migrasi dijalankan x$migrasiBaru (diharapkan 0)\" \"migrasi dijalankan x$migrasiBaru, seharusnya 0\"",
      "Cek 'lima migrasi baru 019-023 diterapkan di atas skema 18' ($migrasiBaru -eq 5) \"migrasi dijalankan x$migrasiBaru (diharapkan 5)\" \"migrasi dijalankan x$migrasiBaru, seharusnya 5\"",
      1, 'pemeriksaan migrasi peningkatan')
tetap("Cek 'basis data EXE baru menjalankan 18 migrasi' ($migrasiExe -eq 18) \"18 migrasi\" \"migrasi=$migrasiExe\"",
      "Cek 'basis data EXE baru menjalankan 23 migrasi' ($migrasiExe -eq 23) \"23 migrasi\" \"migrasi=$migrasiExe\"",
      1, 'pemeriksaan migrasi basis data baru')

# Fitur hasil migrasi 022 harus hidup, bukan sekadar tabelnya ada.
tetap("Cek-BasisData $base2 $token3 'peningkatan' 5312\n",
      "Cek-BasisData $base2 $token3 'peningkatan' 5312\n"
      "$periode = Api 'GET' \"$base2/api/periods\" $token3 $null\n"
      "$jumlahPeriode = 0\n"
      "if ($null -ne $periode) { $jumlahPeriode = @($periode).Count }\n"
      "Cek 'endpoint periode buku hidup sesudah migrasi 022' ($null -ne $periode) (\"daftar periode: $jumlahPeriode\") 'GET /api/periods tidak menjawab'\n",
      1, 'pemeriksaan endpoint periode')

# --- penjaga: tidak boleh ada sisa penanda versi lama (konstanta masih token) ---
for sisa in ['1.2.13', '1213', '\x02']:
    if sisa in teks:
        sys.exit('GAGAL: sisa penanda %r masih ada setelah rename label' % sisa)

# --- token diisi nilai akhir ---
for t, nilai in token:
    if teks.count(t) != 1:
        sys.exit('GAGAL: token %r muncul %d kali' % (t, teks.count(t)))
    teks = teks.replace(t, nilai)

os.makedirs(TUJUAN)
nama_uji = 'uji-%s-%s.ps1' % (LAMA_NUM, BARU_NUM)
with open(os.path.join(TUJUAN, nama_uji), 'w', encoding='utf-8', newline='\n') as f:
    f.write(teks)
print('OK skrip tamu %s %d bita' % (nama_uji, len(teks.encode('utf-8'))))

# --- peluncur: rename yang sama, tanpa konstanta ---
luncur = open(os.path.join(SUMBER, 'run-sandbox.ps1'), encoding='utf-8').read()
luncur = luncur.replace(BARU, '\x02V\x02').replace(LAMA_NUM, '\x02N\x02')
luncur = luncur.replace(LAMA, BARU).replace('1.2.13', LAMA)
luncur = luncur.replace('1213', LAMA_NUM)
luncur = luncur.replace('\x02V\x02', BARU).replace('\x02N\x02', BARU_NUM)
for sisa in ['1.2.13', '1213', '\x02']:
    if sisa in luncur:
        sys.exit('GAGAL: peluncur masih memuat %r' % sisa)
with open(os.path.join(TUJUAN, 'run-sandbox.ps1'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(luncur)
print('OK peluncur %d bita' % len(luncur.encode('utf-8')))

# --- manifes payload versi baru (format name|HASH|ukuran) ---
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

# --- salin paket yang diuji ---
for f in (lama_f, baru_f):
    for kind in ('msi', 'exe'):
        shutil.copy2(os.path.join(REPO, 'dist', 'installer', f[kind]), os.path.join(TUJUAN, f[kind]))
        print('OK salin %s' % f[kind])

# --- KIT-SHA256SUMS (sebelum PANDUAN-KIT.md) ---
daftar = [lama_f['msi'], baru_f['msi'], lama_f['exe'], baru_f['exe'],
          'hash-publish-%s.txt' % BARU_NUM, nama_uji, 'run-sandbox.ps1']
sumbaris = ['%s *%s' % (sha256(os.path.join(TUJUAN, n)).lower(), n) for n in daftar]
with open(os.path.join(TUJUAN, 'KIT-SHA256SUMS-sebelum-panduan.txt'), 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(sumbaris) + '\n')
for b in sumbaris:
    print('SUM ' + b)

print('SELESAI port %s -> %s. Langkah berikutnya: tulis PANDUAN-KIT.md, KIT-SHA256SUMS.txt, '
      'lalu run-sandbox.ps1 -Periksa.' % (LAMA, BARU))
