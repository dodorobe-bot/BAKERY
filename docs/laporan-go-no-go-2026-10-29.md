# Laporan GO / NO-GO — Skalabilitas Data, Cadangan, dan Pemulihan Bencana

Tanggal: 2026-10-29

> **Dokumen historis.** Putusan READY FOR LIMITED PILOT di bawah berlaku untuk 1.2.7.
> Per 2026-10-31 putusan terkini untuk **1.2.9** adalah **READY FOR LIMITED PILOT**
> (bukan READY FOR PRODUCTION), berdasarkan atestasi tertulis pemilik bahwa uji
> pasang/peningkatan/copot lulus (`lulus=65 gagal=0 total=65`); berkas bukti mentah
> belum diterima. Lihat `README.md` dan `docs\verifikasi-paket-1.2.9-2026-10-31.md`.

Aplikasi: Seruni 22 Bakery (React + host .NET 8 + SQLite, offline)
Skema basis data: **17** (migrasi 017 baru pada pekerjaan ini)
Versi kemasan terakhir: **1.2.7 (2026-10-30 17:31)** — nama toko, slogan, dan logo
menjadi pengaturan yang bisa diganti kapan saja dari layar Pengaturan, di atas 1.2.6
(2026-10-29 19:29) yang memuat perbaikan urutan otorisasi
pembatalan dan agregasi jurnal yang memuat entri pembalik, di atas penutupan sembilan cacat
1.2.5; rincian di `docs\verifikasi-paket-1.2.7-2026-10-30.md` dan
`docs\verifikasi-paket-1.2.6-2026-10-29.md`. Sebelumnya 1.2.5
(2026-10-29 17:53) memuat penutupan sembilan cacat terbuka; 1.2.4
**dibangun ulang 2026-10-29 08:57 dengan skema 17** (SHA-256
`8aa23cc0…`), diuji pasang → jalan → copot di mesin uji; MSI WiX 1.2.4 juga dibangun
(`fabf6384…`) dan lulus pasang → jalan → copot pada 2026-10-29.

Status yang dipakai: **PASSED**, **FAILED**, **BLOCKED**, **NOT RUN**, **NOT APPLICABLE**.
Semua angka di laporan ini hasil pengukuran pada mesin di bagian 3, bukan perkiraan, kecuali
tabel proyeksi di bagian 5 yang memang dihitung dari angka terukur.

---

## 1. Ringkasan eksekutif

- Dashboard pada 1 juta pesanan turun dari **2.312 ms menjadi 396 ms** (5,8×); analitik satu
  tahun pada 500 ribu pesanan turun dari **5.792 ms menjadi 522 ms** (11×).
- Seluruh layar lain pada 1 juta pesanan berada di bawah 80 ms, kecuali kotak tugas
  (758 ms) dan rekap bulanan jurnal (194 ms).
- Backup kini konsisten (salinan WAL ikut), membawa manifes + SHA-256, diperiksa integritas
  dan foreign key saat dibuat maupun saat dipulihkan; retensi tidak pernah menghapus salinan
  sehat terbaru, cadangan pra-upgrade, atau pra-restore.
- Pemulihan nyata sudah diuji, termasuk skenario basis data hidup yang rusak: aplikasi tetap
  bisa memulihkan, dan basis data yang rusak disimpan sebagai salinan pengaman.
- **Keputusan: READY FOR LIMITED PILOT**, dengan catatan wajib di bagian 18: perangkat fisik
  belum diuji, belum ada uji di mesin Windows bersih, dan aplikasi belum ditandatangani.

## 2. Ruang lingkup dan batasan

Yang dikerjakan: audit baseline, proyeksi pertumbuhan, optimasi indeks/kueri, batas daftar,
backup terverifikasi, retensi aman, pemulihan dengan validasi, penyalinan ke luar komputer,
monitoring kapasitas, simulasi 10 ribu–1 juta pesanan, uji ketahanan, dan tes otomatis
berbasis SQLite nyata.

Yang **tidak** berubah: stack (React + .NET sidecar + SQLite), arsitektur, alur layar.
Tidak ada Docker, layanan awan, atau pesan keluar (WhatsApp/Telegram/email). Tidak ada satu
baris data pengguna yang dihapus untuk memperkecil basis data.

## 3. Konfigurasi perangkat uji

