# Verifikasi Paket 1.2.5 — Penutupan Cacat Terbuka, 29 Oktober 2026

Lingkup: perbaikan sembilan cacat yang masih terbuka (BUG-02, 03, 04, 05, 06, 07, 08, 10, 11), regresi otomatis, lalu kemasan ulang 1.2.5 dan verifikasi isi/pasang-copot.
Keputusan: **READY FOR LIMITED PILOT** — cacat kode tertutup dan terbukti lewat tes; kedua paket memuat perbaikannya. Perangkat kasir fisik tetap di luar lingkup (ditunda atas permintaan pemilik), paket tetap `NotSigned`, dan aplikasi hasil paket belum dijalankan di mesin pemilik.

## 1. Cacat yang ditutup

| Kode | Gejala | Perbaikan | Bukti |
|---|---|---|---|
| BUG-02 | Menu "Pemasok" membuka tab Purchase Order | `PurchasesView` menerima `initialTab`/`onTabChange`; menu memilih tab awalnya | tes UI `MenuPemasok_MembukaTabPemasok` |
| BUG-03 | Menu Pelanggan, Pre-Order, Perencanaan, Pemakaian menampilkan daftar/tabel yang sama | `SalesView`/`ProductionView` memakai pola bagian (view prop + strip tab + filter); menu samping membuka bagian yang sesuai | 4 tes UI (pelanggan, jadwal ambil, SPK terjadwal, dsb.) |
| BUG-04 | Nilai uang retur bisa diisi bebas, tidak terikat jumlah unit | `SalesReturnService` menghitung nilai dari porsi unit; selisih ditolak dengan pesan nominal | `Retur_NilaiUangMengikutiUnit` |
| BUG-05 | Mutasi gudang→dapur tercatat "Adjustment", bukan "Transfer" | pembaca `LocationStockRepository` menerjemahkan `Adjustment`+`SourceType='LocationTransfer'` menjadi `Transfer` (tanpa migrasi) | tes Batch-7 + label `Transfer antarlokasi` |
| BUG-06 | Cap waktu tampil dalam UTC/format berbeda antar layar | satu zona toko `Asia/Jakarta` di `money.js`; instan keluar sebagai UTC bertanda | `CapWaktu_MutasiStokDanJejakAudit_MemakaiUtcBertanda` |
| BUG-07 | Katalog tidak menampilkan stok produk jadi | `finishedGoods` disalurkan dari API ke `InventoryView`; kolom stok jadi per lokasi | tes UI `Katalog_MenampilkanKolomStokProdukJadi` |
| BUG-08 | Catatan usang di layar Keuangan menyebut fitur yang sudah ada | catatan diganti: selisih = laba berjalan belum ditutup, omzet per kanal belum dicatat | tinjauan layar + tes UI |
| BUG-10 | Pintasan dashboard "Perintah Kerja Dapur" tampil tanpa hak produksi | dibungkus `bolehAksi(user, 'production-write')` | tes UI `PintasanDashboard_TanpaHakProduksi_TidakDirender` |
| BUG-11 | Label "Laba Kotor"/"Margin" menyesatkan karena penjualan belum dibebani HPP | KPI menjadi "Selisih Pendapatan − Beban"/"Rasio Selisih"; catatan eksplisit; notifikasi dan MCP memakai istilah yang sama | tes UI `Analisis_MemakaiLabelSelisihBukanLabaKotor` |

## 2. Regresi otomatis

`dotnet test BakeryDesktop.slnx -c Release` pada sumber yang dipaketkan:

| Proyek | Lulus | Gagal |
|---|---:|---:|
| Application | 46 | 0 |
| Domain | 57 | 0 |
| Infrastructure | 115 | 0 |
| UI (Playwright) | 90 | 0 |
| **Total** | **308** | **0** |

Tes UI membaca bundel dari `desktop/`, jadi `desktop/` disinkronkan dari `frontend/dist` sebelum dijalankan; tanpa itu tes akan menguji bundel lama.

## 3. Artefak 1.2.5

| Berkas | Ukuran | SHA-256 | Dibuat |
| --- | ---: | --- | --- |
| `Seruni22Bakery_Setup_v1.2.5.exe` | 42.633.048 bita | `3cc50a1ad222b8a3a1210e523b2acfd953611bb920d5c31407ef4497dbc647ed` | 2026-10-29 17:53 |
| `Seruni22Bakery_v1.2.5_x64.msi` | 50.385.767 bita | `f68363eab70002ef67a3fa2096f62887c3a093841ecaf8f9cd646ec5c303c884` | 2026-10-29 17:54 |

