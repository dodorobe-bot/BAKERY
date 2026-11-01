# Uji Fisik Perangkat Kasir (Printer 80 mm → Pemindai → Laci Uang)

Panduan ini dipakai **di PC kasir dengan perangkat nyata**. Pada mesin pengembangan
(Windows 11 Pro 10.0.26300, 2026-10-28) hanya ada dua printer virtual — tidak ada printer thermal,
pemindai, COM port, atau laci uang — sehingga seluruh uji di bawah **belum dijalankan** dan
tidak boleh ditandai PASS. Yang sudah berjalan di mesin pengembangan hanyalah alat uji ini
ditembakkan ke pengganti printer berupa emulator loopback (`127.0.0.1`): itu membuktikan skrip,
jalur ESC/POS, dan pesan gagal bekerja, **bukan** bahwa ada kertas keluar.

Aturan utama untuk jalur **driver Windows**: status "Printed" hanya berarti spooler Windows
menerima pekerjaan, bukan kertas keluar. Windows tetap mengantrekan pekerjaan saat printer mati.
Lulus hanya bila kertasnya dilihat langsung.

Aturan utama untuk jalur **ESC/POS langsung** (`-CetakMode escpos-raw`): tidak ada antrean Windows
sama sekali. "Tercetak" berarti byte diterima port printer, dan "Gagal" berarti sambungan atau
penulisan ke printer gagal. Kedua hal itu tetap **bukan bukti kertas keluar** — kertas hanya bisa
dibuktikan dengan melihatnya.

## Status 2026-11-01 (paket 1.2.12)

Seluruh tahap A–E di bawah **masih belum dijalankan** pada perangkat nyata. Yang berubah pada
1.2.12 dan hasil percobaan di mesin pengembangan:

- **Pemindai barcode:** simulasi wedge lulus 14/14 pada aplikasi 1.2.12 yang dikemas — ketik per
  karakter + Enter, 10 pindai cepat tanpa karakter hilang, kode asing ditolak, tanpa Enter tidak
  memilih produk, fokus di luar kolom tidak mengisi kolom. Itu **simulasi**, bukan pemindai fisik;
  tahap C tetap wajib dilakukan di PC kasir.
- **Cetak driver Windows:** percobaan ke `Microsoft Print to PDF` gagal — `POST /api/devices/test-print`
  tidak menjawab dalam 40 detik, antrean 0, tidak ada PDF baru. Port `PORTPROMPT:` menuntut dialog
  simpan, sehingga cetak senyap memblokir. Ini batas mesin uji, **bukan** lulus dan **bukan** bukti
  printer thermal gagal. Jangan memakai pencetak virtual sebagai pengganti tahap A.
- **ESC/POS:** byte sudah terbukti di emulator loopback dan pada biner hasil peningkatan di Windows
  Sandbox (635 bita, `ESC @` di awal, `ESC d 3` di akhir). Printer fisik tetap belum diuji.
- **Laci uang:** tetap belum diimplementasikan. Jaminan negatif diperiksa pada 635 bita struk
  tersebut: tidak ada `ESC p`, `ESC a`, `ESC E`, `ESC t`, `GS V`, maupun `GS k`.
- **Authenticode:** 0 sertifikat code signing, kedua paket `NotSigned`, `signtool verify /pa`
  menolak. Paket tidak boleh disebut bertanda tangan.

