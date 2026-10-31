# Changelog

Semua perubahan penting aplikasi Seruni 22 Bakery. Versi terbaru di atas.
Bukti mentah tiap rilis ada di `docs/verifikasi-paket-<versi>-<tanggal>.md` dan
`docs/paket-bukti-<tanggal>/`.

## [1.2.10] — 2026-11-01

Status: **NOT READY** sampai uji pasang/peningkatan 1.2.9 → 1.2.10 dijalankan pada mesin
bersih atau oleh pemilik. Yang sudah dibuktikan untuk paket ini hanya: build, seluruh tes
otomatis, gerbang verifikasi API, dan isi paket. Paket **NotSigned** (tidak ada sertifikat).
Skema basis data: **18** — tidak ada migrasi baru, perubahan hanya perilaku pembacaan/penomoran.
Tag: `v1.2.10` — menandai sumber paket di atas, bukan persetujuan rilis.

Artefak:

| Berkas | Ukuran | SHA-256 |
|---|---|---|
| `Seruni22Bakery_Setup_v1.2.10.exe` | 42.665.720 | `c882b69873cbd7dc51ef58761fe6d38ff2559fbe35141399b7023bced8cd78c5` |
| `Seruni22Bakery_v1.2.10_x64.msi` | 50.395.652 | `872a3c39fe6f906dd4ced13ca434fdb7351b266147dbbd26e31a4384fff4b4af` |

Berkas di dalam paket: host `Seruni22Bakery.Host.dll`
`c265d30907d86f767756969df57a47aac000329fc3f4217ba269e06fe6a6d707`, peluncur
`Seruni22Bakery.exe` `d6e56cdce1a27bf37569f6bd193b1dfda435711196da54dac5d11bb1c2bcf8ae`,
bundel web `wwwroot/index.html` `d9f8f20f0609a070f147f17fce9f15a562c33a0e352fc983d78ad7a73e2d1c`.

Rilis ini menutup sisa cacat satu-zona-waktu (keluarga BUG-06) yang tertinggal setelah 1.2.9,
plus satu cacat peluncur yang muncul saat perintah pemeliharaan.

### Diperbaiki

- **Cap waktu ekspor.** Tanggal cetak dan nama berkas `stok-*.pdf/.xlsx` serta
  `pembelian-*.pdf/.xlsx` memakai jam mesin, bukan jam toko; sekarang `BusinessTime.Now()`.
- **Rentang bawaan ekspor.** `/api/export/*` tanpa `from`/`to` memakai tanggal mesin; sekarang
  `BusinessTime.Today()` sehingga 30 hari terakhir dihitung dari hari kalender toko.
- **Penomoran dokumen.** Nomor ORD/SPK/PO/RTR dikunci pada tanggal UTC, jadi dokumen pukul
  00:00–06:59 WIB bernomor tanggal kemarin dan urutan hari bergeser. Sekarang memakai tanggal
  kalender toko (`BusinessTime.Now()` ke `IDocumentSequenceRepository.NextNumberAsync`).
  Terbukti hidup: pada 04:05 WIB (UTC masih 31 Oktober) verifikasi API menghasilkan
  `ORD-20261101-0003` dan seterusnya.
- **Rekap harian.** Penjualan per hari dan laporan harian memotong `CreatedAt` pada 10 karakter
  pertama (tanggal UTC) dan menyaring tanggal toko dengan teks UTC. Sekarang rentang tanggal toko
  diterjemahkan ke batas UTC lewat `BusinessTime.AwalHariUtc`, disaring dua tahap (prefix kasar
  agar indeks `SalesOrders(CreatedAt)` tetap terpakai, lalu pembandingan presisi), dan baris
  per jam dipetakan kembali ke tanggal toko.
- **Zona tanggal di antarmuka.** Default rentang Keuangan, umur piutang, dan penyaring
  pre-order di Penjualan memakai `toISOString()`/tanggal perangkat. Sekarang
  `tanggalTokoISO()` dan `selisihHariBisnis()` (`frontend/src/utils/money.js`), sama seperti
  kolom tanggal di layar lain.
