# Uji Fisik Perangkat Kasir (Printer 80 mm → Pemindai → Laci Uang)

Panduan ini dipakai **di PC kasir dengan perangkat nyata**. Pada mesin pengembangan
(Windows 11 Pro 10.0.26300, 2026-10-28) hanya ada dua printer virtual — tidak ada printer thermal,
pemindai, COM port, atau laci uang — sehingga seluruh uji di bawah **belum dijalankan** dan
tidak boleh ditandai PASS.

Aturan utama: **status "Printed" hanya berarti spooler Windows menerima pekerjaan, bukan kertas
keluar.** Windows tetap mengantrekan pekerjaan saat printer mati. Lulus hanya bila kertasnya
dilihat langsung.

---

## A. Prasyarat

1. Printer thermal 80 mm terpasang dengan **driver resmi dari pabrik** (bukan driver generik
   "ESC/POS" yang tidak dikenal). Catat: merek, model, versi driver, port (USB/COM), versi Windows.
2. Kertas 80 mm terpasang, penutup printer tertutup, indikator normal.
3. Aplikasi SERUNI 22 BAKERY build Batch-8 terpasang, atau host dijalankan dari folder repo.
4. Pemindai barcode USB (tahap B) belum perlu dipasang pada tahap A.
5. Siapkan HP/kamera untuk memfoto struk sebagai bukti.

Isi tabel ini sebelum mulai (tanpa data ini hasilnya tidak bisa dibandingkan dengan model lain):

| Butir | Isi |
|---|---|
| Merek & model printer | |
| Driver + versi | |
| Port | |
| Versi Windows | |
| Lebar kertas | 80 mm |
| Pemindai (merek/model) | |

## B. Tahap A — Printer 80 mm

### A1. Rekam lingkungan dan alur (tanpa mencetak)

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -PrinterName "NAMA PRINTER DI WINDOWS" -PaperWidthMm 80 -SkipPrint
```

Yang dibuktikan: printer terpasang dan terbaca, driver/port/status tercatat, host uji memakai
basis data sementara, login Owner, pengaturan perangkat tersimpan, pesanan uji dibuat tepat satu.
Tidak ada kertas yang keluar pada langkah ini.

### A2. Cetak struk uji (POS-01)

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -PrinterName "NAMA PRINTER DI WINDOWS" -PaperWidthMm 80 -AllowPrint
```

Lulus bila **semua** terpenuhi:

- Kertas keluar berisi "UJI CETAK PERANGKAT", nama printer, "80 mm / 48 kolom", contoh
  `2 x Rp 15.000`, dan penutup "SELESAI".
- Tidak ada baris terpotong di kanan; huruf tidak berhimpit.
- Pekerjaan cetak di layar Perangkat Kasir berstatus **Tercetak** (bukan "Menunggu").
- Tidak muncul dialog printer di layar (cetak senyap).

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
   di **Settings → Printers & scanners → Open print queue**.
4. Pasang kembali kertas, lalu cetak ulang dari layar Penjualan.

Lulus bila: tidak ada pesanan ganda, pesanan tetap ada dengan total yang sama, dan setelah
kertas dipasang struk baru bisa dicetak. **Jangan lulus** hanya karena server menjawab 200 —
periksa apakah kertas benar-benar keluar atau pekerjaan hanya mengendap di antrean.

### A5. Printer dimatikan (POS-04)

1. Matikan printer (kabel listrik/USB dicabut sesuai prosedur model).
2. Ulangi A2 `-Mode Failure -AllowPrint`.
3. Perhatikan: Windows umumnya **menerima** pekerjaan dan menahannya di antrean, sehingga
   aplikasi bisa melaporkan "Tercetak". Ini keterbatasan yang harus dicatat, bukan bug yang
   disembunyikan.
4. Nyalakan printer. Bila pekerjaan lama ikut tercetak, catat; pastikan tidak ada pesanan kedua.
5. Cetak ulang manual; pastikan struk keluar dan transaksi tidak berganda.

### A6. Pemotong kertas (POS-05)

Amati apakah kertas terpotong otomatis dan rapi. Bila model tidak punya pemotong, tulis
"tidak mendukung" — aplikasi belum mengirim perintah potong terpisah.

### A7. Ganti printer tanpa ubah logika (POS-06)

Di layar **Perangkat Kasir**, pilih printer kedua (bila ada), simpan, cetak uji. Lulus bila
transaksi dan struk tetap sama isinya; yang berubah hanya perangkat tujuan.

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

## E. Uji rendam (setelah A dan B lulus)

- Jalankan pemakaian berulang 2–4 jam: cetak struk, cetak ulang, matikan/nyalakan printer,
  keluarkan kertas, biarkan antrean menumpuk.
- Catat: pertumbuhan memori host, pekerjaan `Menunggu` yang menggantung, kegagalan berulang,
  dan apakah ada pesanan yang hilang atau berganda.
- Aplikasi menandai pekerjaan `Pending` yang lebih tua dari 10 menit sebagai `Failed` saat start;
  perilaku ini diuji otomatis, tetapi rendaman nyata tetap perlu dicatat.

## F. Bukti yang harus disimpan

- `bukti.txt` dari setiap jalankan skrip (lingkungan, jawaban server, status pekerjaan).
- Foto struk uji dan struk pesanan (terutama A4/A5).
- Tangkapan layar antrean Windows saat kertas habis/printer mati.
- Tabel perangkat pada bagian A yang sudah diisi.

## G. Batas yang jangan dilanggar

- Jangan memasang atau mengganti driver lewat aplikasi; pemasangan dilakukan manual di Windows.
- Jangan menandai PASS dari jawaban HTTP saja.
- Jangan menguji laci uang sebelum tahap A dan B lulus.
- Model yang belum dicoba tetap ditulis "belum diuji", bukan "didukung".
