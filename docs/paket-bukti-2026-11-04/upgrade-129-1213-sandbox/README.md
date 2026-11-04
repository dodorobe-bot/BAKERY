# Uji pasang & peningkatan 1.2.9 → 1.2.13 di Windows Sandbox (2026-11-04)

Hasil akhir: **PASS — 106 pemeriksaan lulus, 0 gagal** (`run1/status.txt`, rekap
`lulus=106 gagal=0 total=106`, baris `| FAIL |` = 0). Satu run saja; tidak ada run
pertama yang gagal lalu diperbaiki. Jumlah baris PASS individual (106) cocok persis
dengan rekap, jadi tidak ada pemeriksaan yang hilang dari berkas status.

Jalur ini adalah **jalur peningkatan tertua yang masih ada di lapangan**: 1.2.9 adalah
paket pilot yang benar-benar dipasang di PC kasir, dan 1.2.13 adalah paket yang akan
pasang sekarang. Dengan run ini, ketiga jalur yang ditahan dokumen verifikasi 1.2.13
(1.2.9 → 1.2.13, 1.2.10 → 1.2.13, 1.2.12 → 1.2.13) sudah terbukti.

## Lingkungan

- Windows Sandbox, tamu **Windows 11 Enterprise build 26100**, 1.535 MB, jaringan
  **dimatikan** (`<Networking>Disable</Networking>`).
- Pengguna tamu `WDAGUtilityAccount`, elevated. Tidak ada runtime .NET bersama
  (publish self-contained) — diperiksa sebagai baris pertama.
- Mesin host: Windows 11 build 26300, RAM 3.746 MB; commit bebas ±3,5 GB saat mulai.
  Host hanya membaca/menulis folder kit `C:\tmp\kit-uji-129-1213`; tidak ada installer
  yang dijalankan di host dan tidak ada basis data pemilik yang disentuh.
- Durasi: peluncur 12:00:52 → selesai 12:14:27 (±13,6 menit); boot tamu sampai baris
  pertama status ±111 detik.

## Paket yang diuji

| Berkas | Ukuran (bita) | SHA-256 | ProductCode |
|---|---|---|---|
| `Seruni22Bakery_v1.2.9_x64.msi` | 50.391.554 | `4cf092ce05cfb0301d2740cc136322135dc9f506a482c807bea020782052d635` | `{7609C8A3-2349-4606-A02D-652FE121FB78}` |
| `Seruni22Bakery_v1.2.13_x64.msi` | 50.416.132 | `eb3f40fb0fa2fe1fb7e09bee733db524a8b29fb55af367b1bd8d0df1f5afd132` | `{CE68DB95-1278-4892-B835-7F875F0DAB63}` |
| `Seruni22Bakery_Setup_v1.2.9.exe` | 42.662.274 | `fd233be4e4a8441dd2e63349b723466ee4ab803f41e6d6b357b97e05f55143b7` | — (Inno, AppId `{5D9D507C-…}_is1`) |
| `Seruni22Bakery_Setup_v1.2.13.exe` | 42.672.292 | `c1a0af29bd8f5bf9c439cadef3c1d8c891da0c49a3271384192fd682c444ed9d` | — (Inno, AppId `{5D9D507C-…}_is1`) |

Semua konstanta di atas **dibaca ulang dari disk** pada sesi ini (`Get-FileHash` atas
`dist\installer` dan tabel `Property`/`Upgrade`/`File`/`Component`/`Directory` milik MSI
1.2.9), bukan diambil dari dokumen lama. Nama aset web 1.2.9 (`index-BksMbgv0.js`,
`index-CoYZy02P.css`) juga diambil dari tabel `File` paket itu sendiri. UpgradeCode 1.2.9
`{B975F792-6F80-4D56-8A64-D274A044DCF1}` sama dengan 1.2.10–1.2.13, dan tabel `Upgrade`
1.2.9 memuat bentuk `WIX_UPGRADE_DETECTED` / `WIX_DOWNGRADE_DETECTED` yang identik, jadi
peningkatan in-place memang jalur yang didukung dari 1.2.9. Kedua paket 1.2.13 **NotSigned**.

## Yang dibuktikan

**Peningkatan MSI 1.2.9 → 1.2.13 dengan data utuh**

