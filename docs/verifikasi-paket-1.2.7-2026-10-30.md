# Verifikasi Paket 1.2.7 — Merek Toko Bisa Diganti, 30 Oktober 2026

Lingkup: nama toko, slogan, dan logo kini menjadi pengaturan yang bisa diganti kapan saja dari layar Pengaturan, berlaku di layar masuk, header menu, judul jendela, struk, dan seluruh ekspor; lalu kemasan ulang 1.2.7 dan verifikasi isi paket.
Keputusan: **READY FOR LIMITED PILOT** — fitur terbukti lewat tes otomatis dan lewat binari yang benar-benar dikemas, peningkatan MSI 1.2.6 → 1.2.7 sudah dijalankan di mesin ini dengan basis data pemilik utuh (bagian 6), alur yang sama lulus 65/65 di mesin bersih Windows Sandbox termasuk copot tanpa kehilangan data (bagian 7), dan pasang/copot EXE 1.2.7 lulus 42/42 di mesin pemilik tanpa mengganggu instalasi MSI atau basis data (bagian 8). Paket tetap `NotSigned`, dan perangkat kasir fisik di luar lingkup.

## 1. Yang berubah

| Bagian | Sebelum | Sesudah |
|---|---|---|
| Nama toko | Teks tetap "SERUNI 22" di `Sidebar.jsx`, "Seruni 22 Bakery" di `document.title`, struk, dan 8 kepala ekspor | Dibaca dari `brand_name` (AppSettings) di semua tempat itu |
| Slogan | Teks tetap "Sistem Pengelolaan Roti & Kue" di layar masuk dan struk | Dibaca dari `brand_tagline`; label di layar masuk dan struk |
| Logo | Kotak huruf awal "S22" saja | Unggah PNG/JPEG/WebP di Pengaturan; tanpa logo kembali ke huruf awal |
| Lencana "BAKERY" | Teks tetap di header menu | Dihapus — bagian dari merek yang tidak bisa diganti |

Aturan merek berada di satu tempat, `src/Domain/Common/StoreBrand.cs`: nama wajib (≤40 karakter), slogan ≤80 karakter, tanpa karakter kendali, logo data URL png/jpeg/webp yang byte awalnya harus cocok dengan jenisnya dan ≤512 KB. Pelanggaran ditolak **sebelum** ada satu kunci pun ditulis, sehingga penyimpanan tidak pernah setengah jalan.

Endpoint baru `GET /api/brand` sengaja **tanpa token** karena layar masuk harus menampilkan merek sebelum ada sesi; jawabannya hanya `name`, `tagline`, dan `logo` — tidak ada data toko lain. Pagar `EndpointTanpaKebijakanHanyaYangSengajaDibuka` menangkap endpoint ini pada run pertama (daftar putih berisi 2 jalur, ditemukan 3) dan jalur itu baru didaftarkan setelah diputuskan memang publik. Perubahan merek tetap hanya untuk pemilik: `PUT /api/settings` memakai kebijakan `administration`, dan Pegawai menerima 403.

Unggahan logo di layar diperkecil ke sisi maksimum 256 px dan dikodekan ulang ke PNG di peramban sebelum dikirim; batas 512 KB di server menjaga permintaan tetap wajar. Jejak audit mencatat perubahan merek sebagai `<logo terpasang>` / `<logo dihapus>`, bukan gambar utuh, supaya log tidak membengkak dan tidak menyimpan data biner.

## 2. Regresi otomatis

`dotnet test BakeryDesktop.slnx -c Release` pada sumber yang dipaketkan (2026-10-30 17:22):

| Proyek | Lulus | Gagal | 1.2.6 |
|---|---:|---:|---:|
| Application | 51 | 0 | 48 |
| Domain | 80 | 0 | 57 |
| Infrastructure | 119 | 0 | 118 |
| UI (Playwright) | 101 | 0 | 91 |
| **Total** | **351** | **0** | **314** |

