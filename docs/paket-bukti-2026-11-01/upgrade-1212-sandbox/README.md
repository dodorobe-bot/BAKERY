# Uji pasang & peningkatan 1.2.9 → 1.2.12 di Windows Sandbox (2026-11-01)

Hasil akhir: **PASS — 105 pemeriksaan lulus, 0 gagal** (`run2/status.txt`, rekap
`lulus=105 gagal=0 total=105`). Run pertama (`run1/`) berakhir 100 lulus / 5 gagal; kelima
kegagalan itu cacat skrip uji, bukan produk, dan sudah diperbaiki sebelum run kedua. Kedua run
disimpan apa adanya supaya klaim ini bisa diaudit.

## Lingkungan

- Windows Sandbox, tamu **Windows 11 Enterprise build 26100**, 1.536 MB, jaringan **dimatikan**.
- Pengguna tamu `WDAGUtilityAccount`, elevated. Tidak ada runtime .NET bersama (publish
  self-contained).
- Mesin host: Windows, RAM 3.746 MB. Tutup peramban sebelum run; percobaan sandbox 2026-10-31
  pernah gagal boot karena memori virtual habis (`docs/paket-bukti-2026-10-31/uji-129/`), bukan
  karena paketnya.
- Durasi run 2: 17:44:08 → 17:56:44 (±13 menit), boot tamu ±2 menit.

## Paket yang diuji

| Berkas | Ukuran (bita) | SHA-256 |
|---|---|---|
| `Seruni22Bakery_v1.2.9_x64.msi` | 50.391.554 | `4cf092ce05cfb0301d2740cc136322135dc9f506a482c807bea020782052d635` |
| `Seruni22Bakery_v1.2.12_x64.msi` | 50.399.748 | `5ee109f9ece7cd24b4125adb5f999aedd8cd90d701a77bb9b2d8e9a8bd8fc859` |
| `Seruni22Bakery_Setup_v1.2.9.exe` | 42.662.274 | `fd233be4e4a8441dd2e63349b723466ee4ab803f41e6d6b357b97e05f55143b7` |
| `Seruni22Bakery_Setup_v1.2.12.exe` | 42.675.344 | `f43f4e8583cc1df84354039925f45046aafe93a6f3996b112c80501229a506c1` |

Kedua paket 1.2.12 **NotSigned**. Checksum kit (termasuk skrip) diperiksa dulu di host lewat
`KIT-SHA256SUMS.txt`; hasilnya "cocok semua".

## Yang dibuktikan run 2

**Peningkatan MSI 1.2.9 → 1.2.12 dengan data utuh**

- 1.2.9 dipasang senyap, lalu diisi lewat API: sandi admin diganti, produk `Roti Uji 1212`
  (Rp 15.000) dibuat, merek `Toko Uji Sandbox 1212` disetel.
- Peningkatan senyap 1.2.12: exit 0, log memuat `WIX_UPGRADE_DETECTED` untuk ProductCode 1.2.9
  (`{7609C8A3-…}`), `RemoveExistingProducts` dijalankan, ARP 1.2.9 hilang, ARP 1.2.12 tunggal.
- **393 berkas payload cocok persis** dengan manifes (`cocok=393 beda=0 hanya-terpasang=0
  hanya-paket=0 total=393`), termasuk `Host.dll`, bundel `index-dCrlHoiC.js` /
  `index-RvdFb41s.css`, dan aset lama tersapu.
- **Tidak ada migrasi baru**: log host 1.2.12 memuat 0 baris `Migrasi N diterapkan`, sesuai
  karena 1.2.9 sudah skema 18. Periksa-diri 9/9, `terpasang 18 … terkemas 18`.
- Sandi hasil ganti masih berlaku, produk terbaca dengan nilai sama, merek tetap, logo bisa
  dipasang dan kembali persis sama (`/api/brand` tetap 4 kunci).