- 1.2.9 dipasang senyap (exit 0), host-nya menjawab `/health` dan melapor `version=1.2.9`;
  sandi admin diganti, produk `Roti Uji 1213` (Rp 15.000) dibuat, merek
  `Toko Uji Sandbox 1213` disetel lewat API 1.2.9 sendiri.
- Basis data 1.2.9 terbentuk dengan **18 migrasi** (`host-129.log` memuat
  `Migrasi 18 diterapkan.`), jadi skema 18 memang sudah dipakai paket pilot itu.
- Peningkatan senyap 1.2.13: exit 0; `msi-1213-upgrade.log` mencatat
  `PROPERTY CHANGE: Adding WIX_UPGRADE_DETECTED property. Its value is
  '{7609C8A3-2349-4606-A02D-652FE121FB78}'` dan `MIGRATE` dengan nilai yang sama,
  `RemoveExistingProducts` jalan, tanpa rollback; ARP 1.2.9 hilang dan ARP 1.2.13
  satu-satunya entri Seruni.
- **393 berkas payload cocok persis** dengan manifes (`cocok=393 beda=0
  hanya-terpasang=0 hanya-paket=0 total=393`), termasuk `Seruni22Bakery.Host.dll`
  (hash `d4d1bf68…`). Bundel 1.2.9 benar-benar tersapu: keduanya terbukti **ada sebelum**
  peningkatan (`aset=index-BksMbgv0.js, index-CoYZy02P.css`) dan sesudahnya hanya
  `index-BZfyiTsC.js` / `index-CUfTbA7B.css` yang tersisa.
- Data bertahan: sandi hasil ganti masih bisa login, `mustChangePassword=False`,
  produk dan merek terbaca utuh, `/api/brand` anonim tetap 4 kunci, logo bisa dipasang
  ulang dan kembali sama persis.
- **Tidak ada migrasi baru**: periksa-diri melaporkan skema 18 = skema terbaru terkemas,
  `migrasi dijalankan x0 (diharapkan 0)`, dan 10 pemeriksaan integritas 0 gagal.

**Pagar kanal dan versi**

- EXE 1.2.13 menolak dipasang selama MSI 1.2.9 ada: exit 1,
  `exe-1213-guard.log` mencatat `Pagar kanal: penanda HKLM Software\Seruni22Bakery\Channel = msi ditemukan.`;
  tanpa folder, tanpa ARP, tanpa penanda HKCU; MSI 1.2.9 tetap satu-satunya instalasi.
- MSI 1.2.9 (lama) ditolak di atas 1.2.13: exit 1603 dengan
  `WIX_DOWNGRADE_DETECTED = {CE68DB95-…}`, versi terpasang tetap 1.2.13.
- MSI 1.2.13 ditolak selama EXE terpasang: exit 1603, log menyebut
  `EXECHANNEL = exe` dan pesan `Instalasi Seruni 22 Bakery versi EXE masih terpasang untuk
  pengguna ini.`; tidak ada folder Program Files yang terbentuk.
- **EXE 1.2.9 yang asli** ditolak saat dipasang di atas EXE 1.2.13: exit 1,
  `exe-129-versi-guard.log` mencatat `Pagar versi: HKCU Version=1.2.13 lebih baru dari
  paket 1.2.9.` dan penanda HKCU tetap 1.2.13. Ini lebih keras daripada harness 1.2.12 →
  1.2.13 yang masih merekayasa penanda `HKCU Version=9.9.9`: `setup.iss` tag `v1.2.9`
  sudah memuat `BandingkanVersi` di `InitializeSetup`, jadi pagar versi diuji dengan
  paket lama sungguhan.

**Jalur ESC/POS raw dari biner hasil peningkatan**

- Cetak uji dari host **hasil peningkatan 1.2.9 → 1.2.13** diterima emulator loopback
  port 9100: 635 bita (`run1/escpos-diterima.bin`), diawali `ESC @`, diakhiri `ESC d 3`,
  nominal contoh `2 x Rp 15.000 … Rp 30.000` benar dan `Rp 300` lama tidak muncul lagi,
  `GS V` tidak dikirim saat cut=0, riwayat cetak mencatat `status=Printed kind=Test`,
  dan pilihan perangkat (`mode=escpos-raw`, `tujuan=tcp://127.0.0.1:9100`) bertahan
  setelah host dihentikan dan dijalankan ulang.