Kenaikan 37 kasus: 23 `StoreBrandTests` (Domain), 3 asersi merek baru di `ReceiptRendererTests` (Application), 1 `KepalaEkspor_MengikutiNamaTokoDiPengaturan` (Infrastructure), 10 `BrandSettingsTests` (UI). Satu tes struk lama kini juga menuntut penanda merek, jadi jumlah kasus Application naik 3, bukan 4.

Tes merek yang dijalankan hari ini:

| Berkas | Kasus | Isi |
|---|---:|---|
| `tests/Domain.Tests/StoreBrandTests.cs` | 23 | bawaan saat kosong, pemangkasan spasi, batas panjang, karakter kendali, png/jpeg/webp sah, GIF/URL biasa/base64 rusak/PNG palsu/terlalu besar ditolak, kunci lain tidak diperiksa |
| `tests/Application.Tests/ReceiptRendererTests.cs` | 4 | nama/slogan dari pengaturan, bukan teks tetap; nama outlet sama dengan merek tidak dicetak ganda; `RenderTest` ikut merek |
| `tests/Infrastructure.Tests/ExportOrdersPdfTests.cs` | 1 | kepala xlsx "Toko Roti Bu Ratna - Laporan Penjualan" |
| `tests/UI.Tests/Tests/BrandSettingsTests.cs` | 10 | `/api/brand` anonim tepat 3 kunci; ubah nama/slogan langsung terbaca anonim; nama kosong 400 tanpa simpan sebagian; 4 varian logo tidak sah + kosong berarti hapus; logo sah tersimpan dan audit tanpa base64; Pegawai 403; E2E Playwright mengisi nama/slogan, mengunggah PNG, menyimpan, lalu memeriksa header menu, judul jendela, layar masuk, dan bertahan setelah muat ulang |

Tes UI membaca bundel dari `desktop/`, yang disinkronkan dari `frontend/dist` sebelum suite dijalankan (`desktop/assets/index-DppHnhYj.js`, 582.985 bita).

## 3. Artefak 1.2.7

| Berkas | Ukuran | SHA-256 | Dibuat |
| --- | ---: | --- | --- |
| `Seruni22Bakery_Setup_v1.2.7.exe` | 42.641.033 bita | `adb779c1d6c4ceb1c424ae3700b6f435e6223ae372104827d3ef4b9a943cd36b` | 2026-10-30 17:28 |
| `Seruni22Bakery_v1.2.7_x64.msi` | 50.373.479 bita | `5c4c31f87ecb96e23b5630238ba59d72c9f115904cd98a8ac13d6953fe20dfbb` | 2026-10-30 17:31 |

Keduanya `NotSigned`. Peluncur melaporkan versi berkas 1.2.7, host 1.2.7.0. MSI ProductCode build ini `{A0AEA895-FC09-4312-8E24-8C9E0FEABDC8}`; UpgradeCode tetap `B975F792-6F80-4D56-8A64-D274A044DCF1`, `ALLUSERS=1` (perMachine). Identitas dibaca langsung dari tabel Property MSI. Tidak ada migrasi baru: skema tetap 17.

## 4. Isi paket

| Pemeriksaan | Hasil |
| --- | --- |
| Ekstraksi administratif MSI (`msiexec /a`) | 393 berkas aplikasi |
| Nama berkas vs `dist/publish-single` | 393 vs 393, tidak ada selisih nama |
| SHA-256 seluruh 393 berkas | **0 berbeda**, 0 berkas hanya di satu sisi |
| `Seruni22Bakery.Host.dll` | `4773a367836b117a8106e45a2d345b7d15d61fdf774768183c97cd28f07c8b49` — sama |
| `Seruni22Bakery.exe` (peluncur) | `777f2fcc55d5e5bdcb14c00daf73662d1f41f91dea5b0dc77bdb11ce71d281b0` — sama |
| `wwwroot/index.html` | `f4a2fc04ca04e10fd665704c11b63e1bb57677a8ac3b4570db411dfdb694bd2e` — sama dengan `frontend/dist` dan `desktop/` |
| Bundel frontend | `index-DppHnhYj.js` 582.985 bita `414fbdb2…`, `index-BNYyu2En.css` 56.532 bita `a411f239…`; hanya dua aset ini, bundel 1.2.6 tidak ikut |
| Versi dari payload | host 1.2.7.0, peluncur 1.2.7 |
| EXE Inno | 394 entri `Compressing` di log kompilasi = 393 berkas publish + `app.ico`, termasuk kedua aset baru |

