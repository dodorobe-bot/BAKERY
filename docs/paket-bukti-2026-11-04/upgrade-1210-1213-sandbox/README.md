# Uji pasang & peningkatan 1.2.10 → 1.2.13 di Windows Sandbox (2026-11-04)

Hasil akhir: **PASS — rekap `lulus=106 gagal=0 total=106`** (`run1/status.txt`,
`run1/runner.log` berhenti `ok=True`, keluar peluncur 0). Satu run, tanpa percobaan ulang.
Jalur ini yang akan dipakai mesin pilot: PC yang sekarang menjalankan 1.2.9/1.2.10 menaikkan
ke 1.2.13 lewat peningkatan in-place, bukan pasang baru.

## Catatan kejujuran: satu baris hilang di status.txt

Rekap menghitung 106 PASS, tetapi `run1/status.txt` hanya memuat 105 baris PASS + 1 baris rekap.
Yang tidak tertulis di `status.txt` adalah pemeriksaan **`pintasan EXE hilang`** — append
`Add-Content` ke `status.txt` hilang untuk satu baris itu, sedangkan pemeriksaannya sendiri
berhasil dan terekam di transkrip tamu: `run1/validasi.log` baris 142
`11:44:41 | pintasan EXE hilang | PASS | pintasan per-pengguna terhapus`. Klaimnya juga berdiri
di atas log installer, bukan hanya di atas satu baris rekap: `run1/exe-1213-uninstall.log`
mencatat `Deleting file: ...\Start Menu\Programs\Seruni 22 Bakery\Seruni 22 Bakery.lnk` dan
`Deleting file: C:\Users\WDAGUtilityAccount\Desktop\Seruni 22 Bakery.lnk`. Tidak ada FAIL yang
tertutupi; 106 pemeriksaan dijalankan, 106 lulus.

## Lingkungan

- Windows Sandbox, tamu **Windows 11 Enterprise build 26100**, 1.535 MB, jaringan **dimatikan**
  (`<Networking>Disable</Networking>`), pengguna `WDAGUtilityAccount` elevated.
- Tidak ada runtime .NET bersama di tamu (publish self-contained) — diperiksa sebagai langkah pertama.
- Mesin host: Windows 11 build 26300, RAM 3.746 MB; saat mulai 286 MB fisik bebas dan 3,47 GB
  ruang commit bebas. Host hanya membaca/menulis folder kit `C:\tmp\kit-uji-1210-1213`;
  tidak ada installer yang dijalankan di host, tidak ada basis data pemilik yang disentuh.
- Durasi: 11:31:05 diluncurkan → 11:44:46 rekap (±13,5 menit), boot tamu ±101 detik.

## Paket yang diuji

| Berkas | Ukuran (bita) | SHA-256 | ProductCode |
|---|---|---|---|
| `Seruni22Bakery_v1.2.10_x64.msi` | 50.395.652 | `872a3c39fe6f906dd4ced13ca434fdb7351b266147dbbd26e31a4384fff4b4af` | `{F74073DE-74DE-47CB-B9A7-358F0DE5E8FF}` |
| `Seruni22Bakery_v1.2.13_x64.msi` | 50.416.132 | `eb3f40fb0fa2fe1fb7e09bee733db524a8b29fb55af367b1bd8d0df1f5afd132` | `{CE68DB95-1278-4892-B835-7F875F0DAB63}` |
| `Seruni22Bakery_Setup_v1.2.10.exe` | 42.665.720 | `c882b69873cbd7dc51ef58761fe6d38ff2559fbe35141399b7023bced8cd78c5` | — (Inno, AppId `{5D9D507C-…}_is1`) |
| `Seruni22Bakery_Setup_v1.2.13.exe` | 42.672.292 | `c1a0af29bd8f5bf9c439cadef3c1d8c891da0c49a3271384192fd682c444ed9d` | — (Inno, AppId `{5D9D507C-…}_is1`) |

1.2.10 dibangun dari commit bertag `v1.2.10` (`5e23e66`, 2026-11-01); 1.2.13 dari commit bersih
`5993072`. Keempat paket **NotSigned**.