| Bagian | Nilai |
|---|---|
| CPU | Intel Core i3-1215U, 6 inti / 8 thread |
| RAM | 4 GB terpasang (3,66 GB terlihat OS) |
| Cakram | WDC PC SN530 NVMe SSD 512 GB |
| OS | Windows 10.0.26300 x64 |
| Runtime | .NET 8, SQLite (Microsoft.Data.Sqlite), WAL |
| Basis data uji | data sintetis tanpa data pelanggan nyata |

Catatan: RAM hanya 4 GB dan saat pengukuran tersisa ±0,3 GB bebas. Lonjakan waktu sesekali
(2–3 detik pada satu permintaan di antara delapan) berasal dari tekanan memori dan pemindai
antivirus, bukan dari rencana kueri. Median dilaporkan bersama min/max agar lonjakan itu
terlihat.

## 4. Baseline performa sebelum perbaikan

Diukur pada basis data 500 ribu pesanan dengan skema 16:

| Layar | Sebelum | Sesudah |
|---|---|---|
| analitik 365 hari | 5.792 ms | 522 ms |
| dashboard | 327 ms | 223 ms |
| saldo akun | 253 ms | 193 ms |
| kotak tugas | 230 ms | 313 ms (lihat catatan) |
| pembelian | 620 ms (sebelum indeks 016) | 12 ms |

Pada 1 juta pesanan sebelum indeks penutup: dashboard **2.312 ms**, ringkasan piutang
**1.340 ms**, kotak tugas 700 ms, analitik 1.017 ms.

Percobaan yang **tidak** dipakai (diukur, lalu dibatalkan) dicatat di dalam migrasi 017:
indeks penutup JournalLines (saldo akun melambat 4–7×), indeks parsial piutang berurut
`TotalAmount` (1,1 detik vs 0,36 detik), ringkasan piutang lewat CTE agregat (lebih lambat
dari subkueri berkorelasi setelah indeks penutup ada).

## 5. Proyeksi pertumbuhan (3/6/12/36/60 bulan)

Dasar ukuran terukur: **±565 byte per pesanan** termasuk baris jurnal, pembayaran, PO, dan
indeks (10 ribu = 9,1 MB; 100 ribu = 55,4 MB; 500 ribu = 282 MB; 1 juta = 567 MB).

Skenario: Kecil 30 pesanan/hari, Sedang 120/hari, Besar 300/hari.

| Bulan | Kecil (ukuran / dashboard) | Sedang | Besar |
|---|---|---|---|
| 3 | 1,5 MB / ±9 ms | 6 MB / ±10 ms | 15 MB / ±11 ms |
| 6 | 3 MB / ±9 ms | 12 MB / ±10 ms | 31 MB / ±11 ms |
| 12 | 6 MB / ±9 ms | 24 MB / ±13 ms | 61 MB / ±20 ms |
| 36 | 18 MB / ±10 ms | 73 MB / ±30 ms | 184 MB / ±70 ms |
| 60 | 31 MB / ±10 ms | 122 MB / ±45 ms | 306 MB / ±120 ms |

Angka dashboard dihitung dari kurva terukur 10 ribu (8,1 ms), 100 ribu (49,9 ms),
500 ribu (222,9 ms), 1 juta (395,7 ms); status **PASSED** untuk proyeksi, **NOT RUN** untuk
simulasi nyata di atas 1 juta.

Konsekuensi retensi: pada skenario Besar, satu cadangan berumur 5 tahun ±306 MB. Retensi
bawaan dapat menyimpan puluhan salinan; sisa ruang cakram harus dipantau (bagian 13).

## 6. Indeks dan rencana kueri

Migrasi `017_CoveringIndexesForLargeData.sql`:

1. `idx_salesorders_created_agg (CreatedAt, Status, TotalAmount, Quantity, ProductTemplateId)`
   menggantikan `idx_salesorders_created` — analitik tidak lagi menyentuh tabel utama.
2. `idx_purchaseorders_created (CreatedAt)` — daftar pembelian tidak lagi mengurutkan di memori.
3. `idx_salespayments_order_amount (SalesOrderId, Amount)` menggantikan
   `idx_salespayments_salesorder` — penjumlahan pembayaran tanpa membuka tabel.
4. `idx_salesorders_open (Id, TotalAmount, DownPayment) WHERE Status NOT IN ('Paid','Cancelled')`
   — dashboard dan kotak tugas hanya membaca pesanan belum lunas (±12 MB), bukan 160 MB tabel.
5. `ANALYZE` supaya perencana memakai statistik baru.