Bukti dan panduan pemilik: `docs\paket-bukti-2026-11-01\uji-fisik-1212\`.

---

## A. Prasyarat

1. Printer thermal 80 mm. Untuk uji jalur driver: terpasang dengan **driver resmi dari pabrik**
   (bukan driver generik "ESC/POS" yang tidak dikenal). Untuk uji jalur ESC/POS langsung: driver
   Windows **tidak diperlukan**, tetapi printer harus punya alamat IP di jaringan toko dan port
   cetak (umumnya 9100) terbuka; kabel jaringan/Wi-Fi printer sudah tersambung. Catat: merek,
   model, versi driver, port (USB/COM/IP), versi Windows.
2. Kertas 80 mm terpasang, penutup printer tertutup, indikator normal.
3. Aplikasi SERUNI 22 BAKERY build Batch-8 terpasang, atau host dijalankan dari folder repo.
4. Pemindai barcode USB (tahap B) belum perlu dipasang pada tahap A.
5. Siapkan HP/kamera untuk memfoto struk sebagai bukti.
6. PC kasir dan (untuk ESC/POS) printer berada di jaringan yang sama; skrip uji mencatat hasilnya
   ke folder bukti sementara, bukan ke basis data usaha.

Isi tabel ini sebelum mulai (tanpa data ini hasilnya tidak bisa dibandingkan dengan model lain):

| Butir | Isi |
|---|---|
| Merek & model printer | |
| Driver + versi | |
| Port | |
| Alamat IP printer (untuk ESC/POS) | |
| Port TCP (umumnya 9100) | |
| Versi Windows | |
| Lebar kertas | 80 mm |
| Pemindai (merek/model) | |
| Mode cetak yang diuji | Driver Windows / ESC/POS langsung |

## B. Tahap A — Printer 80 mm

Jalankan **setiap langkah di bawah dua kali**, sekali per jalur cetak, karena yang diuji justru
bahwa pergantian jalur tidak mengubah transaksi:

- `windows-driver` — struk dikirim lewat driver printer di Windows (layar **Perangkat Kasir →
  Cara mencetak → Driver Windows**).
- `escpos-raw` — byte ESC/POS dikirim langsung ke IP printer, tanpa driver dan tanpa antrean
  Windows (**Perangkat Kasir → Cara mencetak → ESC/POS langsung**, isi **Alamat printer**,
  **Port**, centang **Potong kertas setelah struk selesai** bila printer punya pemotong, lalu
  **Simpan Pengaturan**).

Di aplikasi, kedua jalur dipilih dari layar; di skrip uji, dipilih dengan `-CetakMode`.

### A1. Rekam lingkungan dan alur (tanpa mencetak)

Jalur driver:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -CetakMode windows-driver -PrinterName "NAMA PRINTER DI WINDOWS" -PaperWidthMm 80 -SkipPrint
```

Jalur ESC/POS langsung:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -CetakMode escpos-raw -EscPosHost 192.168.1.50 -EscPosPort 9100 -EscPosPotong -SkipPrint
```

Yang dibuktikan: host uji memakai basis data sementara (data usaha tidak disentuh), login Owner,
pengaturan perangkat tersimpan, pesanan uji dibuat tepat satu. Pada jalur driver turut tercatat
driver/port/status Windows dan ukuran kertas dari driver; pada jalur ESC/POS tercatat tujuan TCP
dan apakah port terbuka sebelum mencetak, dengan catatan "Driver printer: tidak dipakai". Tidak ada
kertas yang keluar pada langkah ini.

Perbedaan yang disengaja: `-EscPosHost` wajib diisi saat `-CetakMode escpos-raw`, dan `-PrinterName`
wajib saat `windows-driver`; skrip menolak berjalan dengan keluaran `GAGAL:` kalau salah satu
kosong.

### A2. Cetak struk uji (POS-01)

Ganti `-SkipPrint` menjadi `-AllowPrint` pada dua perintah A1 di atas. Skrip akan menekan
**Cetak Struk Uji** lewat API lalu mencetak struk pesanan uji.

Lulus bila **semua** terpenuhi (kedua jalur):

- Kertas keluar berisi "UJI CETAK PERANGKAT", nama printer, "80 mm / 48 kolom", contoh
  `2 x Rp 15.000`, dan penutup "SELESAI".
- Tidak ada baris terpotong di kanan; huruf tidak berhimpit.
- Pekerjaan cetak di layar Perangkat Kasir berstatus **Tercetak** (bukan "Menunggu").
- Tidak muncul dialog printer di layar (cetak senyap).

Tambahan khusus jalur ESC/POS:

- Pada jalur driver, nama tujuan di riwayat adalah nama printer Windows; pada ESC/POS yang tampil
  adalah `tcp://ALAMAT:PORT`. Itu memang isi kolom **Alamat printer** dan **Port**, bukan galat.
- Bila port tertutup, struk **tidak** masuk antrean Windows apa pun: riwayat langsung berwarna
  **Gagal** dengan pesan bahasa toko, misalnya
  `Printer 192.168.1.50:9100 menolak sambungan. Pastikan printer menyala dan port 9100 benar.`
- Laci uang tidak ikut terbuka: aplikasi ini belum mengendalikan laci uang sama sekali.

Simpan: berkas `bukti.txt` dari folder yang dicetak skrip + foto struk uji.

### A3. Struk pesanan dengan rupiah, jumlah, dan cetak ulang (POS-02)

Masih dengan perintah A2 (skrip juga mencetak struk pesanan uji):

- Struk memuat nomor nota, pelanggan, deskripsi, `2 x Rp 12.500`, `Total Rp 25.000`,
  `DP / Uang Muka Rp 5.000`, `Sisa Tagihan Rp 20.000`.
