# Bukti Uji Perangkat 1.2.12 — 2026-11-01

Uji ini dijalankan di **mesin pengembangan**, bukan PC kasir, dan **tanpa perangkat fisik**.
Semua yang bisa dibuktikan lewat perangkat lunak sudah dijalankan; hasilnya ada di folder ini.
Yang tidak bisa dibuktikan di sini ditulis apa adanya sebagai **BELUM DIUJI**, bukan lulus.

Putusan rilis tidak berubah: **NOT READY**.

| Bagian | Hasil | Boleh disebut lulus? |
|---|---|---|
| Pemindai barcode (simulasi papan ketik) | 14 dari 14 pemeriksaan lulus | Lulus **sebagai simulasi**, bukan perangkat fisik |
| Cetak thermal (driver Windows) | Percobaan ke printer virtual gagal karena batas lingkungan | Tidak |
| Cetak thermal (ESC/POS langsung) | Byte sudah dibuktikan emulator di uji Sandbox; printer fisik tidak ada | Tidak untuk kertas |
| Laci uang | Fitur belum ada; jaminan negatif (tidak ada `ESC p`) terverifikasi | Tidak ada fitur yang bisa diuji |
| Authenticode | Diblokir: 0 sertifikat code signing, kedua paket `NotSigned` | Tidak |

## 1. Lingkungan mesin uji

Diambil `perangkat-2026-11-01.txt` (skrip pendata `C:\tmp\cek-perangkat-2026-11-01.ps1`):

- Windows 11 Pro build 26300 64-bit, sesi **tidak elevated**, RAM 3746 MB.
- Printer terpasang hanya 2, keduanya virtual: `OneNote (Desktop)` (port `nul:`) dan
  `Microsoft Print to PDF` (port `PORTPROMPT:`). Tidak ada printer thermal.
- Tidak ada port COM; tidak ada perangkat yang dikenali sebagai pemindai barcode
  (perangkat HID yang ada hanya papan ketik/tetikus Logitech dan Primax).
- Tidak ada sertifikat code signing di `CurrentUser\My` maupun `LocalMachine\My`;
  `TrustedPublisher` kosong.
- Kedua paket 1.2.12 berstatus `NotSigned`.

Karena itu uji kertas, pemindai fisik, dan laci fisik **tidak mungkin** dijalankan di sini.

## 2. Pemindai barcode — simulasi wedge: PASS 14/14

Berkas: `pindai/uji-pindai.js`, `pindai/hasil-pindai.json`, `pindai/01-pindai-dikenal.png`,
`pindai/02-pindai-asing.png`, `pindai/host.out`.

Metode: aplikasi 1.2.12 yang sudah dikemas (`dist\publish-single\Seruni22Bakery.Host.exe`,
versi 1.2.12.0) dijalankan dengan **basis data sementara** di `C:\tmp\uji-pindai-1212\uji.db`.
Chromium (Playwright) mengetik kode **satu karakter demi satu karakter** ke kolom
"Pindai Barcode Produk", lalu menekan Enter — urutan ketukan yang sama dengan pemindai
mode keyboard wedge.

Hasil (2026-11-01 18:07 WIB, `ringkasan: PASS`, `jumlahLulus: 14`, `jumlahGagal: 0`):

1. Login UI dengan sandi baru berhasil.
2. Sebelum Enter, kolom berisi utuh `8991234567890` — tidak ada karakter hilang.
3. Barcode dikenal memilih produk yang benar (`select=1`).
4. Total terisi dari harga katalog (`12500`, bukan diketik kasir).
5. Kolom pindai kosong lagi setelah Enter.
6. Barcode kedua memilih produk kedua (`select=2`), total `8000`.
7. Barcode asing menampilkan `Barcode "0000000000000" tidak cocok dengan produk mana pun.`
8. Barcode asing tidak mengubah produk yang sudah terpilih; kolom dikosongkan.
9. Tanpa Enter tidak ada produk terpilih (pilihan tetap `2`).
10. 10 pindai cepat berturut-turut: 10 dari 10 benar, tanpa karakter hilang, selesai 4189 ms.
11. Saat fokus di luar kolom, kolom pindai tidak terisi.

Tidak ada galat konsol peramban (`galatKonsol: []`).

