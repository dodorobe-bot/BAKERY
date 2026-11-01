# Panduan Uji di PC Kasir — SERUNI 22 BAKERY 1.2.12

Halaman ini untuk pemilik/teknisi yang menjalankan uji perangkat nyata. Rincian tiap langkah
ada di `docs\pos-uji-fisik.md`; di sini hanya urutan kerja dan perintah yang sudah pasti.

Hasil di PC kasir **tidak boleh disimpulkan dari layar aplikasi saja**. Kertas struk harus
dilihat, pemindai harus ditembakkan, dan antrean Windows harus dibuka saat uji kegagalan.

## Sebelum mulai

Catat dulu (tanpa ini hasil tidak bisa dibandingkan antar model):

| Butir | Isi |
|---|---|
| Merek & model printer | |
| Driver + versi | |
| Port Windows (USB/COM/IP) | |
| Alamat IP printer (bila ESC/POS) | |
| Port TCP (umumnya 9100) | |
| Lebar kertas | 80 mm / 58 mm |
| Merek & model pemindai | |
| Akhiran pemindai | Enter / Tab / tanpa akhiran |
| Versi Windows PC kasir | |
| Tanggal uji | |

Siapkan kamera untuk memfoto setiap struk, dan folder kosong untuk menyimpan bukti.

## Jalur A — uji lewat aplikasi terpasang (cara utama)

1. pasang paket 1.2.12 seperti biasa, buka aplikasi, login sebagai Owner.
2. Buka **Perangkat Kasir**. Pilih **Cara mencetak**:
   - **Driver Windows** + nama printer yang persis sama dengan daftar Windows, atau
   - **ESC/POS langsung** + **Alamat printer** (IP di jaringan toko) + **Port** (9100),
     centang **Potong kertas setelah struk selesai** bila printer punya pemotong.
3. Tekan **Simpan Pengaturan**, lalu **Cetak Struk Uji**. Ulangi untuk jalur kedua
   (ganti mode, simpan, cetak lagi).
4. Di **Daftar Pesanan & Penjualan**, buat satu transaksi uji lalu cetak struknya.
   Cetak ulang transaksi yang sama dan pastikan muncul penanda `*** CETAK ULANG ***`
   tanpa menambah pesanan.
5. Uji kegagalan:
   - Keluarkan kertas, cetak lagi. Catat status di aplikasi dan di
     **Settings → Printers & scanners → Open print queue**.
   - Matikan printer, cetak lagi. Catat statusnya. Nyalakan printer dan lihat apakah
     pekerjaan lama ikut tercetak.
   - Pasang kertas/nyalakan printer, cetak ulang manual. Pastikan jumlah pesanan tidak bertambah.
6. Pemindai: fokuskan kolom **Pindai Barcode Produk**, tembakkan 10 kali cepat, lalu
   tembakkan kode yang tidak terdaftar. Uji juga klik di luar kolom sebelum menembak.
7. Laci uang: **jangan diuji sebagai fitur** — aplikasi 1.2.12 belum punya pengendali laci.
   Yang boleh dicatat hanyalah apakah laci terbuka manual/lewat sakelar printer.

## Jalur B — skrip uji otomatis (butuh salinan repo)

Skrip `scripts\pos-physical-check.ps1` menjalankan host dari
`src\Host.Service\bin\<Configuration>\net8.0\Seruni22Bakery.Host.dll` dan bundel `desktop\`.
Artinya skrip ini **bukan bagian dari installer**: ia butuh salinan repo yang sudah dibangun
(.NET 8 SDK dan bundel frontend). Skrip memakai basis data sementara di `%TEMP%`, jadi data
usaha di aplikasi terpasang tidak tersentuh.

Dari akar repo, jalur driver Windows:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -CetakMode windows-driver -PrinterName "NAMA PRINTER DI WINDOWS" -PaperWidthMm 80 -SkipPrint

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -CetakMode windows-driver -PrinterName "NAMA PRINTER DI WINDOWS" -PaperWidthMm 80 -AllowPrint
```

Jalur ESC/POS langsung (printer jaringan):

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -CetakMode escpos-raw -EscPosHost 192.168.1.50 -EscPosPort 9100 -EscPosPotong -SkipPrint

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -CetakMode escpos-raw -EscPosHost 192.168.1.50 -EscPosPort 9100 -EscPosPotong -AllowPrint
```

Mode kegagalan (kertas habis / printer mati):

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
    -CetakMode escpos-raw -EscPosHost 192.168.1.50 -EscPosPort 9100 -Mode Failure -AllowPrint
```

Simpan `bukti.txt` dari setiap jalankan. Keluaran `HASIL OTOMATIS: FAIL` berarti skrip gagal
menyelesaikan alurnya — jangan diabaikan, salin baris `GAGAL`-nya ke laporan.

**Jangan** memakai `Microsoft Print to PDF` atau `OneNote` sebagai pengganti printer thermal.
Di mesin pengembangan keduanya gagal (lihat `README.md` bagian 3): port `PORTPROMPT:` menuntut
dialog simpan, sehingga uji menggantung dan bukan bukti apa pun.

## Yang harus difoto/disimpan

1. Struk uji jalur driver dan jalur ESC/POS (nama berkas menyebut jalurnya).
2. Struk pesanan: tampak nomor nota, `2 x Rp 12.500`, `Total Rp 25.000`, `DP / Uang Muka`,
   `Sisa Tagihan`, dan penanda cetak ulang.
3. Struk saat kertas habis dan saat printer mati, plus tangkapan antrean Windows
   (jalur driver) atau riwayat pekerjaan cetak di aplikasi (jalur ESC/POS).
4. `bukti.txt` setiap jalankan skrip.
5. Layar **Perangkat Kasir** bagian bawah yang menampilkan mode tersimpan, misalnya
   `Mode tersimpan di server: ESC/POS langsung ke tcp://192.168.1.50:9100.`
6. Tabel perangkat di atas yang sudah diisi.

## Kapan berhenti dan melapor

- Cetak driver dilaporkan **Tercetak** tetapi kertas tidak keluar → bukan PASS; catat dan lanjut
  periksa antrean. Windows memang bisa menerima pekerjaan saat printer mati.
- ESC/POS melaporkan **Gagal** dengan pesan sambungan → periksa IP/port dan jaringan printer,
  lalu ulangi. Jangan mengganti jalur hanya untuk membuat status hijau.
- Pemindai memasukkan kode ke kolom lain → catat kolom mana yang aktif; itu perilaku wedge,
  bukan kerusakan aplikasi.
- Ada pesanan bertambah saat cetak ulang → hentikan uji dan laporkan; itu cacat transaksi.

Setelah semua bukti terkumpul, kirimkan folder bukti beserta tabel perangkat. Verdict perangkat
baru bisa dinaikkan dari **BELUM DIUJI** setelah kertas dan pemindai benar-benar terlihat bekerja.