- Pintasan menunjuk `C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.exe`; penanda HKLM
  `Channel=msi Version=1.2.12`.

**ESC/POS raw dari biner hasil peningkatan**

- Mode `escpos-raw` + `127.0.0.1:9100` + potong mati disimpan lewat
  `PUT /api/devices/settings`, lalu `POST /api/devices/test-print`.
- Emulator TCP loopback menerima **635 bita** (`escpos-diterima.bin`, SHA-256
  `a107547c6f04070b445650da77b74086d41f86ab596671fbcf571fb3ff92af22`):
  - diawali `ESC @` (0x1B 0x40), diakhiri `ESC d 3` (0x1B 0x64 0x03);
  - memuat `2 x Rp 15.000 … Rp 30.000`, dan **tidak** memuat `Rp 300` (nominal lama);
  - tidak ada `GS V` selama pemotongan mati;
  - teks lengkap: merek, tagline, outlet, `80 mm / 48 kolom`, waktu toko.
- `/api/devices/status` melaporkan `mode=escpos-raw`, tujuan `tcp://127.0.0.1:9100`;
  `/api/devices/print-jobs` mencatat pekerjaan `Test` berstatus `Printed` dengan tujuan yang sama.
- Pengaturan bertahan setelah host di-restart (dibaca ulang dari basis data, bukan dari memori
  proses).

**Pagar kanal dan versi**

- EXE 1.2.12 menolak berjalan selama MSI terpasang: exit 1, log
  `Pagar kanal: penanda HKLM Software\Seruni22Bakery\Channel = msi ditemukan.` Tidak ada folder,
  ARP, atau penanda HKCU yang terbentuk.
- MSI 1.2.9 ditolak di atas 1.2.12: exit 1603, alasan turun versi tercatat; versi terpasang tetap.
- EXE 1.2.12 menolak bila penanda versi lebih baru (`9.9.9`): exit 1, log
  `Pagar versi: HKCU Version=9.9.9 lebih baru dari paket 1.2.12.`
- MSI 1.2.12 menolak selama EXE terpasang: exit 1603, log menyebut EXE masih terpasang.

**Copot tidak menghapus data**

- Copot MSI: folder program, pintasan, ARP, dan penanda HKLM hilang; `uji.db` + `uji.key` tetap.
- EXE 1.2.12 dipasang per pengguna, host-nya menjalankan **18 migrasi** pada basis data baru,
  SPA memuat bundel 1.2.12, periksa-diri 9/9.
- Copot EXE: folder program, pintasan per-pengguna (Start Menu subfolder grup + Desktop), ARP,
  dan `HKCU\Software\Seruni22Bakery\Channel` hilang; folder
  `%LOCALAPPDATA%\Seruni22Bakery` beserta `penanda-data.txt` dan `seruni22bakery.db` **tetap ada**.
- Tidak ada proses `Seruni22Bakery*` yatim.

## Run 1: lima kegagalan dan sebabnya

Run 1 (17:19:11 → 17:33:10) berakhir `lulus=100 gagal=5 total=105`. Semua pemeriksaan produk
lulus; kelima kegagalan berasal dari skrip uji yang membaca teks/path yang salah. Tidak ada kode
produk yang diubah setelah run 1.