## 5. Uji perilaku pada binari payload

Host dari hasil ekstraksi MSI dijalankan langsung (bukan salinan build) dengan basis data sementara dan kunci JWT sementara di port bebas; empat skrip verifikasi API dijalankan berurutan, lalu uji merek:

| Skenario | Pemeriksaan | Gagal |
|---|---:|---:|
| `verify-batch2` | 62 | 0 |
| `verify-batch3` | 54 | 0 |
| `verify-batch6` | 21 | 0 |
| `verify-bug09` | 29 | 0 |
| Uji merek (baru) | 14 | 0 |
| **Total** | **180** | **0** |

`/health` menjawab `version 1.2.7` pada keempat host. Uji merek pada binari payload membuktikan: `/api/brand` anonim mengembalikan tepat `logo,name,tagline` dengan nama "Seruni 22 Bakery" dan slogan "Sistem Manajemen Toko Kue"; nama "Toko Roti Bu Ratna" + slogan "Roti Segar Sejak 1998" tersimpan dan langsung terbaca tanpa sesi; nama 58 karakter ditolak 400 **tanpa** menyimpan sebagian; logo GIF ditolak 400; logo PNG 1×1 tersimpan sama persis lalu terhapus; jejak audit memuat 3 baris "ubah pengaturan" dan tidak satu pun memuat base64.

Basis data nyata tidak tersentuh: `seruni22bakery.db` tetap 491.520 bita, waktu ubah 2026-10-30 16:52:56.461 sebelum dan sesudah seluruh uji. Satu-satunya proses `Seruni22Bakery.Host` yang tersisa saat uji payload berjalan adalah host terpasang 1.2.6 milik pemilik (mulai 16:37), yang tidak disentuh; proses itu kemudian ditutup atas izin pemilik untuk uji peningkatan di bagian 6.

## 6. Peningkatan MSI 1.2.6 → 1.2.7 di mesin ini

Aplikasi terpasang 1.2.6 milik pemilik ditutup lebih dulu atas izin pemilik, lalu MSI 1.2.7 dipasang senyap dengan hak administrator (`msiexec /i ... /qn /norestart /l*v`). Aplikasi tidak dibuka selama maupun sesudah uji; uji ini hanya membuktikan pemasangan, bukan perilaku aplikasi terpasang.