Konstanta harness tidak disalin dari dokumen: `ProductCode` dan `UpgradeCode` kedua MSI, baris
`Upgrade` (`WIX_UPGRADE_DETECTED` / `WIX_DOWNGRADE_DETECTED`), jumlah berkas (393 pada kedua
paket), dan nama bundel `wwwroot\assets` dibaca langsung dari tabel `Property`/`File` isi MSI;
hash dan ukuran dicocokkan ke `dist\installer` sesudah penyalinan. `UpgradeCode`
`B975F792-6F80-4D56-8A64-D274A044DCF1` sama dengan 1.2.4–1.2.13, jadi peningkatan in-place
memang jalur yang didukung. Skrip port yang melakukan penggantian ada di `port-kit-1210.py`
(rename token + tiga blok yang benar-benar berbeda dari harness 1.2.12 → 1.2.13), dan
`run-sandbox.ps1 -Periksa` mengiyakan checksum kit sebelum installer apa pun berjalan.

## Yang dibuktikan

**Peningkatan MSI 1.2.10 → 1.2.13 dengan data utuh**

- 1.2.10 dipasang senyap (exit 0), ARP dan versi peluncur melaporkan 1.2.10, lalu diisi lewat
  API-nya sendiri: sandi admin diganti, produk `Roti Uji 1213` (Rp 15.000, id=1) dibuat, merek
  `Toko Uji Sandbox 1213` disetel — `login bawaan admin berhasil di 1.2.10` PASS, jadi
  sandi bawaan 1.2.10 sama dengan yang dipakai harness.
- Peningkatan senyap 1.2.13: exit 0; log memuat `WIX_UPGRADE_DETECTED` untuk ProductCode
  `{F74073DE-…}` (1.2.10 **terdeteksi sebagai sasaran peningkatan** dari tiga versi lebih lama),
  `RemoveExistingProducts` jalan, ARP 1.2.10 hilang, ARP 1.2.13 satu-satunya entri Seruni.
- **393 berkas payload cocok persis** dengan manifes 1.2.13
  (`cocok=393 beda=0 hanya-terpasang=0 hanya-paket=0 total=393`), termasuk
  `Seruni22Bakery.Host.dll` (hash `d4d1bf68…`) dan bundel `index-BZfyiTsC.js` / `index-CUfTbA7B.css`.
- **Aset lama tersapu, dan itu diperiksa dari dua sisi**: sebelum peningkatan
  `wwwroot\assets` berisi tepat `index-B8lfbLcy.js` + `index-CoYZy02P.css` (aset 1.2.10, dibaca
  dari tabel `File` MSI 1.2.10), sesudah peningkatan hanya menyisakan dua berkas bundel 1.2.13.
- Penanda kanal HKLM ikut naik (`Channel=msi Version=1.2.13`) dan kedua pintasan
  (Start Menu umum + Desktop publik) menunjuk `C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.exe`.
- Data bertahan di atas basis data yang dibuat oleh 1.2.10: sandi hasil ganti masih bisa login
  di 1.2.13, `mustChangePassword=False`, produk terbaca `id=1 name=Roti Uji 1213 harga=15000`,
  merek sama, logo bisa dipasang ulang dan kembali sama persis.
- **Tidak ada migrasi baru**: periksa-diri 10 pemeriksaan 0 gagal, `terpasang 18, terkemas 18`,
  `migrasi dijalankan x0 (diharapkan 0)`, `uji.db` tetap ada. 1.2.10 memang sudah skema 18.

**Pagar kanal dan versi**

- EXE 1.2.13 menolak dipasang selama MSI 1.2.10 ada: exit 1, log Inno menyebut
  `Channel = msi`, tanpa folder `C:\SeruniExe1213`, tanpa entri ARP, tanpa penanda HKCU;
  MSI 1.2.10 tetap satu-satunya instalasi.
- MSI 1.2.10 (lama) ditolak di atas 1.2.13: exit 1603 dengan alasan turun versi; versi
  terpasang tetap 1.2.13.
- **EXE 1.2.10 yang asli ditolak di atas EXE 1.2.13**: exit 1 dan log memuat
  `Pagar versi … 1.2.13`. Ini naik derajat dari harness 1.2.12 → 1.2.13, yang ketika itu harus
  mengakali dengan menaikkan penanda `HKCU\Software\Seruni22Bakery\Version` ke `9.9.9` karena
  paket lama yang diuji belum punya pagar versi. 1.2.10 sudah memuat `BandingkanVersi` di
  `InitializeSetup` (diperiksa ke `build/installer/inno/setup.iss` pada tag `v1.2.10`), jadi
  skenarionya kini memakai berkas sungguhan; penanda HKCU tidak lagi ditulis paksa, dan
  setelah penolakan tercatat `EXE 1.2.13 tetap terpasang` + `Version=1.2.13`.