| Pemeriksaan | Pesan run 1 | Sebab sebenarnya | Bukti |
|---|---|---|---|
| log EXE menyebut MSI terdeteksi | `log tidak memuat alasan kanal` | regex mengharapkan `MSI sudah terpasang`; 1.2.12 menulis `Pagar kanal: … Channel = msi ditemukan.` | `run1/exe-1212-guard.log` baris 18; exit 1 |
| tidak ada migrasi baru di atas skema 18 | `migrasi dijalankan x` | `host-1212.log` dibaca saat host masih hidup; berkas belum berisi/terkunci | `run1/host-1212.log` sekarang memuat 0 baris `Migrasi` |
| pintasan per-pengguna menunjuk folder uji | `pintasan per-pengguna salah` | harness mencari `Programs\Seruni 22 Bakery.lnk`; Inno memasangnya di subfolder grup | `run1/exe-1212-install.log` baris 2000: `…\Programs\Seruni 22 Bakery\Seruni 22 Bakery.lnk` |
| basis data EXE baru menjalankan 18 migrasi | `migrasi=` | dibaca sebelum host dihentikan | `run1/host-exe-1212.log`: 18 baris `Migrasi N diterapkan` |
| log penolakan EXE menyebut versi lebih baru | `alasan versi tidak tercatat` | regex mengharapkan `Versi 9.9.9 sudah terpasang`; teks asli `Pagar versi: HKCU Version=9.9.9 …` | `run1/exe-1212-versi-guard.log` baris 18; exit 1 |

Perbaikan yang diterapkan pada `uji-1212.ps1` (berkas ini disalin di folder ini):

1. Regex pagar kanal → `Pagar kanal` + `Channel = msi` (tetap memeriksa dua bagian pesan).
2. Pemeriksaan migrasi peningkatan dipindah ke **setelah** host dihentikan; membaca gabungan
   `host-1212.log` + `host-1212-restart.log`.
3. Path pintasan Start Menu EXE → `…\Start Menu\Programs\Seruni 22 Bakery\Seruni 22 Bakery.lnk`
   (pemeriksaan "pintasan EXE hilang" sesudah copot ikut memakai path yang benar).
4. Pemeriksaan 18 migrasi EXE dipindah ke setelah `Hentikan-Host`.
5. Regex pagar versi EXE → `Pagar versi` + `9.9.9`.

Tidak ada pemeriksaan yang dilonggarkan: semua kondisi lulus/gagal tetap sama ketatnya, hanya
teks/path/urutan baca yang dikoreksi. Run 2 mengulang **seluruh** 105 pemeriksaan dari awal di
mesin sandbox baru, bukan hanya lima yang merah.

## Yang tidak dibuktikan folder ini

- Printer thermal fisik 80 mm, printer jaringan fisik, pemindai barcode, dan laci uang.
  Bagian ESC/POS hanya membuktikan byte yang dikirim, ke emulator perangkat lunak.
- Pemasangan di mesin pemilik dan di Windows 10. Sandbox ini Windows 11 Enterprise 26100.
- Tanda tangan Authenticode: kedua paket NotSigned.
- Basis data pemilik: seluruh uji memakai basis data sementara di dalam tamu.

## Cara mengulang

Kit asli ada di `C:\tmp\kit-1212` (tidak masuk repositori karena berisi empat installer,
±186 MB). Berkas skrip, panduan, daftar checksum, dan manifes payload disalin di folder ini.

```
powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1 -Periksa
powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1
```

Baris terakhir `bukti\status.txt` harus `| rekap | PASS |` dengan total minimal 80; run yang
terpotong menghasilkan rekap FAIL dan tidak dihitung lulus.

## Berkas

| Berkas | Isi |
|---|---|
| `run1/status.txt` | 105 baris hasil run 1 (5 FAIL) |
| `run1/*.log`, `run1/escpos-diterima.bin` | log mentah run 1 |
| `run2/status.txt` | 105 baris hasil run 2, rekap PASS |
| `run2/validasi.log` | transkrip penuh run 2 |
| `run2/msi-*.log`, `run2/exe-*.log`, `run2/host-*.log` | log installer dan host run 2 |
| `run2/escpos-diterima.bin` | 635 bita struk uji yang diterima emulator |
| `run2/runner.log` | catatan peluncur sisi host |
| `uji-1212.ps1`, `run-sandbox.ps1` | skrip final (versi yang dipakai run 2) |
| `PANDUAN-KIT.md`, `KIT-SHA256SUMS.txt`, `hash-publish-1212.txt` | panduan, checksum kit, manifes 393 berkas |