| Pemeriksaan | Sebelum | Sesudah |
|---|---|---|
| Kode keluar MSI | — | **0** (`%ERRORLEVEL%`), baris log "Installation success or error status: 0" |
| Produk lama terdeteksi | — | `WIX_UPGRADE_DETECTED = {EDCBBD96-732D-4854-A31B-0A36E6C43E77}` (ProductCode 1.2.6), `FindRelatedProducts` + `RemoveExistingProducts` berjalan |
| Entri daftar program | `Seruni 22 Bakery` 1.2.6 `{EDCBBD96-…}` | `Seruni 22 Bakery` **1.2.7** `{A0AEA895-FC09-4312-8E24-8C9E0FEABDC8}`; hanya satu entri, 1.2.6 tidak tersisa |
| Registrasi produk | — | `ProductRegister(UpgradeCode={B975F792-6F80-4D56-8A64-D274A044DCF1}, VersionString=1.2.7)` |
| Berkas terpasang | 393 berkas, 135.742.559 bita | 393 berkas, **135.755.088 bita** |
| Perbandingan hash | — | 393 vs 393 berkas `dist\publish-single`, **0 berbeda**, 0 hanya di satu sisi |
| Host / peluncur | 1.2.6.0 / 1.2.6 | **1.2.7.0 / 1.2.7** |
| `Seruni22Bakery.Host.dll` | `c9a5b020…` | `4773a367…` (sama dengan publish) |
| `wwwroot/index.html` | `298108b0…` | `f4a2fc04…` |
| Bundel frontend | `index-BPNiweyi.js` 579.183, `index-Bz7F3T1f.css` 55.997 | `index-DppHnhYj.js` 582.985, `index-BNYyu2En.css` 56.532; **bundel 1.2.6 tidak tersisa** |
| Pintasan | Start Menu (ProgramData) + Desktop (Public) | keduanya tetap ada, sasaran `C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.exe` |
| Basis data pemilik | 491.520 bita, mtime 2026-10-30 16:52:56.461 | **identik**; `-wal`/`-shm` tidak ada sebelum maupun sesudah |
| Proses aplikasi | 0 | 0 |

Pemeriksaan sesudah peningkatan juga tidak menemukan salinan terpasang lain: satu-satunya `Seruni22Bakery.exe` di mesin ini ada di `C:\Program Files\Seruni 22 Bakery` (1.2.7), tidak ada entri daftar program per-pengguna, dan tidak ada pintasan yang menunjuk ke lokasi lain.

Basis data tidak dimigrasi karena skema tetap 17 sejak 1.2.4; karena itu waktu ubahnya wajar bila tidak berubah sama sekali.

## 7. Peningkatan MSI 1.2.6 → 1.2.7 di mesin bersih (Windows Sandbox)

Tanggal 2026-10-30 18:13–18:20, Windows Sandbox Windows 11 Enterprise build 26100, tanpa jaringan, RAM VM 1536 MB, pengguna `WDAGUtilityAccount` (elevated). Sesi sandbox dibuang setiap ditutup, jadi keadaan awalnya bersih; skrip uji memverifikasi itu sebelum memasang apa pun. Satu sesi menjalankan seluruh alur: pasang 1.2.6 → isi data lewat API → peningkatan ke 1.2.7 → periksa berkas, identitas produk, data, periksa-diri, dan fitur merek → copot 1.2.7.