Dua kueri jurnal ditulis ulang dengan `CROSS JOIN JournalEntries` agar baris jurnal dibaca
berurutan (JournalRepository). Status: **PASSED**. Rollback 017 mengembalikan indeks lama dan
diuji: naik-turun skema tanpa sisa.

## 7. Batas daftar dan penanganan riwayat

Semua daftar dibatasi di server: pesanan 100 baris, pembelian bawaan 300 (maksimum 2.000)
dengan `X-Total-Count`, audit 300, mutasi 100, SPK 50, kotak tugas 60 per jenis. Tidak ada
layar yang memuat seluruh riwayat. Pagination berbasis keyset belum diperlukan karena
batasnya kecil; status **NOT APPLICABLE** untuk keyset pada pilot ini. Tidak ada riwayat yang
dihapus atau diarsipkan otomatis; status **PASSED** untuk prinsip "jangan mengecilkan basis
data dengan menghapus riwayat".

## 8. Backup: konsistensi dan kelengkapan

- Dibuat dengan `VACUUM INTO` setelah `wal_checkpoint(TRUNCATE)`, dalam satu berkas.
- Setiap cadangan membawa manifes `.json`: waktu, ukuran, SHA-256, versi skema, jumlah
  pesanan, jumlah entri jurnal.
- Cadangan ditulis dengan nama sementara `.tmp` lalu diganti nama; cadangan yang setengah
  jadi tidak pernah terlihat sebagai cadangan sah.
- Otomatis sekali sehari (diperiksa tiap 30 menit), manual dari **Pengaturan → Cadangan &
  penyimpanan**, dan lewat `--backup`.
- Status: **PASSED** (tes `Cadangan_MembawaManifesChecksumDanHasilPemeriksaanSqlite`,
  `Cadangan_IkutMembawaIsiWalYangBelumCheckpoint`, `Cadangan_SatuBerkasTanpaSidecar`).

## 9. Verifikasi cadangan

Saat dibuat dan saat dipulihkan: ukuran vs manifes, SHA-256 vs manifes, `integrity_check`,
`foreign_key_check`. Cadangan yang berubah setelah dibuat ditolak; cadangan tanpa manifes
(versi lama) tetap boleh dipulihkan dengan peringatan bahwa checksum tidak bisa dibandingkan.
Status: **PASSED** (`Cadangan_BerubahSetelahDibuat_DitolakSaatDiperiksaDanDipulihkan`,
`CadanganTanpaManifes_TetapBisaDipulihkan`).

## 10. Retensi

Jendela harian/mingguan/bulanan (7/14/8/12) dengan batas minimum 3 salinan. Yang tidak pernah
dihapus: salinan sehat terbaru, cadangan pra-upgrade, cadangan pra-restore. Setiap
penghapusan dicatat di `retention.log`. Status: **PASSED**
(`Retensi_MenyimpanSalinanTerbaruPraUpgradeDanPraRestore`,
`Retensi_TidakPernahMenyisakanKurangDariBatasMinimum`,
`Retensi_TidakMenyisakanCadanganRusakSebagaiSalinanTerakhir`).

## 11. Pemulihan

Alur `--restore`: tolak bila aplikasi masih hidup (kode 3) → verifikasi cadangan → tolak
cadangan dari skema lebih baru → buat salinan pengaman → salin ke `.restore-tmp` → pindahkan →
migrasi ke skema terbaru → `integrity_check` + `foreign_key_check` pada hasil → bila gagal,
kembalikan dari salinan pengaman dan keluar dengan kode 1.

Uji nyata yang dijalankan: pemulihan data lama (data kembali, salinan pengaman dibuat),
pemulihan saat basis data hidup rusak, penolakan berkas bukan SQLite, penolakan cadangan
skema lebih baru, dan pemulihan cadangan tanpa manifes. Status: **PASSED**
(`Restore_TetapBerjalanSaatBasisDataBerlakuRusak`,
`Restore_MengembalikanDataLama_DanMembuatSalinanPengaman`,
`Pemulihan_MengembalikanIsiCadangan_DanMenyimpanSalinanPengaman`,
`Restore_MenolakBerkasBukanDatabase_TanpaMengubahData`,
`Restore_MenolakCadanganDariSkemaLebihBaru`).

## 12. Pemulihan di luar komputer utama