- MSI 1.2.13 ditolak selama EXE terpasang: exit 1603 dengan alasan kanal EXE; folder Program
  Files tidak terbentuk; EXE tetap satu-satunya instalasi.

**Jalur ESC/POS raw dari biner hasil peningkatan**

- Cetak uji dari host **hasil peningkatan 1.2.10 → 1.2.13** diterima emulator loopback port 9100:
  635 bita (`run1/escpos-diterima.bin`), diawali `ESC @`, diakhiri `ESC d 3`, nominal contoh
  `2 x Rp 15.000 … Rp 30.000` benar, `Rp 300` lama tidak muncul, `GS V` tidak dikirim saat
  cut=0, riwayat cetak mencatat `status=Printed kind=Test`, dan `mode=escpos-raw` +
  `tujuan=tcp://127.0.0.1:9100` bertahan setelah host dihentikan dan dijalankan ulang.
- lintas-jalur: tangkapan run ini 631 dari 635 bita **identik byte** dengan tangkapan peningkatan
  1.2.12 → 1.2.13 (2026-11-03). Empat bita yang berbeda semuanya cap waktu struk
  (`03 November 2026 18:06` → `04 November 2026 11:38`), jadi dua jalur peningkatan yang berbeda
  menghasilkan struk yang sama persis.

**Copot dan kanal EXE**

- Copot MSI 1.2.13: folder program, kedua pintasan umum, entri ARP, dan penanda HKLM hilang;
  `uji.db` + `uji.key` di luar folder program tetap utuh.
- EXE 1.2.13 dipasang per pengguna (exit 0): 393 berkas payload cocok (4 berkas tambahan milik
  Inno tidak dihitung sebagai beda), health 200 melaporkan 1.2.13, SPA memuat bundel
  `index-BZfyiTsC.js`, basis data **baru** menjalankan tepat 18 migrasi, periksa-diri 10/10.
- Copot EXE: folder, ARP, penanda HKCU hilang; folder data pengguna
  (`%LOCALAPPDATA%\Seruni22Bakery`) beserta `seruni22bakery.db` penanda tetap ada; tanpa proses yatim.

## Batas bukti

- Satu guest Windows 11 build 26100. **Windows 10 belum diuji.**
- Cetak lulus ke **emulator loopback**, bukan printer thermal 80 mm / printer jaringan fisik;
  laci uang dan pemotong kertas tidak diuji; pemindai barcode hanya simulasi wedge.
- Kedua paket **NotSigned**: SmartScreen/SmartCheck di PC nyata tetap risiko.
- Harness ini tidak menguji migrasi basis data (skema 18 → 18). Nilai uji data 1.2.10 di sini
  adalah "data yang sudah ada terbaca utuh oleh 1.2.13", bukan "skema lama naik ke 18".
- Yang diuji hanya peningkatan in-place; instalasi bersih 1.2.13 oleh pemilik di PC kasir tetap
  langkah terpisah.

## Berkas

- `run1/` — apa adanya dari folder bukti tamu: `status.txt` (105 baris + rekap), `validasi.log`
  (transkrip tamu, memuat baris yang hilang di `status.txt`), log installer MSI/Inno per tahap,
  log host, `escpos-diterima.bin`, `runner.log`, `launcher-out.txt`.
- `uji-1210-1213.ps1` / `run-sandbox.ps1` / `uji-jalan.wsb` — skrip yang benar-benar berjalan.
- `port-kit-1210.py` — skrip penyalinan harness 1.2.12 → 1.2.13 menjadi 1.2.10 → 1.2.13, dengan
  penjaga jumlah occurrence pada tiap penggantian.
- `KIT-SHA256SUMS.txt` — checksum seluruh berkas kit (paket, skrip, manifes).
- `hash-publish-1213.txt` — manifes 393 berkas payload 1.2.13 yang dipakai perbandingan byte.

Kit asli berada di `C:\tmp\kit-uji-1210-1213` pada mesin ini dan identik dengan salinan di folder
ini (diperiksa lewat `KIT-SHA256SUMS.txt`).
