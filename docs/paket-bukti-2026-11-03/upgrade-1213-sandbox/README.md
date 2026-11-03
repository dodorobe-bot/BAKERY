# Uji pasang & peningkatan 1.2.12 → 1.2.13 di Windows Sandbox (2026-11-03)

Hasil akhir: **PASS — 105 pemeriksaan lulus, 0 gagal** (`run1/status.txt`, rekap
`lulus=105 gagal=0 total=105`). Hanya satu run; tidak ada run pertama yang gagal lalu
diperbaiki. Semua konstanta harness (hash, ukuran, ProductCode, nama bundel, hash
`Host.dll`) dicocokkan ke berkas di `dist\installer` dan `dist\publish-single.manifest.txt`
**sebelum** sandbox diluncurkan, dan `run-sandbox.ps1 -Periksa` mengiyakan checksum kit
sebelum ada installer yang dijalankan.

## Lingkungan

- Windows Sandbox, tamu **Windows 11 Enterprise build 26100**, 1.535 MB, jaringan **dimatikan**
  (`<Networking>Disable</Networking>`).
- Pengguna tamu `WDAGUtilityAccount`, elevated. Tidak ada runtime .NET bersama (publish
  self-contained) — diperiksa di langkah pertama.
- Mesin host: Windows 11 build 26300, RAM 3.746 MB, ±380 MB bebas saat mulai. Host hanya
  membaca/menulis folder kit `C:\tmp\kit-uji-1213`; tidak ada installer yang dijalankan di host.
- Durasi: 18:00:20 → 18:11:34 (±11 menit), boot tamu ±69 detik.

## Paket yang diuji

| Berkas | Ukuran (bita) | SHA-256 | ProductCode |
|---|---|---|---|
| `Seruni22Bakery_v1.2.12_x64.msi` | 50.399.748 | `5ee109f9ece7cd24b4125adb5f999aedd8cd90d701a77bb9b2d8e9a8bd8fc859` | `{1D0B5C27-C2B9-49AD-A675-BA390B33E3E3}` |
| `Seruni22Bakery_v1.2.13_x64.msi` | 50.416.132 | `eb3f40fb0fa2fe1fb7e09bee733db524a8b29fb55af367b1bd8d0df1f5afd132` | `{CE68DB95-1278-4892-B835-7F875F0DAB63}` |
| `Seruni22Bakery_Setup_v1.2.12.exe` | 42.675.344 | `f43f4e8583cc1df84354039925f45046aafe93a6f3996b112c80501229a506c1` | — (Inno, AppId `{5D9D507C-…}_is1`) |
| `Seruni22Bakery_Setup_v1.2.13.exe` | 42.672.292 | `c1a0af29bd8f5bf9c439cadef3c1d8c891da0c49a3271384192fd682c444ed9d` | — (Inno, AppId `{5D9D507C-…}_is1`) |

1.2.13 dibangun dari commit `5993072` (pohon sumber bersih, `pohonKotor=0`). Kedua paket
1.2.13 **NotSigned**. UpgradeCode sama dengan 1.2.4–1.2.12 (`B975F792-…`), jadi peningkatan
in-place memang jalur yang didukung. Checksum kit (paket + skrip + manifes) ada di
`KIT-SHA256SUMS.txt`.

## Yang dibuktikan

**Peningkatan MSI 1.2.12 → 1.2.13 dengan data utuh**

- 1.2.12 dipasang senyap (exit 0), lalu diisi lewat API nyata: sandi admin diganti, produk
  `Roti Uji 1213` (Rp 15.000) dibuat, merek `Toko Uji Sandbox 1213` disetel.
- Peningkatan senyap 1.2.13: exit 0, log memuat `WIX_UPGRADE_DETECTED` untuk ProductCode
  1.2.12, `RemoveExistingProducts` jalan, ARP 1.2.12 hilang, ARP 1.2.13 satu-satunya.