- Struk kedua tidak mengubah pesanan: total dan status sebelum/sesudah sama (dicetak skrip).
- Dari layar **Penjualan**, tekan cetak ulang pada pesanan yang sama: struk keluar dengan
  penanda `*** CETAK ULANG ***`, dan jumlah pesanan tidak bertambah.

### A4. Kertas habis (POS-03)

1. Keluarkan kertas (atau aktifkan sensor kertas habis) tanpa mematikan printer.
2. Ulangi A2 dengan `-Mode Failure -AllowPrint`.
3. Catat apa adanya: status pekerjaan (`Tercetak`/`Gagal`), pesan di layar, dan status antrean
   di **Settings → Printers & scanners → Open print queue** (hanya berlaku jalur driver —
   ESC/POS langsung tidak punya antrean Windows).
4. Pasang kembali kertas, lalu cetak ulang dari layar Penjualan.

Lulus bila: tidak ada pesanan ganda, pesanan tetap ada dengan total yang sama, dan setelah
kertas dipasang struk baru bisa dicetak. **Jangan lulus** hanya karena server menjawab 200 —
periksa apakah kertas benar-benar keluar atau pekerjaan hanya mengendap di antrean.

Perbedaan jalur yang wajib dicatat pada langkah 3: pada jalur driver, sensor kertas habis sering
tidak terlihat Windows sehingga pekerjaan tetap dilaporkan `Tercetak`; pada jalur ESC/POS, printer
biasanya tetap menerima byte (kertas tidak keluar, tidak ada galat) — jadi kedua jalur bisa
melaporkan "sukses" tanpa kertas. Hanya pemeriksaan mata yang membedakan.

### A5. Printer dimatikan (POS-04)

1. Matikan printer (kabel listrik/USB dicabut sesuai prosedur model).
2. Ulangi A2 `-Mode Failure -AllowPrint`.
3. Perhatikan: Windows umumnya **menerima** pekerjaan dan menahannya di antrean, sehingga
   aplikasi bisa melaporkan "Tercetak". Ini keterbatasan yang harus dicatat, bukan bug yang
   disembunyikan.
4. Nyalakan printer. Bila pekerjaan lama ikut tercetak, catat; pastikan tidak ada pesanan kedua.
5. Cetak ulang manual; pastikan struk keluar dan transaksi tidak berganda.

Jalur ESC/POS berperilaku berbeda dan lebih mudah dibaca: printer mati berarti port tertutup, jadi
sambungan ditolak, pekerjaan tercatat `Gagal`, struk tercatat **satu** kali di riwayat, dan pesanan
tetap utuh. Skrip mencetak kolom `galat=` berisi pesan bahasa toko, dan pesan itu juga yang tersimpan
permanen di riwayat pekerjaan cetak. Yang masih harus dilihat di perangkat nyata: apakah printer
menyimpan byte yang sudah terlanjur masuk buffer sebelum mati (sebagian model mencetaknya saat
dinyalakan kembali).

### A6. Pemotong kertas (POS-05)

Amati apakah kertas terpotong otomatis dan rapi. Beda perlakuan per jalur:

- `escpos-raw`: aplikasi mengirim perintah potong (`GS V 0`) **hanya bila** centang **Potong kertas
  setelah struk selesai** diaktifkan. Uji dua-duanya: tercetak rapi saat dicentang, dan tidak ada
  perintah potong saat tidak dicentang. Sebagian model memakai perintah potong yang berbeda, jadi
  "tidak memotong" pada satu merek bukan berarti jalur ESC/POS gagal secara umum — catat per model.
- `windows-driver`: aplikasi tidak mengirim perintah potong sendiri; potongan mengikuti driver dan
  pengaturan printer.

Bila model tidak punya pemotong, tulis "tidak mendukung".

### A7. Ganti printer tanpa ubah logika (POS-06)

Di layar **Perangkat Kasir**, pilih printer kedua (bila ada) atau berpindah jalur cetak
(**Driver Windows** ↔ **ESC/POS langsung**), simpan, cetak uji. Lulus bila transaksi dan struk
tetap sama isinya; yang berubah hanya perangkat tujuan. Catatan: struk yang gagal dicetak bisa
dicetak ulang dari layar Penjualan ke tujuan baru tanpa membuat pesanan kedua.

## C. Tahap B — Pemindai barcode USB

Dilakukan hanya setelah tahap A lulus.

1. Pastikan pemindai dikenali Windows sebagai keyboard (mode HID keyboard wedge) dan
   **diprogram mengirim Enter** di akhir kode.