**Batas kejujuran:** ini bukan pemindai fisik. Yang terbukti adalah alur aplikasi
(Enter sebagai pemicu, pencocokan barcode tanpa membedakan huruf besar/kecil, pengisian total,
pesan kode asing, perilaku fokus, ketahanan 10 pindai cepat) dengan urutan ketukan seperti wedge.
Yang belum terbukti: jeda antar karakter perangkat nyata yang bisa jauh lebih rapat, ada/tidaknya
karakter pembuka/penutup khusus dari model tertentu, dan perilaku saat perangkat mengirim
Enter ganda. Karena itu panduan PC kasir tetap meminta uji perangkat nyata.

## 3. Cetak thermal — percobaan di mesin ini GAGAL, bukan PASS

Berkas: `cetak/uji-cetak-1212.log`, `cetak/bukti.txt`, `cetak/host.out`,
`cetak/doc-pdf-sebelum.txt`.

Perintah: `scripts\pos-physical-check.ps1 -PrinterName "Microsoft Print to PDF"
-PaperWidthMm 58 -AllowPrint -Configuration Release`, dijalankan 2026-11-01 18:08 WIB.

Yang berhasil sebelum titik gagal:

- Host uji hidup di `http://127.0.0.1:56373` dengan basis data sementara
  `%TEMP%\seruni-pos-fisik-20261101-180809\uji.db`.
- Ganti sandi awal, login Owner, dan simpan pengaturan perangkat: HTTP 200.
- Pesanan uji `#1` dibuat: HTTP 201.

Yang gagal:

- `POST /api/devices/test-print` tidak menjawab dalam 40 detik:
  `GAGAL tak terduga: The operation has timed out.` (`HASIL OTOMATIS: FAIL`).
- Log host berhenti tepat di `Executing endpoint 'HTTP: POST /api/devices/test-print'`.
- Antrean cetak Windows tetap 0 pekerjaan; tidak ada berkas PDF baru di `Documents`
  (daftar sebelum: `rew.pdf`, `teat 3.pdf`, `teat 4.pdf`, `test.pdf`, `test2.pdf`).

Penyebab yang paling masuk akal: `Microsoft Print to PDF` memakai port `PORTPROMPT:`, yang
menampilkan dialog "Simpan sebagai" secara interaktif. Pencetakan senyap
(`StandardPrintController`) menunggu dialog itu, sehingga panggilan `Print()` memblokir dan
endpoint tidak pernah selesai. Ini **batas alat uji di mesin ini**, bukan bukti bahwa jalur
driver thermal gagal atau berhasil.

Konsekuensinya:

- Jalur driver Windows ke **printer thermal fisik: BELUM DIUJI**.
- Jalur `escpos-raw` ke **printer jaringan fisik: BELUM DIUJI** (byte-nya sudah terbukti di
  emulator: 635 bita, SHA-256 `a107547c…af22`, lihat bagian 4).
- Jangan menandai PASS dari jawaban HTTP atau dari status "Printed" di spooler.

## 4. Laci uang — fitur belum ada; jaminan negatif terverifikasi

Berkas: `laci/escpos-diterima.bin`, `laci/hasil-cek-byte.txt`,
`laci/tes-aplikasi-escpos-release.txt`, `laci/tes-ui-device-release.txt`.

`ICashDrawerAdapter` masih antarmuka tanpa implementasi dan tanpa endpoint. Aplikasi memang
belum mengendalikan laci uang, jadi tidak ada fungsi buka laci yang bisa diuji.

Yang bisa dibuktikan adalah **jaminan negatif**: struk ESC/POS hasil pencetakan dari biner
1.2.12 hasil peningkatan di Windows Sandbox (635 bita, SHA-256
`a107547c6f04070b445650da77b74086d41f86ab596671fbcf571fb3ff92af22`) diperiksa byte demi byte:

- `ESC p 0 25 250` (pemantik laci): **tidak ada**.
- `ESC a`, `ESC E`, `ESC t`, `GS V`, `GS k`: **tidak ada**.
- Awal `1B 40` (`ESC @`), akhir `1B 64 03` (`ESC d 3`), 17 baris baru.

Tes otomatis Release yang dijalankan ulang 2026-11-01 18:14 WIB, semuanya lulus:

- `tests/Application.Tests` filter `EscPos|PosDevice`: **96 lulus, 0 gagal**.
- `tests/UI.Tests` filter `EscPosRawPrinterTests|Batch8DeviceApiTests|Batch8DeviceUiTests`:
  **31 lulus, 0 gagal** (termasuk tes yang menegaskan perintah laci tidak pernah dikirim).