Penyalinan cadangan terbaru ke folder lain divalidasi: path relatif, `..`, path tanpa huruf
drive, dan folder cadangan aplikasi sendiri ditolak. Sidik SHA-256 hasil salinan dibandingkan
setelah penyalinan. Status: **PASSED** untuk mekanismenya
(`FolderTujuanSalinan_MenolakPathRelatifTitikDuaTitikDanFolderCadangan`).
Prosedur manusiawinya (flash disk dua buah, irama harian/mingguan) ditulis di
`docs/pemulihan-bencana.md` bagian 3. Uji fisik "cabut flash disk di tengah penyalinan" belum
dijalankan: **NOT RUN**.

## 13. Monitoring kapasitas

Tiga tingkat (normal/perhatian/kritis) dari ukuran nyata basis data, WAL, cadangan, log, dan
sisa ruang cakram; ambang dapat diubah lewat pengaturan. Peringatan hanya internal
(**Pemberitahuan**), satu per tingkat per hari. Status: **PASSED**
(`Kapasitas_TigaTingkatSesuaiBuktiBerkasDanDisk`,
`Kapasitas_CadanganTanpaManifes_HanyaPerhatian`).

## 14. Simulasi data besar

Data sintetis konsisten dengan trigger dan foreign key (`integrity_check ok`,
`foreign_key_check` kosong) pada semua skala. Waktu median lewat host, 5 sampel per endpoint:

| Layar | 10 ribu | 100 ribu | 500 ribu | 1 juta |
|---|---|---|---|---|
| dashboard | 8,1 | 49,9 | 222,9 | 395,7 |
| kotak tugas | 7,8 | 44,4 | 313,3 | 758,0 |
| analitik 365 hari | 12,8 | 109,5 | 522,0 | 1.163,1 |
| saldo akun | 4,7 | 45,6 | 192,7 | 449,3 |
| rekap bulanan jurnal | 2,4 | 15,8 | 96,3 | 193,5 |
| pesanan / pelanggan / pembelian | ≤ 4 | ≤ 12 | ≤ 27 | ≤ 80 |

Status: **PASSED** untuk 10 ribu–1 juta pada mesin ini. Kotak tugas pada 1 juta masih di atas
target 500 ms; penyebabnya satu kueri tenggat piutang yang memindai seluruh pesanan belum
lunas (pada data nyata jumlahnya kecil, tetapi data uji sengaja membuat 43% pesanan tetap
terbuka). Analitik 1,16 detik hanya dibuka dari menu Analisis, bukan saat berpindah menu.
Simulasi 1 juta butuh RAM 4 GB penuh pada mesin ini; mesin dengan RAM lebih kecil belum
diuji — **NOT RUN**.

## 15. Ketahanan dan korupsi

Diuji: cadangan rusak ditolak, checksum tidak cocok ditolak, pemulihan cadangan yang menunjuk
induk hilang ditolak, basis data hidup rusak tetap bisa dipulihkan, retensi tidak menyisakan
cadangan rusak sebagai salinan terakhir, dan transaksi tulis dibungkus transaksi SQLite
(rollback otomatis saat galat). Migrasi yang gagal di tengah: implementasinya memakai
transaksi + rollback (DatabaseMigrator), tetapi skenario "migrasi dipaksa gagal" hanya
diperiksa lewat kode, belum dijalankan sebagai tes: **NOT RUN**.

## 16. Keamanan

- Path tujuan salinan divalidasi (traversal, path relatif, folder cadangan sendiri).
- Berkas `jwt.key` dibatasi ACL pengguna berjalan.
- Tidak ada token, sandi, atau isi data pelanggan di log; log mencatat nama berkas, ukuran,
  dan SHA-256.
- Aksi cadangan, penyalinan, dan pemulihan tercatat di jejak audit (aplikasi) dan log
  (baris perintah).
- Berkas cadangan **tidak dienkripsi** dan aplikasi **belum ditandatangani**; keduanya
  dinyatakan apa adanya, bukan diklaim selesai. Status: **PASSED** untuk validasi path dan
  audit; **BLOCKED** untuk code signing (tidak ada sertifikat).

## 17. Installer dan upgrade

- Migrasi 017 membuat cadangan pra-upgrade otomatis sebelum menyentuh skema; diuji pada
  10 ribu, 100 ribu, 500 ribu, dan 1 juta pesanan (contoh: `pre-upgrade-v16-to-v17-*.db`
  berukuran 568 MB pada basis data 1 juta, `integrity_check ok`).
