# Verifikasi paket 1.2.9 — 2026-10-31

Dokumen ini mencatat apa yang **benar-benar dijalankan** terhadap paket 1.2.9, beserta
apa yang belum diuji. Setiap angka berasal dari log di `dist\build-logs\`, skrip uji di
`tests\Verify\` dan `docs\paket-bukti-2026-10-31\`, **kecuali** bagian uji
pasang/peningkatan (bagian "Pemasangan dan peningkatan 1.2.9"), yang bersumber dari
atestasi tertulis pemilik dan ditandai demikian.

## Identitas paket

| Hal | Nilai |
|---|---|
| Versi aplikasi | 1.2.9 (`src\Host.Service\BakeryDesktop.Host.Service.csproj`) |
| Skema basis data | 18 (`018_CostOfGoodsAndInventoryJournals.sql`) |
| Installer EXE | `dist\installer\Seruni22Bakery_Setup_v1.2.9.exe`, 42.662.274 bita |
| SHA-256 EXE | `fd233be4e4a8441dd2e63349b723466ee4ab803f41e6d6b357b97e05f55143b7` |
| Installer MSI | `dist\installer\Seruni22Bakery_v1.2.9_x64.msi`, 50.391.554 bita |
| SHA-256 MSI | `4cf092ce05cfb0301d2740cc136322135dc9f506a482c807bea020782052d635` |
| Tanda tangan | **NotSigned** untuk EXE dan MSI (tidak ada sertifikat; pembelian ditunda 2026-10-29) |
| Dibuat | 2026-10-31 07:04 (EXE) dan 07:06 (MSI) +07:00 |
| ProductCode MSI | `{7609C8A3-2349-4606-A02D-652FE121FB78}` (hanya berlaku untuk biner ini) |
| UpgradeCode MSI | `B975F792-6F80-4D56-8A64-D274A044DCF1` (tetap antar versi) |

Bundel frontend di payload hanya dua berkas: `index-BksMbgv0.js` (589.236 bita,
`1631fbcb…`) dan `index-CoYZy02P.css` (57.262 bita, `9a26e040…`). Bundel 1.2.7
tidak ikut.

## Isi paket

- Ekstraksi administratif MSI (`msiexec /a`) ke folder sementara menghasilkan
  **393 berkas**; seluruh SHA-256 dan ukurannya **identik 393/393** dengan
  `dist\publish-single`. Tabel `File` MSI berisi 393 baris.
- Berkas kunci: `Seruni22Bakery.Host.dll` `eee0641f…`, peluncur `Seruni22Bakery.exe`
  `4b3ab112…`, `wwwroot\index.html` `975b00e8…`.
- Versi berkas: host `1.2.9.0`, peluncur `1.2.9`.
- Tidak ada PDB, basis data, kunci JWT, atau `appsettings.Development.json` di paket.
- Inno mencatat 394 entri `Compressing` (393 payload + `app.ico`); kompilasi sukses
  dalam 44,5 detik.

Perintah yang dipakai:

```
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version 1.2.9
powershell -ExecutionPolicy Bypass -File scripts\build-msi.ps1 -Version 1.2.9
```

## Pagar yang terkandung di installer

**MSI** (`build\installer\wix\Package.wxs`) — dibaca dari tabel `LaunchCondition`
setelah MSI jadi:

1. `NOT WIX_DOWNGRADE_DETECTED` — menolak pemasangan versi lebih lama di atas versi
   lebih baru (pesan: "Versi yang lebih baru dari Seruni 22 Bakery sudah terpasang.").
2. `NOT (EXECHANNEL = "exe") AND NOT EXEINSTALLED` — menolak berjalan selama kanal
   EXE terpasang, baik yang menulis `HKCU\Software\Seruni22Bakery\Channel=exe`
   maupun yang hanya menyisakan entri Program dan Fitur
   `{5D9D507C-B7C6-4C4B-A6EE-6786A01F909A}_is1`.
3. Komponen `ChannelMarker` menulis `HKLM\Software\Seruni22Bakery`
   (`Channel=msi`, `Version=1.2.9`, `InstallDir`) agar installer EXE bisa mengenali
   kanal MSI.

**EXE** (`build\installer\inno\setup.iss`):

1. `InitializeSetup` memanggil `MsiTerpasang()`: memeriksa penanda
   `HKLM64\Software\Seruni22Bakery\Channel=msi` **dan** memindai entri Program dan
   Fitur bernama "Seruni 22 Bakery" pada HKLM64/HKLM32. Pemindaian kedua perlu karena
   MSI 1.2.7 dan lebih lama tidak menulis penanda `Channel`.
2. Versi yang sudah terpasang di `HKCU\Software\Seruni22Bakery\Version` dibandingkan
   dengan versi paket; versi lebih baru di mesin menolak paket yang lebih lama.
3. Semua `MsgBox` penolakan dibungkus `if not WizardSilent()`. Tanpa itu,
   `/SUPPRESSMSGBOXES` tidak menahan `MsgBox` buatan skrip dan pemasangan senyap
   menunggu klik selamanya.

Bukti di mesin pemilik (mesin ini sudah berisi MSI 1.2.7):

```
Seruni22Bakery_Setup_v1.2.9.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  /DIR=C:\tmp\seruni-exe-129 /LOG=C:\tmp\guard-129b.log
```

- Kode keluar **1**; pemasangan dibatalkan sebelum satu berkas pun ditulis.
- Folder `C:\tmp\seruni-exe-129` tidak terbentuk; tidak ada proses yang tertinggal.
- Log Inno mencatat:
  `Pagar kanal: entri Program dan Fitur MSI ditemukan di Uninstall\{A0AEA895-FC09-4312-8E24-8C9E0FEABDC8}.`
  lalu `InitializeSetup returned False; aborting.`
- Penanda `HKCU\...\Channel` tidak ditulis; instalasi MSI 1.2.7 tetap utuh.

## Perilaku biner yang dikemas

Empat skrip verifikasi API dijalankan terhadap host dari `dist\publish-single`
(biner yang sama dengan isi MSI, sudah dibuktikan identik), masing-masing pada basis
data sementara dan kunci JWT sementara:

| Skrip | Pemeriksaan | Gagal |
|---|---|---|
| `tests\Verify\verify-batch2.mjs` | 63 | 0 |
| `tests\Verify\verify-batch3.mjs` | 56 | 0 |
| `tests\Verify\verify-batch6.mjs` | 21 | 0 |
| `tests\Verify\verify-bug09.mjs` | 29 | 0 |
| **Jumlah** | **169** | **0** |

Ditambah pemeriksaan payload:

- `/health` menjawab `version 1.2.9` pada keempat host.
- Basis data baru menjalankan **18 migrasi** pada keempat host.
- Login bawaan, ganti sandi paksa, lalu `/api/system/self-check`: **9 pemeriksaan,
  0 gagal**, termasuk detail skema "terpasang 18, skema terbaru yang ikut terkemas 18".
- `/api/brand` anonim mengembalikan tepat **empat kunci**: `logo`, `name`, `outlet`,
  `tagline`.
- Tidak ada proses `Seruni22Bakery*` yang tertinggal setelah uji.

Skrip pagar satu perintah `scripts\verify.ps1` juga lulus **18 langkah**, termasuk
pemeriksaan bahwa `[Icons]`/`[Run]` tidak memakai `wscript`/`.vbs`, peluncur
dikompilasi dengan `csc.exe`, dan uji penerusan argumen peluncur (5 pemeriksaan).

Basis data uji selalu dibuat di folder sementara (`%TEMP%\seruni-verify-*` atau
`C:\tmp\payload-129`) dan dihapus lagi. Jalur basis data pemilik
(`%LOCALAPPDATA%\Seruni22Bakery`) tidak pernah dipakai oleh skrip mana pun di atas.

## Tes otomatis

`dotnet test` per proyek, seluruhnya lulus:

| Proyek | Lulus | Gagal |
|---|---|---|
| Domain | 83 | 0 |
| Application | 51 | 0 |
| Infrastructure | 142 | 0 |
| UI (Playwright) | 116 | 0 |
| **Jumlah** | **392** | **0** |

## Pemasangan dan peningkatan 1.2.9: atestasi pemilik, berkas belum diterima

Pada 2026-10-31 pemilik menjalankan kit uji di mesin ≥8 GB dan melaporkan secara
tertulis: `lulus=65 gagal=0 total=65`, yaitu seluruh 13 bagian lulus tanpa kegagalan.
Laporan itu **ketikan, bukan berkas**: `status.txt` dan `runner.log` tidak pernah
sampai ke mesin ini (share `192.168.1.20`/`192.168.1.50` tidak terjangkau, flashdisk
`F:` berisi media Ubuntu tanpa bukti, `C:\tmp\hasil-129` tidak pernah ada). Pemilik
kemudian menyatakan menerima atestasi tertulis sebagai dasar keputusan.

Atestasi ini dipakai sebagai dasar dengan batasan berikut, yang dicatat apa adanya:

- Angka `65` identik dengan hasil uji 1.2.7 (`lulus=65 gagal=0`), sehingga tidak bisa
  dibedakan dari laporan lama tanpa berkas asli.
- Klaim pertama yang masuk adalah `lulus=13 gagal=0`; angka itu sama dengan jumlah
  bagian, bukan jumlah pemeriksaan, dan tidak mungkin berasal dari rekap harness.
- Isi tiap baris (timestamp, hash MSI, 393 berkas, RAM tamu, bukti copot) tidak dapat
  diperiksa silang karena tidak ada berkas maupun teks barisnya.
- Dokumen ini tidak menyatakan berkas bukti diterima. Status mengikuti keputusan
  pemilik atas atestasi, bukan atas verifikasi log.

Percobaan uji pasang/peningkatan 1.2.9 di Windows Sandbox bersih **gagal dijalankan
pada mesin ini**, bukan gagal karena paketnya:

- Harness ada di `docs\paket-bukti-2026-10-31\uji-129\` (`uji-129.ps1`,
  `run-sandbox.ps1`, `PANDUAN-KIT.md`) dan mencakup: MSI 1.2.7 → 1.2.9 dengan data
  lama, penolakan EXE selama MSI terpasang, penolakan MSI selama EXE terpasang,
  penolakan versi lebih baru, serta copot yang tidak menghapus folder data pengguna.
- Kit siap salin untuk mesin ≥8 GB dirakit dengan
  `scripts\siapkan-kit-uji-129.ps1` (bawaan `C:\tmp\kit-uji-129`, 9 berkas,
  177,5 MB). Preflight kit (`run-sandbox.ps1 -Periksa`) di mesin ini: 8 checksum
  cocok, prasyarat terdeteksi, sandbox **tidak** diluncurkan.
- Kit dirakit ulang setelah pengerasan peluncur: penjaga tamu `WDAGUtilityAccount`,
  WAL tidak lagi dihapus, penghitung migrasi dibaca lewat `Baca-Teks`, seluruh skrip
  ASCII murni (PowerShell 5.1 membaca berkas tanpa BOM sebagai ANSI). Hasil periksa
  ulang: `sha256sum -c` 8/8 OK, kedua skrip `parse OK`, isi kit identik byte-per-byte
  dengan sumber di `docs\paket-bukti-2026-10-31\uji-129\`
  (`run-sandbox.ps1` `0a893965…`, `uji-129.ps1` `649cf5ed…`), preflight keluar 0.
- Pagar rekap: `uji-129.ps1` memanggil `Cek` lebih dari 80 kali, dan rekap hanya
  PASS bila `gagal=0` **dan** total pemeriksaan ≥ 60. Karena itu rekap dengan
  `lulus` belasan (mis. 13, jumlah bagian) bukan lulus — run seperti itu terpotong.
- Kontrol negatif di mesin ini: peluncur tanpa `-Periksa` berhenti dengan
  "BERHENTI: RAM host hanya 3746 MB", keluar 3, dan **tidak** membuat `uji-jalan.wsb`
  maupun menyentuh `bukti\`. Jadi pagar RAM bekerja sebelum ada efek samping.
- Pada percobaan 2026-10-31 11:41, Windows mencatat peringatan
  `Microsoft-Windows-Resource-Exhaustion-Detector` ID 2004: **kondisi memori virtual
  rendah**, dengan `vmmemWindowsSandbox` mengonsumsi 1.200 MB. Mesin ini hanya punya
  3.746 MB RAM; tamu sandbox 1.536 MB tidak pernah selesai boot dan tidak satu pun
  pemeriksaan tamu berjalan (`status.txt` tidak terbentuk).
- Sisa proses VM (`vmwp`, `vmmemWindowsSandbox`) tidak dapat dihentikan tanpa hak
  administrator; proses itu akan hilang setelah mesin dijalankan ulang.

Hasil pasang, peningkatan, dan copot 1.2.9 dengan demikian **bersandar pada atestasi
tertulis pemilik** (`lulus=65 gagal=0 total=65`), bukan pada berkas bukti yang dibaca
di mesin ini. Yang bisa dipastikan secara langsung di sini hanya: isi paket utuh,
perilaku biner payload lulus, dan pagar EXE terbukti menolak di mesin pemilik. Bila
berkas asli (`status.txt`, `runner.log`, folder `bukti\`) kelak sampai, isinya harus
dicocokkan dengan atestasi ini; sebelum itu, catatan uji ini tetap berstatus atestasi.

## Yang tetap belum dibuktikan dan tidak boleh diklaim

- Paket **belum ditandatangani** (`NotSigned`); sertifikat code signing belum dibeli.
- Pemasangan/peningkatan/copot **1.2.9** dilaporkan lulus oleh pemilik secara tertulis
  (`lulus=65 gagal=0 total=65`); berkas bukti mentah belum diterima di mesin ini.
- MSI 1.2.7 di mesin pemilik belum dicopot; aplikasi terpasang belum dibuka pada data
  pemilik (hanya berkas, versi, dan metadata basis data yang pernah diperiksa).
- Perangkat kasir fisik (printer thermal 80 mm, pemindai barcode USB, laci uang)
  belum diuji.
- Installer EXE versi lama (1.2.7/1.2.8) tidak memiliki pagar versi, sehingga installer
  lama itu sendiri masih bisa menurunkan instalasi yang sudah 1.2.9. Pagar 1.2.9 yang
  menolak versi lebih baru dilaporkan lulus pada bagian 12 atestasi, tetapi berkas
  lognya belum diperiksa di sini.
- Kompatibilitas belum diuji di semua versi Windows.

## Perbaikan yang termuat

Seluruh 31 temuan audit tahap kedua (2 Kritis, 3 Tinggi, 15 Sedang, 11 Rendah)
ditutup pada 1.2.9, mencakup: penyelesaian SPK yang tidak lagi mempercayai bahan dari
klien (BUG-004) dan menolak output nol (BUG-005); jurnal COGS saat penjualan
(BUG-001), transfer/opname (BUG-002), lokasi penerimaan (BUG-003), biaya retur
(BUG-009); perlindungan balapan pesanan/pembayaran (BUG-010), PO (BUG-011), poin
pelanggan (BUG-012); ringkasan HPP dan definisinya (BUG-006/007); pembulatan ekspor
(BUG-008); otorisasi dan paparan data (BUG-014–018, 021); integritas basis data dan
autentikasi (BUG-019/020/022/023); antarmuka dan perlindungan fetch (BUG-024–027);
serta installer/update (BUG-028–031). Skema naik ke 18 melalui migrasi 018.

## Putusan

**READY FOR LIMITED PILOT** untuk 1.2.9, dengan syarat yang tercatat:

- Dasar keputusan uji pasang/peningkatan/copot adalah **atestasi tertulis pemilik**,
  bukan berkas `status.txt`/`runner.log` yang dibaca di mesin ini.
- Paket **NotSigned**; sertifikat code signing belum ada.
- Perangkat kasir fisik belum diuji; kompatibilitas belum diuji di semua versi Windows.
- MSI 1.2.7 di mesin pemilik belum dicopot.

Putusan ini **bukan** READY FOR PRODUCTION. Naik ke READY FOR PRODUCTION menuntut
berkas bukti mentah yang dapat diperiksa, tanda tangan paket, dan uji perangkat kasir
fisik.