- **Peluncur: perintah pemeliharaan tidak lagi dicegah oleh pemeriksaan "instalasi lain".**
  Selama ini `--backup`/`--restore` dihentikan lebih dulu dengan kode 1 dan pesan "folder lain"
  bila ada host dari kanal lain sedang berjalan. Host sendiri yang berwenang: `--restore`
  menolak dengan kode 3 saat aplikasi berjalan, dan `--backup` memang dirancang jalan saat
  aplikasi hidup. Pemeriksaan instalasi lain kini hanya memagar jalur start interaktif.

### Pagar dan tes baru

- Enam pemeriksaan sumber di `scripts/verify.ps1` (ekspor, rentang ekspor, penomoran pesanan,
  penomoran retur, nomor SPK/PO, rekap harian) supaya pola jam mesin tidak kembali lolos.
- Tes regresi `PenjualanDiniHari_DihitungPadaTanggalToko_BukanTanggalUtc`
  (`tests/Infrastructure.Tests/ReportingAggregateTests.cs`): dijual pada 01:30 WIB dan
  23:30 hari sebelumnya. Kontrol negatif dijalankan dengan mengembalikan
  `SalesOrderRepository.cs` ke versi lama — tes menjadi merah, lalu hijau lagi setelah
  pemulihan.
- `tests/Verify/verify-batch3.mjs` diperbaiki: pembanding "hari ini" diambil dari
  `DocumentSequences.DayKey`, bukan `strftime('%Y%m%d','now')` yang berbasis UTC. Sebelum
  perbaikan, pemeriksaan ini merah (Order=19/0) justru karena penomoran sudah benar.

### Bukti yang dijalankan

| Pemeriksaan | Hasil |
|---|---|
| `dotnet test` Release (dalam `build-installer.ps1`) | Application 51, Domain 83, Infrastructure 143, UI 116 — total 393, 0 gagal |
| `scripts/verify.ps1 -SkipTests` | 24 langkah lulus, 0 gagal |
| Uji argumen peluncur | 5 pemeriksaan, 0 gagal (4 kasus penerusan + kontrol negatif merah di peluncur lama) |
| Verifikasi API hidup | batch2 63, batch3 56, batch6 21, bug09 29 — masing-masing 0 gagal |
| Isi paket | 393 berkas; host `1.2.10.0`, peluncur `1.2.10`; bebas `.pdb`, `appsettings.Development.json`, `*.db/-wal/-shm`, `jwt.key`, `*.env`; hash cocok `BUILD-INFO.txt` dan `SHA256SUMS.txt` |
| Isi MSI | 393 entri `File` pada basis perantara WiX dengan sumber `dist\publish-single\**` |

### Belum terbukti untuk rilis ini

- **Uji pasang, peningkatan 1.2.9 → 1.2.10, turun versi, dan copot.** Up ekstrak isi MSI lokal
  gagal dengan `msiexec` 1618 (instalasi Windows Installer lain sedang berjalan) dan sesi ini
  tidak punya hak administrator; instalasi milik pemilik yang sedang berjalan tidak disentuh.
- Perangkat kasir fisik (printer 80 mm, pemindai barcode, laci uang).
- Kompatibilitas versi Windows lain di luar yang sudah diuji.
- Tanda tangan Authenticode: `NotSigned` untuk EXE maupun MSI.

## [1.2.9] — 2026-10-31

Status: **READY FOR LIMITED PILOT** (bukan READY FOR PRODUCTION). Paket **NotSigned**.
Tag: `v1.2.9`. Skema basis data: **18** (migrasi `018_CostOfGoodsAndInventoryJournals`).

Artefak:

| Berkas | Ukuran | SHA-256 |
|---|---|---|
| `Seruni22Bakery_Setup_v1.2.9.exe` | 42.662.274 | `fd233be4e4a8441dd2e63349b723466ee4ab803f41e6d6b357b97e05f55143b7` |
| `Seruni22Bakery_v1.2.9_x64.msi` | 50.391.554 | `4cf092ce05cfb0301d2740cc136322135dc9f506a482c807bea020782052d635` |