| Pemeriksaan | Hasil |
|---|---|
| Mesin bersih | tidak ada `C:\Program Files\dotnet`, tidak ada folder program/entri ARP Seruni |
| MSI di dalam sandbox | 1.2.6 50.357.095 bita `461d1c77…`, 1.2.7 50.373.479 bita `5c4c31f8…` — sama dengan paket yang diverifikasi di mesin ini |
| Pasang 1.2.6 | exit 0; 393 berkas; runtime mandiri lengkap; tanpa PDB/`appsettings.Development.json`/basis data/kunci; ARP 1.2.6 `{EDCBBD96-…}` satu-satunya; kedua pintasan benar; NotSigned |
| Data di 1.2.6 | `/health` 200 `version=1.2.6`; sandi bawaan wajib diganti lalu login berhasil; produk `id=1` "Roti Uji Peningkatan" 15.000 dibuat; `/api/brand` belum ada (401); merek `Toko Uji Sandbox` tersimpan lewat `PUT /api/settings` |
| Sidik basis data pra-peningkatan | `uji.db=31b0f666…/4096`, `uji.db-wal=e941122d…/992952`, `uji.db-shm=af952b32…/32768` |
| Peningkatan ke 1.2.7 | exit 0; `WIX_UPGRADE_DETECTED = {EDCBBD96-…}`; `RemoveExistingProducts` jalan; "Installation success or error status: 0"; `ProductRegister(UpgradeCode={B975F792-…}, VersionString=1.2.7)` |
| Identitas produk sesudah | ARP 1.2.7 `{A0AEA895-…}` saja; kunci 1.2.6 hilang |
| Berkas sesudah | 393 berkas; **393 dibandingkan dengan `dist\publish-single`, 0 beda hash/ukuran, 0 selisih nama**; peluncur 1.2.7 / host 1.2.7.0; `Host.dll` `4773a367…`; `index.html` `f4a2fc04…`; bundel `index-DppHnhYj.js` + `index-BNYyu2En.css` ada, bundel 1.2.6 tidak tersisa; pintasan tetap benar |
| Basis data tidak disentuh proses pasang | sidik `uji.db` + `-wal` + `-shm` **identik** dengan sebelum peningkatan |
| Host 1.2.7 | `/health` 200 `version=1.2.7`; SPA 200; aset 582.985 bita; sandi hasil ganti di 1.2.6 masih berlaku; `mustChangePassword=False` |
| Data dari 1.2.6 | produk `id=1 name=Roti Uji Peningkatan harga=15000` muncul di daftar dan terbaca utuh; merek `Toko Uji Sandbox` masih tersimpan; `/api/brand` anonim 3 kunci |
| Periksa-diri | 9 pemeriksaan, 0 gagal; skema tetap 17 |
| Fitur merek sesudah peningkatan | nama diganti ke "Toko Uji Sandbox 127"; logo PNG dipasang (118 karakter, sama persis) lalu dihapus lagi |
| Copot 1.2.7 | exit 0; folder program, kedua pintasan, dan entri ARP hilang; **`uji.db` dan `uji.key` di `C:\data` tetap ada**; tidak ada proses `Seruni22Bakery*` tersisa |
| Rekap | **lulus=65 gagal=0** |

Bukti mentah dan skrip harness diarsipkan di `docs/paket-bukti-2026-10-30/upgrade-sandbox/` (`status.txt`, `validasi.log`, tiga log `msiexec`, dua log host, `uji-upgrade.ps1`, `uji.wsb`, `run-sandbox-upgrade.ps1`, `hash-publish.txt`, plus `run1/` untuk percobaan pertama).

Percobaan pertama (17:54–18:00) mencatat 61 lulus dan 2 gagal, keduanya cacat skrip uji, bukan cacat paket: uji membaca `/api/brand` pada 1.2.6 padahal endpoint itu baru ada di 1.2.7, dan uji membaca `sellingPriceMinorUnits` dari daftar produk padahal daftar memakai `sellingPrice`. Setelah kedua pemeriksaan diperbaiki, percobaan ketiga lulus penuh. Percobaan kedua (18:02) tidak menghasilkan bukti karena runner lama menyimpulkan sandbox mati setelah 5 detik tanpa proses; runner sekarang menunggu 120 detik tanpa proses dan tanpa kemajuan `status.txt`.

## 8. Pasang/copot EXE 1.2.7 di mesin pemilik

Tanggal 2026-10-31 03:17–03:20 (+07:00) di mesin pemilik yang sudah berisi instalasi MSI 1.2.7. EXE dipasang per-pengguna (`PrivilegesRequired=lowest`) dengan folder override `/DIR=C:\tmp\seruni-exe-127`, bukan folder bawaannya, sehingga instalasi MSI di `C:\Program Files\Seruni 22 Bakery` dan pintasannya tidak tersentuh. Peluncur `Seruni22Bakery.exe` **tidak dibuka** karena di mesin ini peluncur langsung membaca basis data pemilik; bukti "aplikasi hasil pasang berjalan" memakai `Seruni22Bakery.Host.exe` dari folder hasil pasang dengan basis data dan kunci JWT scratch.