Keduanya `NotSigned`. Peluncur melaporkan versi berkas 1.2.5, host 1.2.5.0. MSI ProductCode build ini `{45DD948C-0D91-424E-9005-6B506457F1EE}`; UpgradeCode tetap `B975F792-6F80-4D56-8A64-D274A044DCF1`.

## 4. Isi paket (MSI, ekstraksi administratif `/a`)

| Pemeriksaan | Hasil |
| --- | --- |
| Ekstraksi administratif | exit 0 |
| Berkas aplikasi vs `dist/publish-single` | 393 berkas, **0 berbeda** (byte-per-byte) |
| Berkas wajib | host exe/dll, hostfxr, coreclr, appsettings, peluncur, `wwwroot/index.html` — semua ada |
| Berkas pengembangan/rahasia | tanpa `appsettings.Development.json`, 0 PDB, tanpa basis data/kunci |
| `wwwroot/index.html` | `298108b0bae6ba8bc550a3f3adcd41ff65e210bf86c22f4931ef38b46ee946f8` — sama dengan `frontend/dist` dan `desktop/` |
| Bundel JavaScript | `index-BPNiweyi.js`, 579.183 bita |
| Uji asap host dari payload (DB scratch, port 5391) | `/health` 200 `{"status":"ok","app":"Seruni 22 Bakery","version":"1.2.5"}`; SPA 200; aset 200 (579.183 bita) |

## 5. Uji EXE (Inno Setup) — pasang/copot senyap

Dipasang ke folder sementara `C:\tmp\seruni-exe-125` dengan `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /MERGETASKS="!desktopicon"`, lalu dicopot.

| Pemeriksaan | Hasil |
| --- | --- |
| Baris perintah setup di log Inno | memuat seluruh switch; `/SL5` membuktikan proses senyap |
| Pasang senyap | exit 0, "Installation process succeeded" |
| Berkas terpasang | 397 berkas; peluncur 1.2.5, host 1.2.5.0, index sama dengan publish, bundel `index-BPNiweyi.js` |
| Berkas pengembangan/rahasia | tanpa `appsettings.Development.json`, 0 PDB |
| Aplikasi dijalankan saat pasang | 0 proses (entri `[Run]` `skipifsilent`) |
| Basis data nyata sebelum/sesudah pasang | ukuran + waktu ubah `seruni22bakery.db`, `-wal`, `-shm` identik |
| Copot senyap | exit 0; folder uji terhapus; 0 proses tertinggal |
| Basis data nyata sesudah copot | ukuran + waktu ubah identik |
| Instalasi nyata per-pengguna (1.2.4, 397 berkas) | folder dan desktop shortcut tetap; kunci uninstall diekspor sebelum uji dan dipulihkan sesudahnya (kembali menunjuk 1.2.4) |

Catatan operator (temuan saat uji): `DisableProgramGroupPage=yes` membuat Inno **mengabaikan** `/GROUP=`, sehingga shortcut Start Menu uji ditulis ke grup `Seruni 22 Bakery` milik instalasi nyata dan ikut terhapus saat copot. Shortcut Start Menu instalasi nyata dipulihkan manual (aplikasi + uninstaller, target dan ikon sama seperti semula). Untuk uji ulang, cadangkan/ pulihkan folder Start Menu atau pakai akun/sandbox terpisah; jangan mengandalkan `/GROUP` selama `DisableProgramGroupPage=yes`.

## 6. Yang belum diuji pada 1.2.5

- Pasang/copot MSI 1.2.5 di mesin ini (butuh shell administrator) dan uji mesin bersih (Windows Sandbox). Mekanisme MSI tidak berubah dari 1.2.4 yang lulus 30/30; yang berubah hanya versi dan ProductCode.
- Menjalankan aplikasi hasil paket di mesin pemilik (dilarang; data pemilik).
- Penandatanganan Authenticode (sertifikat belum ada; keputusan pemilik: ditunda).
- Perangkat kasir fisik: printer thermal, pemindai barcode, laci uang (ditunda atas permintaan pemilik, 2026-10-29).

## 7. Kesimpulan

Sembilan cacat terbuka tertutup dan dijaga 308 tes otomatis. Paket 1.2.5 memuat perbaikan tersebut; isi MSI identik dengan hasil publish, EXE lulus pasang/copot senyap tanpa menyentuh basis data nyata. Keputusan tetap **READY FOR LIMITED PILOT** dengan syarat yang sama seperti laporan GO/NO-GO: uji perangkat fisik di PC kasir, pasang di mesin pemilik, dan cadangan off-machine sebelum dipakai.

Log mentah uji 1.2.5: `docs/paket-bukti-2026-10-29/paket-1.2.5/` (`verify-msi-125.log`, `verify-exe-125.log`, `seruni-exe-125-install.log`, `seruni-exe-125-uninstall.log`); log build di `dist\build-logs\`.