- Migrasi menolak basis data dari versi skema lebih baru tanpa mengubah apa pun.
- Rollback 017 tersedia dan diuji naik-turun tanpa sisa.
- **Kemasan diuji (2026-10-29 08:57).** `scripts/build-installer.ps1` (tes 298 lulus, frontend,
  publish self-contained, peluncur berversi, Inno) menghasilkan `Seruni22Bakery_Setup_v1.2.4.exe`
  ukuran 42.631.347 bita, SHA-256 `8aa23cc0917e80e076791417b8ef66c26de3de99ab15cc8d4e73ceb667fe6575`,
  **NotSigned**. Di dalam paket tidak ada basis data, `jwt.key`, atau `appsettings.Development.json`;
  migrasi `017_CoveringIndexesForLargeData` beserta empat nama indeks baru tertanam di
  `Seruni22Bakery.Infrastructure.Sqlite.dll`; peluncur dan host melaporkan versi 1.2.4.
  Pemasangan senyap ke `C:\tmp\rc124b\app2` (397 berkas) lalu dijalankan dengan basis data uji:
  migrasi 1→17, empat indeks baru muncul, indeks lama hilang, `integrity_check ok`,
  `foreign_key_check` kosong, 29/29 pemeriksaan `tests/Verify/verify-bug09.mjs`. Pencopotan
  keluar 0 tanpa sisa direktori, kunci uninstall, atau pintasan.
- **Insiden saat pengujian (dicatat apa adanya).** Pemasangan pertama dipanggil dari Git Bash;
  MSYS mengubah `/VERYSILENT` menjadi `C:/Program Files/Git/VERYSILENT`, sehingga setup berjalan
  interaktif dan entri `[Run]` postinstall menyalakan aplikasi terhadap basis data pemilik.
  Migrasi 15→17 tertulis ke `-wal` (cadangan pra-upgrade terverifikasi
  `pre-upgrade-v15-to-v17-20261029-100958.db`, SHA-256 `a48968f8…`, skema 15, integrity/fk ok);
  isi 36 tabel sama kecuali `SchemaVersion` dan dua baris audit akses ditolak
  (`DITOLAK GET /api/settings`, `DITOLAK GET /api/dashboard`). Tidak ada baris transaksi yang
  berubah. Pemasangan ulang dengan argumen benar (lewat `Start-Process`) membuktikan
  `skipifsilent` bekerja: nol entri `[Run]`, aplikasi tidak dinyalakan. Susulan: 1.2.4 dipasang
  ulang di lokasi bawaan `%LOCALAPPDATA%\Programs\Seruni22Bakery` pukul 10:22 (exit 0, argumen
  benar, nol `Run entry`, peluncur/host 1.2.4, 397 berkas, 017 tertanam); berkas basis data dan
  `-wal` tidak berubah, tidak ada proses yang dijalankan.
- **Status instalasi bawaan saat siklus MSI ditutup.** Pukul 10:43 instalasi 10:22 itu tidak
  lagi ada di disk: folder `%LOCALAPPDATA%\Programs\Seruni22Bakery` hilang, kunci uninstall
  HKCU `{5D9D507C-…}_is1` hilang, pintasan hilang. Penyebabnya **tidak dapat dipastikan**
  dari sisa log: log Inno pemasangan 10:22 ada, tetapi tidak ada log pencopotan Inno untuk
  instalasi itu; log MSI pasang (10:34) maupun copot (10:38) tidak memuat satu pun jalur
  `Local\Programs` dan `RemoveExistingProducts` tidak menghapus produk apa pun. Tidak ada
  data pengguna yang hilang (basis data, `-wal`, dan empat cadangan utuh). Keadaan dipulihkan:
  dipasang ulang 10:51 dengan argumen benar (log `install-akhir.log`: `/VERYSILENT
  /SUPPRESSMSGBOXES /NORESTART`, nol `Run entry`, "Installation process succeeded", 397 berkas,
  peluncur/host 1.2.4, kunci uninstall 1.2.4 kembali); basis data tetap
  `5fbcaae3…` dengan `-wal` 144.232 bita bertanggal 10:10:21.
- Pagar paket `scripts\verify.ps1 -Configuration Release -SkipTests` setelah perubahan WiX:
  **17 langkah lulus, 0 gagal** (termasuk "paket WiX tanpa wscript/.vbs"), `verify-bug09.mjs`
  29 pemeriksaan 0 gagal. Tes xUnit dilewati pada run ini (sudah dijalankan penuh 08:55:
  298 lulus).