| Pemeriksaan | Hasil |
|---|---|
| EXE sebelum dipasang | 42.641.033 bita, SHA-256 `adb779c1…` — sama dengan `SHA256SUMS.txt` |
| Keadaan awal | 0 proses Seruni; satu entri ARP (MSI 1.2.7); pintasan MSI di ProgramData Start Menu + Public Desktop menunjuk `C:\Program Files\Seruni 22 Bakery`; tidak ada grup/pintasan per-pengguna |
| Pasang senyap | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=…` exit 0; log "Installation process succeeded."; tidak ada entri `[Run]` yang dijalankan pada pasang senyap |
| Isi folder | 397 berkas = 393 payload + `app.ico` + `unins000.exe` + `unins000.dat` + `Uninstall Seruni 22 Bakery.lnk`; peluncur 1.2.7, host 1.2.7.0; `Host.dll` `4773a367…`; `index.html` `f4a2fc04…`; bundel `index-DppHnhYj.js` 582.985 bita; tanpa PDB/`appsettings.Development.json`/basis data/kunci JWT |
| Payload vs publish | **393/393 berkas identik SHA-256 dan ukuran** dengan `dist\publish-single`, 0 selisih; berkas tambahan hanya empat berkas Inno di atas |
| Pintasan EXE | grup Start Menu per-pengguna (aplikasi + uninstaller) dan Desktop per-pengguna, keduanya menunjuk `C:\tmp\seruni-exe-127\Seruni22Bakery.exe`; pintasan MSI tidak berubah |
| ARP saat terpasang | entri per-pengguna `{5D9D507C-…}_is1` "Seruni 22 Bakery (Sistem Toko & Pabrik Roti)" 1.2.7 **dan** entri MSI 1.2.7 — dua produk berbeda |
| Host dari folder hasil pasang | `/health` 200 `version=1.2.7`; SPA 200 memuat `index-DppHnhYj.js`; aset 582.985 bita; login bawaan `mustChangePassword=True`; ganti sandi http 200; login sandi baru berhasil dan gerbang padam; periksa-diri **9 pemeriksaan, 0 gagal**; skema 17; `/api/brand` anonim 3 kunci; 17 migrasi diterapkan pada basis data scratch |
| Copot senyap | `unins000.exe /VERYSILENT …` exit 0; log mencatat `taskkill /f /im Seruni22Bakery.Host.exe` lalu `/im Seruni22Bakery.exe`; "Uninstallation process succeeded." |
| Sisa setelah copot | folder, grup Start Menu, pintasan Desktop per-pengguna, dan kunci ARP per-pengguna hilang; `uji.db` dan `uji.key` di `C:\tmp\exe-127\data` tetap ada |
| Instalasi pemilik | ARP kembali satu entri MSI 1.2.7; pintasan ProgramData + Public Desktop tetap menunjuk MSI; 393 berkas MSI utuh, host 1.2.7.0 |
| Basis data pemilik | **tidak berubah**: 491.520 bita, mtime 2026-10-30 16:52:56.461, tanpa `-wal`/`-shm` |
| Proses | 0 proses `Seruni22Bakery*` tersisa |
| Rekap | **lulus=42 gagal=0**, ditambah siklus pembanding payload lulus penuh |

Bukti mentah dan skrip diarsipkan di `docs/paket-bukti-2026-10-30/exe-127/` (`status.txt`, `status-hash.txt`, dua log Inno pasang/copot + dua log siklus pembanding, dua log host, tiga potret ARP JSON, `uji-penuh.ps1`, `uji-hash.ps1`, `pra-uji.ps1`, plus `run1/` dan `status-hash-run1.txt` untuk percobaan pertama).

Percobaan pertama siklus utama mencatat satu FAIL karena periksa-diri dipanggil **sebelum** sandi bawaan diganti sehingga server menolak lewat gerbang wajib-ganti-sandi (0 pemeriksaan) — urutan harness, bukan cacat produk; siklus utama mengulang dengan urutan benar dan lulus 9/9. Percobaan pembanding pertama juga satu FAIL karena skrip mengharapkan tiga berkas tambahan padahal ada empat (`app.ico` ikut); skrip dikoreksi, paket tidak diubah.

## 9. Yang belum diuji pada 1.2.7

- Menjalankan aplikasi 1.2.7 lewat peluncur di mesin pemilik: **NOT RUN** — dilarang di mesin pemilik (data nyata). Di sandbox, host hasil peningkatan dijalankan langsung dan menjawab `/health`, SPA, dan API; pada uji EXE, host dari folder hasil pasang juga dijalankan pada basis data scratch. Jendela peluncur tidak pernah dibuka di mesin ini.
- Copot (uninstall) **MSI** 1.2.7 di mesin pemilik: **NOT RUN** — mencopot produk yang sedang menjadi instalasi pemilik akan menghapus aplikasi yang dipakai. Copot MSI 1.2.7 sudah diuji di sandbox (bagian 7); yang sudah dijalankan di mesin ini adalah copot EXE (bagian 8).
- Penandatanganan Authenticode: **BLOCKED** (sertifikat belum ada; keputusan pemilik menunda).
- Perangkat kasir fisik: **BLOCKED** (printer thermal, pemindai barcode, laci uang ditunda atas permintaan pemilik).

## 10. Kesimpulan

Nama toko, slogan, dan logo kini dapat diganti kapan saja dari Pengaturan tanpa memasang ulang aplikasi, dan perubahan itu terbawa ke layar masuk, header menu, judul jendela, struk, layar cetak peramban, serta seluruh ekspor. Validasi di server menolak nilai tidak sah sebelum menulis apa pun, jejak audit tidak menyimpan gambar utuh, dan endpoint publik hanya membuka tiga kunci merek. Paket 1.2.7 memuat bundel baru secara byte-per-byte dan binari yang dikemas lulus 180 pemeriksaan termasuk 14 pemeriksaan merek.

Peningkatan 1.2.6 → 1.2.7 berhasil di dua lingkungan: di mesin ini tanpa menyentuh basis data pemilik, dan di mesin bersih Windows Sandbox 65/65 — termasuk produk, sandi, dan merek yang dibuat di 1.2.6 tetap terbaca di 1.2.7, serta copot 1.2.7 tidak menghapus basis data di luar folder program.

Pasang/copot EXE 1.2.7 juga sudah dijalankan di mesin pemilik: 42/42 pemeriksaan lulus, 393/393 berkas payload identik dengan `dist\publish-single`, host dari folder hasil pasang menjawab `/health`/SPA/API pada basis data scratch, dan sesudah copot instalasi MSI beserta pintasannya utuh sementara basis data pemilik tetap 491.520 bita dengan mtime tak berubah. Peluncur tidak dibuka, jadi uji ini tidak membuktikan tampilan jendela aplikasi.

Keputusan **READY FOR LIMITED PILOT**. Verdict **READY FOR PRODUCTION** belum bisa diberikan selama paket `NotSigned`, peluncur 1.2.7 belum pernah dibuka di mesin pemilik, copot MSI 1.2.7 di mesin pemilik belum dijalankan, dan perangkat kasir fisik belum diuji.

Log mentah: `C:\tmp\verify-payload-127.log` (180 pemeriksaan), `C:\tmp\test-1.2.7-brand-run2.log` (351 tes), `C:\tmp\build-1.2.7.log`, `C:\tmp\build-msi-1.2.7.log`, `C:\tmp\upgrade-127.out` (potret sebelum/sesudah), `C:\tmp\msi-upgrade-127.log` (log MSI), `C:\tmp\sbx-upgrade-127\` (harness sandbox), `C:\tmp\exe-127\` (harness uji EXE); arsip bukti di `docs/paket-bukti-2026-10-30/upgrade-sandbox/` dan `docs/paket-bukti-2026-10-30/exe-127/`; metadata paket di `dist\installer\BUILD-INFO-MSI.txt`.