Rilis ini menutup seluruh 31 temuan audit tahap kedua: 2 Kritis, 3 Tinggi, 15 Sedang,
11 Rendah.

### Diperbaiki — Kritis

- **BUG-004** Penyelesaian SPK tidak lagi mempercayai bahan dan harga satuan kiriman
  klien. Bahan asing, duplikat, atau kosong ditolak; stok dan nilai jurnal tidak bisa
  ditekan dari klien.
- **BUG-005** SPK tidak bisa diselesaikan dengan output nol, sehingga jurnal tidak lagi
  mendebit persediaan produk jadi tanpa mutasi stok.

### Diperbaiki — Tinggi

- **BUG-001** Jurnal harga pokok penjualan (COGS) kini terbentuk saat penjualan,
  memakai `AccountingPoster` yang sama dengan jalur persediaan.
- **BUG-002** Transfer gudang→dapur dan stok opname kini berjurnal; akun persediaan
  dapur tidak lagi bisa negatif tanpa catatan.
- **BUG-010** Status pesanan dan pembayaran diputuskan di dalam transaksi, dengan syarat
  status pada `UPDATE`; keputusan baca-lalu-tulis di luar transaksi dihapus.

### Diperbaiki — Sedang

- **BUG-003** Penerimaan barang mendebit persediaan sesuai lokasi tujuan, bukan selalu
  gudang.
- **BUG-006** Ringkasan HPP tidak lagi terpotong pada 200 SPK.
- **BUG-007** Definisi HPP aktual disamakan di semua layar dan ekspor.
- **BUG-008** Ekspor pembelian tidak lagi memotong pecahan.
- **BUG-009** Retur dinilai dengan harga saat transaksi, bukan resep yang berlaku kini.
- **BUG-011** Penerimaan/pembatalan PO terlindung dari balapan dua permintaan.
- **BUG-014** `GET /api/users` tidak lagi mengirim `PasswordHash`.
- **BUG-015** Resep, harga bahan, dan HPP dibatasi per peran.
- **BUG-016** Daftar pelanggan dibatasi untuk kasir.
- **BUG-018** Peran Dapur tidak lagi bisa membuat PO atau menerima barang.
- **BUG-021** Ada kebijakan otorisasi cadangan; endpoint baru tidak jatuh ke "terbuka".
- **BUG-022** Versi skema yang gagal dibaca tidak lagi dianggap 0.
- **BUG-024** Tersedia UI pembatalan pesanan.
- **BUG-029** `PublishDir` MSI tidak lagi absolut.
- **BUG-030** Dua kanal installer (MSI dan EXE) tidak bisa terpasang bersamaan.

### Diperbaiki — Rendah

- **BUG-012** Pelanggan dan poin loyalti ditulis di dalam transaksi.
- **BUG-013** Transfer jumlah 0 tidak lagi dilaporkan sukses.
- **BUG-017** `GET /api/settings` memerlukan peran yang tepat, bukan sekadar login.
- **BUG-019** Pesan kunci akun tidak lagi membocorkan keberadaan akun.
- **BUG-020** Secret JWT wajib memenuhi panjang minimum.
- **BUG-023** Penyimpanan pengaturan bersifat atomik.
- **BUG-025** Fetch memiliki timeout.
- **BUG-026** Galat server dijawab JSON, bukan HTML SPA.
- **BUG-027** Daftar pesanan memakai pagination, tidak lagi berhenti di 100.
- **BUG-028** Metode pembayaran divalidasi.
- **BUG-031** Pagar versi diseragamkan di kedua kanal; aplikasi tetap tidak memiliki
  pembaruan otomatis, jadi pembaruan dilakukan dengan menjalankan paket baru.

### Ditambahkan

- Migrasi `018_CostOfGoodsAndInventoryJournals` (skema 18) dan `AccountingPoster`
  sebagai satu jalur penulisan jurnal persediaan/HPP.
- Peluncur meneruskan argumen pemeliharaan (`--backup`, `--restore <berkas>`,
  `--Database:Path=...`) ke host, plus tes penerusan argumen.