- **Korelasi lintas run (dihitung ulang dengan `cmp -l` atas berkas `.bin` tersimpan):**
  tangkapan ini berbeda **2 byte** dari tangkapan run 1.2.10 → 1.2.13 hari yang sama, dan
  **3 byte** dari tangkapan run 1.2.12 → 1.2.13 tanggal 3 November. Semua posisi yang beda
  (bita ke-321, 338, 340, 341) jatuh di dalam satu baris waktu struk:
  `04 November 2026 12:08` vs `04 November 2026 11:38` vs `03 November 2026 18:06`.
  Tiga jalur peningkatan berbeda menghasilkan struk yang sama kecuali cap waktunya.

**Copot**

- Copot MSI: folder program, pintasan Start Menu + Desktop publik, entri ARP, dan penanda
  HKLM hilang; `uji.db` dan `uji.key` di luar folder program tetap utuh.
- EXE 1.2.13 dipasang per pengguna (exit 0): 393 berkas payload cocok (4 berkas tambahan
  milik Inno), health 200 melapor 1.2.13, SPA memuat bundel 1.2.13, basis data baru
  menjalankan 18 migrasi, periksa-diri 10/10.
- Copot EXE: folder, pintasan, ARP, dan penanda HKCU hilang; folder data pengguna beserta
  `seruni22bakery.db` tetap ada; tidak ada proses yatim.

## Batas bukti

- Satu mesin, satu guest Windows 11. **Windows 10 belum diuji.**
- Cetak lulus ke **emulator loopback**, bukan printer thermal 80 mm fisik; pemindai barcode
  hanya simulasi wedge; laci uang dan pemotong kertas belum diuji pada perangkat nyata.
- Kedua paket 1.2.13 **NotSigned**, jadi SmartScreen masih merupakan risiko di PC nyata.
- Jalur 1.2.9 di sini adalah skema 18 → skema 18 (nol migrasi). Run ini **tidak**
  membuktikan peningkatan dari basis data skema lama (mis. 1.2.4/skema 17) langsung ke
  1.2.13; untuk itu yang ada baru bukti terpisah 14→15 dan 13→15 pada salinan DB nyata.
- Penanaman data 1.2.9 memakai endpoint yang sama dengan run 1.2.9 → 1.2.12 (login,
  change-password, `POST /api/products`, `PUT /api/settings`), yang sudah lulus di paket
  itu — jadi kompatibilitas API-nya ada presedennya, bukan asumsi.
- Riwayat jujur: run **pertama** jalur 1.2.9 → 1.2.12 (1 November) berakhir 100 lulus /
  5 gagal dan kelima kegagalan itu cacat harness (regex teks log, berkas dibaca sebelum
  host berhenti, lokasi pintasan Inno), bukan produk; perbaikannya sudah ikut terport ke
  skrip ini, dan run 1.2.9 di folder ini bersih sejak awal.

## Berkas

- `run1/` — apa adanya dari folder bukti tamu (23 berkas): `status.txt` (106 baris PASS +
  rekap), `validasi.log`, `msi-129-install.log`, `msi-1213-upgrade.log`,
  `msi-129-downgrade.log`, `msi-1213-uninstall.log`, `msi-1213-guard.log`,
  `exe-1213-guard.log`, `exe-129-versi-guard.log`, `exe-1213-install.log`,
  `exe-1213-uninstall.log`, `host-129.log`, `host-1213*.log`, `escpos-diterima.bin`,
  `runner.log`, `launcher-out.txt`, `selesai.txt`. Log MSI/Inno ber-Encoding UTF-16.
- `uji-129-1213.ps1` — skrip tamu 13 bagian (13 `Cek` dinamis + statis, 98 panggilan `Cek`).
- `run-sandbox.ps1` — peluncur host; `uji-jalan.wsb` — konfigurasi sandbox yang dihasilkan.
- `port-kit-129.py` — skrip port dari kit 1.2.10 → 1.2.13; setiap penggantian dijaga dengan
  jumlah occurrence dan menolak jalan bila paket di disk tidak cocok dengan hash yang diharapkan.
- `hash-publish-1213.txt` (manifes 393 berkas), `KIT-SHA256SUMS.txt` (checksum kit),
  `PANDUAN-KIT.md` (panduan jalannya uji).