Kesimpulan: laci uang tetap dibuka manual atau lewat sakelar printer, bukan oleh aplikasi ini.
Jangan menulis "laci uang didukung".

## 5. Authenticode — diblokir, bukan lulus

Berkas: `auth/uji-auth-1212.log`, `auth/uji-auth-1212.ps1`.

- Sertifikat code signing terpasang: **0** (CurrentUser dan LocalMachine).
- `Get-AuthenticodeSignature` atas `Seruni22Bakery_Setup_v1.2.12.exe` dan
  `Seruni22Bakery_v1.2.12_x64.msi`: `Status=NotSigned`, `Signer=(tidak ada)`.
- `signtool verify /pa` untuk kedua berkas: kode keluar 1, `SignTool Error: No signature found.`
- Uji awal `scripts\sign-paket.ps1 -Thumbprint 0000…0000` ditolak dengan pesan
  `Sertifikat dengan thumbprint 000…0 tidak ditemukan di CurrentUser\My atau LocalMachine\My.`
- SHA-256 kedua paket **tidak berubah** setelah percobaan itu
  (`BERKAS BERUBAH: 0`), jadi penolakan terjadi sebelum ada byte yang ditulis.

Catatan: baris `KODE KELUAR:` di log kosong karena skrip uji menerima galat sebagai
terminating error; bukti penolakan bertumpu pada pesan dan hash yang tidak berubah.

Artinya: Windows akan menampilkan penerbit "Tidak dikenal" saat paket dijalankan. Ini tidak
boleh digambarkan sebagai paket bertanda tangan. Pembelian sertifikat masih ditunda pemilik.

## 6. Yang masih harus dijalankan di PC kasir

Ringkas; langkah lengkap ada di `docs\pos-uji-fisik.md` dan `PANDUAN-PC-KASIR.md` di folder ini.

1. Cetak struk uji dan struk pesanan ke printer thermal 80 mm, jalur driver **dan** ESC/POS.
2. Kertas habis dan printer dimatikan: catat status antrean/riwayat, pastikan tidak ada pesanan ganda.
3. Pemotong kertas (bila ada).
4. Pemindai fisik: 10 pindai cepat, kode asing, tanpa Enter, fokus di luar kolom.
5. Laci uang: jangan diuji sebagai fitur — belum diimplementasikan.
6. Uji yang sama di Windows 10 bila PC kasir memakainya.
7. Authenticode setelah sertifikat tersedia (`scripts\sign-paket.ps1 -Thumbprint ...`).

## 7. Isi folder

| Berkas | Isi |
|---|---|
| `perangkat-2026-11-01.txt` | Pendataan OS, printer, port, antrean, PnP, sertifikat, jaringan |
| `pindai/uji-pindai.js` | Skrip simulasi wedge (Playwright, ketik per karakter + Enter) |
| `pindai/hasil-pindai.json` | 14 pemeriksaan, `PASS`, 0 gagal |
| `pindai/01-pindai-dikenal.png` | Layar setelah barcode dikenal |
| `pindai/02-pindai-asing.png` | Layar setelah barcode asing |
| `pindai/host.out` | Log host aplikasi 1.2.12 selama simulasi |
| `cetak/uji-cetak-1212.log` | Transcript percobaan cetak (termasuk kegagalan) |
| `cetak/bukti.txt` | Bukti dari skrip uji fisik (`HASIL OTOMATIS: FAIL`) |
| `cetak/host.out` | Log host, berhenti di `test-print` |
| `cetak/doc-pdf-sebelum.txt` | Daftar PDF di `Documents` sebelum percobaan |
| `laci/escpos-diterima.bin` | 635 bita struk ESC/POS dari biner hasil peningkatan |
| `laci/hasil-cek-byte.txt` | Pemeriksaan byte: `ESC p` dan perintah terlarang lain tidak ada |
| `laci/tes-aplikasi-escpos-release.txt` | 96 tes lulus (Application, Release) |
| `laci/tes-ui-device-release.txt` | 31 tes lulus (UI, Release) |
| `auth/uji-auth-1212.log` | Pendataan sertifikat, tanda tangan paket, `signtool verify` |
| `auth/uji-auth-1212.ps1` | Skrip audit Authenticode yang dijalankan |