- Harness uji pasang/peningkatan 1.2.9 di `docs/paket-bukti-2026-10-31/uji-129/`
  (13 bagian) dan kit siap salin `scripts/siapkan-kit-uji-129.ps1`.
- Pagar rekap harness: PASS hanya bila tidak ada FAIL **dan** total pemeriksaan ≥60,
  sehingga run yang terpotong tidak bisa tampil lulus.

### Installer

- MSI menolak pemasangan versi lebih lama (`NOT WIX_DOWNGRADE_DETECTED`).
- MSI menolak berjalan selama kanal EXE terpasang; EXE menolak berjalan selama kanal
  MSI terpasang, termasuk MSI lama yang tidak menulis penanda `Channel`.
- EXE menolak paket yang lebih lama dari versi di `HKCU\Software\Seruni22Bakery`.
- Penolakan senyap tidak menggantung pada `/SUPPRESSMSGBOXES`.
- Isi MSI 1.2.9: 393/393 berkas identik SHA-256 dan ukuran dengan `dist\publish-single`.

### Pengujian

- 392 tes otomatis lulus: Domain 83, Application 51, Infrastructure 142, UI 116.
- Gate `scripts/verify.ps1` lulus 18/18 langkah.
- Biner yang benar-benar dikemas lulus 169 pemeriksaan perilaku (batch2 63, batch3 56,
  batch6 21, bug09 29) dan 25 pemeriksaan payload: `/health` 1.2.9, 18 migrasi,
  periksa-diri 9/9, skema 18, `/api/brand` empat kunci.
- Uji pasang/peningkatan/copot di Windows Sandbox pada mesin pengembangan **gagal
  dijalankan** karena host hanya 3.746 MB RAM (event ID 2004), bukan karena paket.
- Pemilik menjalankan kit di mesin ≥8 GB dan melaporkan tertulis
  `lulus=65 gagal=0 total=65`. Dasar keputusan adalah **atestasi tertulis**; berkas
  `status.txt`/`runner.log` tidak pernah diterima di mesin pengembangan.

### Batasan yang diketahui

- Paket **belum ditandatangani** (`NotSigned`); sertifikat code signing belum dibeli.
- Hasil pasang/peningkatan/copot bersandar pada atestasi tertulis, bukan berkas bukti
  mentah yang diperiksa.
- Perangkat kasir fisik (printer thermal 80 mm, pemindai barcode USB, laci uang) belum diuji.
- Kompatibilitas belum diuji di semua versi Windows.
- MSI 1.2.7 di mesin pemilik belum dicopot.
- Installer EXE 1.2.7/1.2.8 tidak memiliki pagar versi, sehingga installer lama itu
  sendiri masih bisa menurunkan instalasi.

## [1.2.8] — tidak dirilis

- Peluncur meneruskan argumen pemeliharaan ke host; koreksi teks perintah pemulihan.
- Perubahan ini ikut termuat di 1.2.9. Tidak ada paket 1.2.8 yang diterbitkan.

## [1.2.7] — 2026-10-30

- Nama toko, slogan, dan logo dapat diganti kapan saja dari layar Pengaturan.
- `GET /api/brand` anonim mengembalikan `name`, `tagline`, `logo`.
- Logo dipakai di sidebar, layar masuk, judul jendela, struk, dan ekspor.
- Skema tetap 17.

## [1.2.6] — 2026-10-29

- Kemasan final audit pra-pilot: sembilan cacat yang ditutup di 1.2.5 ikut termuat.
- Payload MSI 1.2.6 diperiksa identik dengan `dist\publish-single`; pemasangan
  1.2.6 belum dijalankan pada saat rilis itu.

## [1.2.5] — 2026-10-29

- Perbaikan BUG-02 s.d. BUG-11: tab pemasok, bagian pelanggan/pre-order, nilai retur,
  jurnal transfer stok, zona waktu toko, stok produk jadi di katalog, label keuangan.

## [1.2.4] — 2026-10-29

- Skema 17: indeks penutup untuk data besar; peningkatan performa navigasi.
- MSI WiX pertama yang dipakai untuk uji mesin bersih.