- **393 berkas payload cocok persis** dengan manifes (`cocok=393 beda=0 hanya-terpasang=0
  hanya-paket=0 total=393`), termasuk `Seruni22Bakery.Host.dll` (hash `d4d1bf68…`) dan
  bundel baru `index-BZfyiTsC.js` / `index-CUfTbA7B.css`; hanya dua berkas aset yang ada,
  jadi bundel 1.2.12 benar-benar tersapu, bukan tertinggal.
- Data bertahan: sandi hasil ganti masih bisa login, `mustChangePassword=False`, produk dan
  merek terbaca utuh, logo bisa dipasang ulang dan kembali sama persis.
- **Tidak ada migrasi baru**: periksa-diri melaporkan skema 18 = skema terbaru terkemas,
  `migrasi dijalankan x0 (diharapkan 0)`, dan 10 pemeriksaan integritas 0 gagal.

**Pagar kanal dan versi**

- EXE 1.2.13 menolak dipasang selama MSI 1.2.12 ada: exit 1, log menyebut `Channel = msi`,
  tanpa folder, tanpa ARP, tanpa penanda HKCU.
- MSI 1.2.12 (lama) ditolak di atas 1.2.13: exit 1603 dengan alasan turun versi; versi
  terpasang tetap 1.2.13.
- MSI 1.2.13 ditolak selama EXE terpasang: exit 1603 dengan alasan kanal EXE; tidak ada
  folder Program Files yang terbentuk; EXE tetap satu-satunya instalasi.
- EXE 1.2.12 tidak bisa menimpa EXE 1.2.13: exit 1, penanda versi dikembalikan dan
  instalasi tetap 1.2.13.

**Jalur ESC/POS raw dari biner hasil peningkatan**

- Cetak uji dari host **hasil peningkatan** diterima emulator loopback port 9100: 635 bita
  (`run1/escpos-diterima.bin`), diawali `ESC @`, diakhiri `ESC d 3`, nominal contoh
  `2 x Rp 15.000 … Rp 30.000` benar dan `Rp 300` lama tidak muncul lagi, `GS V` tidak
  dikirim saat cut=0, riwayat cetak mencatat `status=Printed kind=Test`.
- Pilihan perangkat (`mode=escpos-raw`, `tujuan=tcp://127.0.0.1:9100`) bertahan setelah host
  dihentikan dan dijalankan ulang.

**Copot**

- Copot MSI: folder program, pintasan Start Menu + Desktop publik, entri ARP, dan penanda
  HKLM hilang; `uji.db` dan `uji.key` di luar folder program tetap utuh.
- EXE 1.2.13 dipasang per pengguna (exit 0): 393 berkas payload cocok, health 200 melapor
  1.2.13, SPA memuat bundel 1.2.13, basis data baru menjalankan 18 migrasi, periksa-diri
  10/10.
- Copot EXE: folder, pintasan, ARP, dan penanda HKCU hilang; folder data pengguna beserta
  `seruni22bakery.db` tetap ada; tidak ada proses yatim.

## Batas bukti

- Satu mesin, satu guest Windows 11. **Windows 10 belum diuji.**
- Cetak lulus ke **emulator loopback**, bukan printer thermal 80 mm fisik; pemindai barcode
  hanya simulasi wedge; laci uang dan pemotong kertas belum diuji pada perangkat nyata.
- Kedua paket **NotSigned**, jadi SmartScreen/SmartCheck masih merupakan risiko di PC nyata.
- Yang tidak diuji di sini: peningkatan dari 1.2.9 dan 1.2.10 langsung ke 1.2.13, dan
  pemasangan oleh pemilik di PC kasir.

## Berkas

`run1/` isinya apa adanya dari folder bukti tamu: `status.txt` (105 baris pemeriksaan),
`validasi.log`, log installer MSI/Inno per langkah, log host, dan `escpos-diterima.bin`.