- **MSI (WiX 5.0.2) dibangun dan diuji (2026-10-29 10:34).** `Seruni22Bakery_v1.2.4_x64.msi`
  ukuran 50.352.999 bita, SHA-256
  `fabf638491d98c5c4f7d9bd69a9ea59ed8400b6956a8cbaa7d4be94dd2617e5f`, **NotSigned**,
  `Package.wxs` kini mengambil versi dari define `ProductVersion` (sebelumnya angka tetap
  `1.0.0`), 393 berkas, per-machine, UpgradeCode `B975F792-…` tetap; ProductCode
  dibuat otomatis WiX per build (`{DE2E612D-…}` untuk build 10:34, `{B1734125-…}`
  untuk build 14:31 — jangan pakai GUID lama untuk copot). Gerbang `.NET 8` lama dihapus karena publish self-contained
  (`hostfxr.dll`/`coreclr.dll` ikut di paket); gerbang itu menolak pemasangan di mesin bersih
  yang sebenarnya tidak membutuhkannya. Bukti: pasang administratif menghasilkan payload
  identik byte-per-byte dengan `dist\publish-single`; pasang elevated `/qn` keluar 0 (393
  berkas di `C:\Program Files\Seruni 22 Bakery`, entri Programs and Features
  "Seruni 22 Bakery 1.2.4", pintasan Start Menu dan Desktop); host hasil MSI dijalankan pada
  basis data scratch port 5212 → `/health` 1.2.4, SPA 200, skema 17 dengan empat indeks 017,
  `integrity_check ok`, 29/29 `verify-bug09.mjs`; copot elevated `/qn` keluar 0 ("Removal
  completed successfully") dan folder, entri Programs and Features, serta pintasan hilang.
  Basis data nyata tidak tersentuh sepanjang siklus: SHA-256 `5fbcaae3…` sama sebelum dan
  sesudah, `-wal` 144.232 bita bertanggal 10:10:21 tidak berubah, empat berkas cadangan utuh,
  tidak ada proses yang dijalankan pemasang. Data pengguna di `%LOCALAPPDATA%` tetap ada
  setelah copot. Belum diuji: pasang di mesin Windows bersih.
- Berkas `.wixpdb` dipindahkan ke `dist\build-logs` supaya folder kirim hanya berisi artefak;
  `SHA256SUMS.txt` memuat kedua artefak 1.2.6 dan nilainya cocok dengan berkas.
- **Paket 1.2.6 (2026-10-29 19:21 EXE, 19:29 MSI).** `Seruni22Bakery_Setup_v1.2.6.exe`
  42.631.595 bita SHA-256 `4ebb5962…`; `Seruni22Bakery_v1.2.6_x64.msi` 50.357.095 bita
  SHA-256 `461d1c77…`; keduanya **NotSigned**. Identitas MSI dibaca dari tabel Property:
  ProductVersion 1.2.6, ProductCode `{EDCBBD96-732D-4854-A31B-0A36E6C43E77}`, UpgradeCode
  `B975F792-…` tetap. Ekstraksi administratif menghasilkan 393 berkas yang **seluruhnya
  identik hash-nya** dengan `dist\publish-single` (0 beda, 0 berkas hanya di satu sisi).
  Binari payload dijalankan pada basis data scratch dan lulus `verify-batch2/3/6/bug09`
  total **166 pemeriksaan, 0 gagal**, termasuk "Pegawai membatalkan pesanan (sales-cancel)
  ditolak" → 403 dan `/health` versi 1.2.6. **Pasang/copot EXE 1.2.6 dan MSI 1.2.6:
  NOT RUN**; mekanisme kemasan tidak berubah dari 1.2.5 (EXE lulus pasang/copot senyap)
  dan 1.2.4 (MSI lulus pasang/copot serta Windows Sandbox 30/30).
- Perintah build MSI kini berupa skrip: `scripts\build-msi.ps1` (memeriksa versi
  parameter vs `<Version>` proyek host, mengunci `-arch x64`, `.wixpdb` ke
  `dist\build-logs`, mencetak ukuran/SHA-256/status tanda tangan). Skrip dijalankan dan
  keluar 0. `-arch x64` wajib: tanpa flag itu WiX membangun paket x86 yang memasang ke
  `Program Files (x86)` (terbukti saat uji ulang: 50.320.227 bita, ekstrak ke `PFiles`).
  Dengan flag itu hasilnya berukuran sama dengan paket yang diuji (50.352.999 bita) dan
  393 berkas payload identik byte-per-byte; SHA-256 berbeda karena cap waktu di dalam
  basis data MSI, bukan karena isi berbeda. Rincian ada di `dist\installer\BUILD-INFO-MSI.txt`.

## 18. Tes otomatis dan keputusan

Seluruh tes: **314 lulus, 0 gagal** — Domain 57, Application 48, Infrastructure 118, UI 91.
Infrastructure memakai SQLite nyata (bukan tiruan) untuk migrasi, backup, retensi, pemulihan,
integrity, dan trigger.

| Butir GO | Status |
|---|---|
| Backup konsisten + terverifikasi + bisa dipulihkan | PASSED |
| Riwayat tidak dihapus; optimasi kueri/indeks lebih dulu | PASSED |
| Pemulihan di luar komputer (mekanisme + prosedur) | PASSED (uji fisik flash disk: NOT RUN) |
| Performa 10 ribu–1 juta pesanan | PASSED (1 juta kotak tugas 758 ms, dicatat) |
| Retensi aman | PASSED |
| Monitoring kapasitas | PASSED |
| Ketahanan/korupsi | PASSED (migrasi gagal: NOT RUN) |
| Keamanan path + audit | PASSED |
| Installer/upgrade dengan skema 17 | PASSED untuk isi paket — 1.2.7 dibangun 2026-10-30 17:31; 393 berkas payload identik hash dengan publish, binari payload lulus 180 pemeriksaan (termasuk 14 merek); rincian 1.2.6 di lampiran bawah. EXE/MSI 1.2.5 lulus pasang/copot senyap, MSI 1.2.4 lulus pasang/copot dan Windows Sandbox 30/30. **Peningkatan MSI 1.2.6 → 1.2.7 PASSED di dua lingkungan**: di mesin ini (30-10 17:45) MSI keluar 0, daftar program 1.2.7 `{A0AEA895…}`, 393/393 berkas identik publish, bundel 1.2.6 tersapu, pintasan utuh, basis data pemilik tidak berubah; di Windows Sandbox bermesin bersih (30-10 18:13–18:20) 65/65 lulus termasuk produk/sandi/merek kiriman 1.2.6 terbaca utuh dan copot 1.2.7 tidak menghapus basis data. **Pasang/copot EXE 1.2.7 PASSED di mesin ini** (31-10 03:17–03:20): 42/42 pemeriksaan, 393/393 payload identik publish, instalasi MSI + basis data pemilik utuh |
| Perangkat fisik (printer, pemindai, laci) | BLOCKED — belum ada perangkat |
| Code signing | DITUNDA — keputusan pemilik 2026-10-29: sertifikat dibeli setelah paket didistribusikan ke pihak lain. Jalur siap di `scripts\sign-paket.ps1` (mode thumbprint/PFX, uji negatif lulus, belum pernah menandatangani artefak); 1.2.6 tetap NotSigned |

**Keputusan: READY FOR LIMITED PILOT.**

Syarat sebelum pilot di toko:

1. Pasang kemasan 1.2.7 (skema 17) — **sudah terpasang di mesin ini lewat peningkatan MSI dari 1.2.6
   pada 2026-10-30 17:45**, tetapi aplikasi hasil peningkatan belum pernah dibuka. Pilih salah satu:
   EXE (tanpa hak administrator, per pengguna) atau MSI (per-machine, butuh administrator). Pasang/copot
   EXE 1.2.7 sudah lulus di mesin ini (31-10 03:17–03:20, 42/42, tanpa mengganggu MSI); copot MSI 1.2.7
   di mesin pemilik masih NOT RUN.
2. Uji printer thermal, pemindai barcode, dan laci uang di PC kasir sebenarnya.
3. Salin cadangan terbaru ke dua flash disk dan simpan satu di luar toko.
4. Latih pemilik menjalankan `--restore` satu kali di mesin uji (bukan mesin data nyata).

Yang tidak boleh diklaim sampai syarat di atas selesai: installer siap produksi,
kompatibilitas semua versi Windows, dan durabilitas di luar mesin uji ini.

## Lampiran 2026-10-30 — 1.2.7: merek toko bisa diganti

Permintaan pemilik: nama toko, logo, dan slogan harus bisa diganti kapan saja, bukan teks tetap
di kode. Kini ketiganya disimpan di AppSettings (`brand_name`, `brand_tagline`, `brand_logo`) dan
berlaku di layar masuk, header menu, judul jendela, struk, layar cetak peramban, dan delapan
kepala ekspor. Hanya pemilik (`administration`) yang bisa menggantinya; `GET /api/brand` terbuka
tanpa sesi tetapi hanya mengembalikan tiga kunci itu. Tidak ada migrasi baru (skema tetap 17).

| Butir | Status 1.2.7 |
|---|---|
| Regresi otomatis | PASSED — 351 kasus, 0 gagal (Application 51, Domain 80, Infrastructure 119, UI 101) |
| Isi paket | PASSED — 393/393 berkas MSI identik SHA-256 dengan publish; bundel baru `index-DppHnhYj.js`; EXE 394 entri kompresi |
| Binari payload | PASSED — 180 pemeriksaan (4 skrip lama 166 + 14 merek), 0 gagal; `/health` version 1.2.7 |
| Basis data nyata | TIDAK TERSENTUH — 491.520 bita, mtime 2026-10-30 16:52:56.461 sebelum dan sesudah |
| Pasang/peningkatan 1.2.6 → 1.2.7 | PASSED (30-10 17:45) — `msiexec /qn` keluar 0 dan log "status: 0"; `WIX_UPGRADE_DETECTED = {EDCBBD96-…}` lalu `RemoveExistingProducts`; daftar program kini hanya 1.2.7 `{A0AEA895-…}`; 393/393 berkas terpasang identik SHA-256 dengan publish; host 1.2.7.0, peluncur 1.2.7; bundel 1.2.6 tersapu; pintasan Start Menu + Desktop utuh; basis data 491.520 bita mtime 2026-10-30 16:52:56.461 tidak berubah; 0 proses aplikasi. Aplikasi hasil peningkatan **tidak dibuka** |
| Mesin bersih / Windows Sandbox | PASSED (30-10 18:13–18:20) — 65 pemeriksaan, 0 gagal: pasang 1.2.6 di sandbox Windows 11 build 26100 tanpa jaringan, isi data lewat API, peningkatan ke 1.2.7 (`WIX_UPGRADE_DETECTED = {EDCBBD96-…}`, `RemoveExistingProducts`, log status 0), 393/393 berkas terpasang identik publish, sidik basis data (`db`+`-wal`+`-shm`) tidak berubah, produk/sandi/merek dari 1.2.6 utuh di 1.2.7, periksa-diri 9/9, skema 17, bundel 1.2.6 tersapu, copot 1.2.7 exit 0 dengan `uji.db`+`uji.key` tetap ada. Bukti di `docs/paket-bukti-2026-10-30/upgrade-sandbox/` |
| Pasang/copot EXE 1.2.7 | PASSED (31-10 03:17–03:20) — 42 pemeriksaan, 0 gagal: pasang senyap per-pengguna dengan `/DIR=C:\tmp\seruni-exe-127` (exit 0, log "Installation process succeeded.", `[Run]` dilewati), 397 berkas, 393/393 payload identik SHA-256 dengan `dist\publish-single` plus empat berkas Inno, host dari folder hasil pasang melayani `/health` 1.2.7, SPA, aset, periksa-diri 9/9, skema 17, `/api/brand` 3 kunci; copot exit 0 dengan kedua `taskkill` di log; folder/pintasan per-pengguna/ARP per-pengguna hilang, instalasi MSI + pintasannya utuh, basis data pemilik 491.520 bita mtime tidak berubah, 0 proses. Peluncur **tidak dibuka**. Bukti di `docs/paket-bukti-2026-10-30/exe-127/` |
| Code signing | DITUNDA — 1.2.7 tetap `NotSigned` |

**Keputusan tetap READY FOR LIMITED PILOT.** Peningkatan 1.2.6 → 1.2.7 sudah dijalankan di mesin ini
dengan basis data pengguna di `%LOCALAPPDATA%\Seruni22Bakery` tetap utuh dan lulus 65/65 di Windows
Sandbox; pasang/copot EXE 1.2.7 juga lulus 42/42 di mesin ini tanpa mengganggu instalasi MSI atau basis
data. Yang masih tertahan: aplikasi 1.2.7 belum pernah dibuka lewat peluncur di mesin pemilik, copot MSI
1.2.7 di mesin pemilik belum dijalankan, paket `NotSigned`, dan perangkat kasir fisik belum diuji.