2. Buka **Daftar Pesanan & Penjualan → Tambah Transaksi**.
3. Fokuskan kolom "Pindai Barcode Produk", pindai produk yang barcode-nya sudah diisi di katalog.
   Lulus bila produk terpilih dan total terisi dari harga katalog (bukan diketik kasir).
4. Pindai kode yang tidak terdaftar: harus muncul pesan "tidak cocok dengan produk mana pun",
   bukan memilih produk sembarang.
5. Pindai berulang cepat 10×: tidak ada karakter hilang, tidak ada produk salah.
6. Uji tanpa akhiran Enter (bila model bisa diubah): catat bahwa alur tidak berjalan; kembalikan
   ke Enter. Aplikasi saat ini hanya mendukung akhiran `enter`.
7. Uji fokus: klik di luar kolom pindai, lalu pindai. Catat bahwa hasil masuk ke kolom yang
   sedang aktif — ini perilaku keyboard wedge, bukan pemindai yang "hilang".

## D. Tahap C — Laci uang (belum boleh diuji sebagai fitur)

`ICashDrawerAdapter` masih **antarmuka tanpa implementasi** dan tidak punya endpoint. Jangan
menulis "laci uang didukung" sebelum:

1. Tahap A dan B lulus pada perangkat nyata.
2. Model printer diketahui punya port DK (RJ-11/RJ-12) dan perintah buka laci yang terdokumentasi.
3. Aturan usaha disepakati: laci hanya terbuka setelah pembayaran tunai yang sah, tidak pernah
   saat halaman pembayaran dibuka.

Khusus jalur ESC/POS, yang belum dikirim sampai hari ini adalah perintah pemantik laci (`ESC p`);
tes otomatis justru menjaga bahwa byte itu **tidak pernah** keluar. Jadi laci uang pada printer apa
pun — termasuk yang tersambung via ESC/POS — tetap terbuka manual atau lewat sakelar printer, bukan
oleh aplikasi ini.

## E. Uji rendam (setelah A dan B lulus)

- Jalankan pemakaian berulang 2–4 jam: cetak struk, cetak ulang, matikan/nyalakan printer,
  keluarkan kertas, biarkan antrean menumpuk.
- Catat: pertumbuhan memori host, pekerjaan `Menunggu` yang menggantung, kegagalan berulang,
  dan apakah ada pesanan yang hilang atau berganda.
- Aplikasi menandai pekerjaan `Pending` yang lebih tua dari 10 menit sebagai `Failed` saat start;
  perilaku ini diuji otomatis, tetapi rendaman nyata tetap perlu dicatat.

## F. Bukti yang harus disimpan

- `bukti.txt` dari setiap jalankan skrip (lingkungan, jawaban server, status pekerjaan). Jalur
  driver dan ESC/POS menghasilkan folder berbeda — simpan keduanya, jangan hanya yang terakhir.
- Foto struk uji dan struk pesanan (terutama A4/A5), dengan nama file menyebut mode cetaknya.
- Tangkapan layar antrean Windows saat kertas habis/printer mati — khusus jalur driver; pada
  ESC/POS tidak ada antrean Windows, jadi yang disimpan tangkapan layar riwayat pekerjaan cetak
  di aplikasi (baris `Gagal` beserta pesannya).
- Tangkapan layar bagian bawah **Perangkat Kasir** yang menampilkan mode tersimpan, misalnya
  `Mode tersimpan di server: ESC/POS langsung ke tcp://192.168.1.50:9100.`
- Tabel perangkat pada bagian A yang sudah diisi, termasuk kolom alamat IP dan mode cetak.

## G. Batas yang jangan dilanggar

- Jangan memasang atau mengganti driver lewat aplikasi; pemasangan dilakukan manual di Windows.
- Jangan menandai PASS dari jawaban HTTP saja.
- Jangan menandai PASS satu mode lalu menganggap mode lain ikut lulus — keduanya punya jalur
  perangkat lunak yang berbeda dan kegagalan yang berbeda.
- Uji fisik dilakukan di PC kasir. Menjalankan skrip terhadap pengganti printer di
  `127.0.0.1` (emulator Node) hanya membuktikan perkakas ujinya bekerja, bukan printer-nya.
- Jangan mengisi `-EscPosHost` dengan alamat publik: struk memuat nama dan nomor telepon
  pelanggan, jadi aplikasi hanya mau mengirim ke loopback atau jaringan privat toko.
- Jangan menguji laci uang sebelum tahap A dan B lulus.
- Model yang belum dicoba tetap ditulis "belum diuji", bukan "didukung".
