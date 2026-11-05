# Verifikasi paket 1.2.14 — 2026-11-05

Dokumen ini mencatat apa yang **sudah dijalankan** dan apa yang **belum** untuk paket
Seruni 22 Bakery 1.2.14. Angka di bawah dibaca ulang dari berkas laporan dan dari disk pada
5 November 2026, bukan dari ingatan maupun dari dokumen rilis 1.2.13.

## Putusan

**NOT READY.** Semua yang bisa dibuktikan di mesin pengembang ini sudah dibuktikan dan seluruhnya
hijau: gerbang Release, 544 tes, 169 pemeriksaan API, `build-pro`, validasi paket 393 berkas,
kontrol negatif automasi 14/14, dan pasang/peningkatan/copot **1.2.13 → 1.2.14** di Windows Sandbox
bersih **106/106**. CI juga sudah hijau di mesin kedua — **empat run success beruntun**
(`41946780573`, `41948231824`, `41949270292` masing-masing 544 tes; `41972726233` = **547 tes** setelah
perbaikan bahan nonaktif masuk): build Release 0 peringatan/0 galat dan keter-reproduksian berkas bukti
lulus — lihat bagian "Gerbang CI"). Yang menahan putusan bukan mutu paket, melainkan empat hal yang tidak bisa
dikerjakan dari mesin ini — cetak pada printer kasir fisik, pemindai barcode fisik, laci uang, dan
uji di Windows 10 — ditambah tanda tangan Authenticode yang sertifikatnya belum ada, dan perintah
penambalan data warisan (`--rekonsiliasi-stok-bahan --terapkan`) yang menunggu persetujuan pemilik
karena menyentuh saldo neraca.

**Empat blocker perangkat + Windows 10 tidak berubah statusnya sejak 1.2.12.** Namun penyapuan naskah
buku petunjuk pada hari yang sama menemukan **dua cacat nyata pada bahan nonaktif**, keduanya sudah
diperbaiki dan diuji (`ac89217`, lihat bagian "Gerbang CI" run 5):

1. **Nilai stok bahan nonaktif menyurut jadi nol.** Baris stok mengambil harga dari daftar bahan
   **aktif**, padahal `DELETE /api/ingredients/{id}` hanya menandai `IsActive = 0` sementara barangnya
   masih ada di rak. Perbaikan: `LocationStock` kini membawa `IngredientPricePerUnit` dan
   `IngredientIsActive`, dan `LocationStockRepository.GetStockByLocationAsync` ikut membaca
   `i.PricePerUnit, i.IsActive`.
2. **Sisa stok bahan nonaktif tidak bisa dihabiskan dari layar mana pun.** **Formulir Mutasi Bahan**
   dan **Pemeriksaan Fisik (Stok Opname)** hanya menawarkan master aktif, sehingga stok peninggalannya
   terjebak. Perbaikan: kedua view menambahkan baris yang benar-benar dipegang tapi tidak dikenal
   dengan akhiran **„• nonaktif di master"**. Jalur tulis tidak disentuh — `AdjustStockAsync`/
   `TransferStockAsync`/`StockValueAsync` memang tidak pernah memfilter `IsActive`.

**Yang wajib dibaca bersama perbaikan ini: kedua cacat itu ada di paket 1.2.14.** Paket tercetak
sebelum `ac89217`, dan bundel `desktop/` tidak ikut terkomit (di-`.gitignore`), jadi EXE/MSI 1.2.14 yang
beredar masih menampilkan perilaku lama sampai `scripts/build-pro.ps1` dijalankan pada pohon ini.

## Artefak

| Berkas | Ukuran | SHA-256 | Tanda tangan |
|---|---|---|---|
| `Seruni22Bakery_Setup_v1.2.14.exe` | 42.680.006 | `f48442d54016841d0aa411dbb63103c59addebc54152cbb92001a5885a318e7c` | NotSigned |
| `Seruni22Bakery_v1.2.14_x64.msi` | 50.424.324 | `a8d998a60268eb563a56bcd0a23ef31518b2121e81d33ef30c0649588fd91def` | NotSigned |

Sumber: `dist/installer/BUILD-INFO.json`, `dist/installer/SHA256SUMS.txt`, dan hasil
`sha256sum` yang dijalankan ulang setelah kontrol negatif (keduanya tetap sama — artefak rilis
tidak tersentuh).

Dibangun dari commit **`94073a34d202e74ad3cc2537bcf7330942751573`** dengan `pohonKotor=1`: satu
berkas sumber, `README.md`, belum ter-commit pada saat build. Ini dicatat apa adanya — `build-pro`
memberi **WARN** (bukan gagal) dan laporan tetap menyebut commit dasarnya.

`UpgradeCode` `B975F792-6F80-4D56-8A64-D274A044DCF1` sama dengan paket 1.2.4–1.2.13, jadi
peningkatan menggantikan pemasangan lama, bukan menumpuk. `ProductCode` 1.2.14 adalah
`{75FF905B-6F22-4051-A437-5F9362F0EA2B}` (1.2.13: `{CE68DB95-1278-4892-B835-7F875F0DAB63}`).

Cara membangun ulang:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-pro.ps1 -SinkronkanBundelDesktop
```

## Yang terbukti masuk paket (bukan hanya ada di sumber)

Perbaikan siklus ini ada di dalam biner yang dikemas, dan itu diperiksa per berkas, bukan
diasumsikan:

| Bukti | Nilai |
|---|---|
| `Seruni22Bakery.Host.dll` hasil pasang = manifes publish | `f710ccc9a0430ad2dfe0528b535bd18495314ec77a56e8c54389032f20cfcc77` (351.744 bita), **sama byte** dengan hasil `Get-FileHash` di dalam sandbox |
| Hash berkas yang sama pada paket 1.2.13 | `d4d1bf68c31c3f66c13b9605b5588a914d0d126655defefe7f4380da173fa2d672` — **berbeda**, jadi biner host benar-benar berganti, bukan nama saja |
| Bundel web | `index-BZfyiTsC.js` (1.2.13) → `index-BmCxycbD.js` (1.2.14); CSS tetap `index-CUfTbA7B.css` karena isi CSS memang tidak berubah |
| Payload MSI | 393/393 berkas cocok hash dan ukuran terhadap `dist/publish-single.manifest.txt` |

Konsekuensi yang perlu diketahui pemilik: karena perbaikan **Stok Awal bahan** (layar Bahan Baku)
ada di biner 1.2.14, basis data toko lama tetap punya stok yang belum berjurnal. Aplikasi kini
menandainya di **Periksa Basis Data**, dan jalannya menambal tersedia — tetapi tambalan itu
sengaja tidak dijalankan lewat migrasi dan **belum dijalankan sama sekali pada basis data nyata**.

## Yang dijalankan 5 November 2026

| Pagar | Hasil | Bukti |
|---|---|---|
| `verify.ps1 -Configuration Release` | **33 langkah PASS, 0 gagal, 0 SKIP**, 222,7 s, keluar 0, konfigurasi Release, commit `94073a34…` | `dist/verify/verify-20261105-064252.json` |
| xUnit unit + integrasi + UI/Playwright | **544 lulus, 0 gagal, 0 dilewati** — Domain 83, Application 129, Infrastructure 164, UI 168 (dijumlah dari baris `Passed!` di log, bukan dari exit code) | `dist/verify/verify-20261105-064252.test.log` |
| Verifikasi perilaku lewat API nyata | **169 pemeriksaan, 0 gagal** — batch2 63, batch3 56, batch6 21, bug09 29 — host loopback + basis data sementara | langkah `verifikasi verify-*.mjs` di laporan gerbang |
| Keter-reproduksian berkas bukti (pagar baru siklus ini) | **268 lulus, 0 gagal, 16 lewati** dari 4 manifest; 16 yang dilewati adalah artefak `.exe`/`.msi` yang memang tidak dikomit — dihitung **lewati**, bukan lulus | langkah `keter-reproduksian berkas bukti` |
| `build-pro.ps1` | **12 langkah, 0 gagal, 0 dilewati, 1 peringatan** (peringatan = `README.md` belum ter-commit); tanpa opsi penimpaan (`izinkanTulisUlang=False`) | `dist/installer/BUILD-INFO.json` |
| `validate-paket.ps1` | **19 pemeriksaan, lulus 19, gagal 0, lewati 0, peringatan 0** | `dist/installer/VERIFY-REPORT.json` |
| Publish bebas berkas terlarang | lulus — tidak ada basis data, kunci JWT, kredensial, atau berkas debug | `VERIFY-REPORT.json` |
| Kontrol negatif automasi (`uji-build-pro.ps1 -Version 1.2.14`) | **14/14 lulus, 0 gagal, 0 dilewati** — baseline hijau sebelum dan sesudah sabotase, penjaga versi terbit menolak build ulang 1.2.14 (pemicu: artefak di folder keluaran) dan tidak menghapus laporan yang ada | [`docs/paket-bukti-2026-11-05/kontrol-1214/uji-build-pro-rilis-1214.json`](paket-bukti-2026-11-05/kontrol-1214/uji-build-pro-rilis-1214.json) |
| Pasang/peningkatan/copot **1.2.13 → 1.2.14** di Windows Sandbox bersih | **106 lulus, 0 gagal** dalam satu run (07:11–07:21), tanpa jaringan, tamu 1.536 MB | [`docs/paket-bukti-2026-11-05/upgrade-1213-1214-sandbox/run1/status.txt`](paket-bukti-2026-11-05/upgrade-1213-1214-sandbox/run1/status.txt) |

Kontrol negatif membuktikan validator menolak: hash EXE salah, MSI tak tercantum, manifest payload
tidak cocok, MSI hilang, versi EXE tertukar, kunci di folder publish, laporan menyebut artefak
hantu, nama ber-`..` di SHA256SUMS, EXE hilang (tanpa crash), manifest hilang (tidak boleh jadi
SKIP), dan `JsonPath` di luar repo/temp.

## Isi 106 pemeriksaan sandbox (yang paling penting)

- **Mesin bersih** terbukti: belum ada instalasi Seruni sebelum mulai; 393 berkas payload cocok
  hash+ukuran setelah pasang; `Seruni22Bakery.Host.dll` sama byte dengan manifes.
- **Peningkatan senyap 1.2.13 → 1.2.14** exit 0; `WIX_UPGRADE_DETECTED` memuat ProductCode
  `{CE68DB95-…}`; `RemoveExistingProducts` berjalan; entri **Seruni 22 Bakery 1.2.13** hilang dari
  Daftar Program dan yang tersisa tepat satu: **Seruni 22 Bakery 1.2.14**.
- **Data pemilik bertahan**: sandi hasil ganti, produk (`Roti Uji 1214`, harga 15.000), merek
  (`Toko Uji Sandbox 1214`), dan logo tetap sama persis sesudah peningkatan; `mustChangePassword`
  tidak menyala kembali; periksa-diri basis data 11 pemeriksaan, 0 gagal.
- **Skema 18 tanpa migrasi baru** (`migrasi dijalankan x0`). Ini memang yang diharapkan: 1.2.13
  sudah memakai 18, jadi paket ini tidak membawa migrasi 019.
- **Aset web lama tersapu**: `index-BZfyiTsC.js` terbukti ada sebelum peningkatan dan
  `index-BmCxycbD.js` ada sesudahnya, dengan total berkas `wwwroot\assets` tetap dua.
- **ESC/POS raw dari biner hasil peningkatan**: emulator loopback port 9100 menerima 635 bita;
  diawali `ESC @` (`0x1B 0x40`), diakhiri `ESC d 3` (`0x1B 0x64 0x03`), nominal contoh
  `2 x Rp 15.000 … Rp 30.000` hadir dan `Rp 300` yang lama tidak ada lagi, `GS V` tidak dikirim
  saat pemotongan mati, riwayat cetak mencatat pekerjaan `Test` berstatus `Printed`, dan pilihan
  perangkat bertahan setelah host di-restart.
- **Pagar**: EXE 1.2.14 menolak berjalan di atas instalasi MSI; MSI 1.2.13 ditolak di atas 1.2.14
  (exit 1603, alasan turun versi tercatat); EXE 1.2.13 asli ditolak di atas EXE 1.2.14
  (`Pagar versi`); MSI 1.2.14 ditolak selama kanal EXE ada (exit 1603, alasan kanal tercatat) dan
  tidak meninggalkan folder program maupun entri ARP.
- **Copot EXE** menghapus folder program, pintasan, entri ARP, dan penanda HKCU — tetapi folder
  data pengguna dan berkas basis data di dalamnya **tetap ada**. Tidak ada proses yatim.

Distribusi hasil: 107 baris `PASS` (106 pemeriksaan + 1 baris rekap) dan 6 baris `INFO` yang
hanya mencatat aksi harness (menghentikan host) — tidak ada `FAIL`, tidak ada `SKIP`.

## Perbandingan byte struk lintas rilis

Tangkapan ESC/POS run ini 635 bita, sama panjangnya dengan run 1.2.9 → 1.2.13. Hanya **6 byte**
yang berbeda: satu digit pada nama merek uji yang memang saya bedakan antar run
(`… Sandbox 1213` → `… Sandbox 1214`) dan lima byte cap waktu. Struktur byte struk identik,
jadi jalur cetak tidak berubah bentuk antara kedua rilis.

## Gerbang CI (mesin kedua) — run 1 merah, run 2 sampai run 5 hijau

Workflow [`/.github/workflows/gerbang.yml`](../.github/workflows/gerbang.yml) (`Gerbang build + tes`)
berjalan di pelari `windows-latest` milik GitHub. Ia **tidak** membangun installer; yang diperiksa
adalah `npm ci` + bundel desktop, `dotnet build -c Release`, `dotnet test -c Release`, dan
`scripts/uji-checkout-bukti.ps1`.

- **Run 1 [`41944547594`](https://github.com/dodorobe-bot/BAKERY/actions/runs/41944547594)** pada commit
  `96e4389`: **`failure`**. Domain 83, Application 129 dan Infrastructure 164 lulus penuh;
  UI **163 lulus / 5 gagal / 168 total**, kelimanya di `McpStdioTests`
  (4 × `JsonReaderException: 'P' is an invalid start of a value`, 1 × `IOException: The pipe is being closed`).
  Penyebabnya harness, bukan produk: `tests/UI.Tests/Helpers/McpClient.cs` mengunci jalur
  `bin\Debug\net8.0\Seruni22Bakery.Host.dll`, padahal CI hanya membangun Release, sehingga `dotnet`
  mencetak "Possible reasons …" ke stdout dan baris itu dibaca sebagai JSON.
  Langkah `Keter-reproduksian berkas bukti` tidak sempat berjalan di run ini.
- **Perbaikan (`1861fb0`)**: aturan pencarian host disatukan di `TestHost.ResolveHostDll` (konfigurasi
  majelis tes lebih dulu, lawannya sebagai pangkal cadangan) dan empat salinan privat di
  `McpClient.cs`, `ErrorEnvelopeTests.cs`, `StartupSecurityTests.cs` (2×) dan `OrderPaginationUiTests.cs`
  dihapus. Diuji dua arah di mesin ini sebelum push.
- **Run 2 [`41946780573`](https://github.com/dodorobe-bot/BAKERY/actions/runs/41946780573)** pada commit
  `1861fb0`: **`success`**, 6 m 8 s (01:11:22Z → 01:17:30Z). `Build Release` =
  `Build succeeded. 0 Warning(s) 0 Error(s)`. Tes: 83 + 129 + 164 + 168 = **544 lulus, 0 gagal,
  0 dilewati** — identik dengan gerbang Release lokal. `Keter-reproduksian berkas bukti` lulus:
  **302 dibandingkan byte-per-byte, 0 gagal, 20 lewati** dari 5 manifes (20 itu entri `.exe`/`.msi`
  yang memang tidak dikomit). Langkah `Unggah log bila ada kegagalan` berstatus `skipped` karena
  berkondisi `if: failure()` —SKIP, bukan lulus.

- **Run 3 [`41948231824`](https://github.com/dodorobe-bot/BAKERY/actions/runs/41948231824)** pada commit
  dokumentasi bukti ini (`5b0bbfa`): **`success`** juga, 4 m 11 s. **544 tes lulus untuk kedua kalinya di
  mesin kedua tanpa satu pun perubahan kode** (UI 168 lulus dalam 2 m 6 s), build Release tetap
  0 peringatan/0 galat, dan langkah keter-reproduksian naik tepat satu berkas — **282 diperiksa,
  303 lulus, 0 gagal, 20 lewati** — artinya berkas bukti ini sendiri terbukti tergandakan byte-per-byte
  di klon CI yang baru, bukan hanya di klon sekali-pakai lokal.

- **Run 4 [`41949270292`](https://github.com/dodorobe-bot/BAKERY/actions/runs/41949270292)** pada commit
  `7103013`: **`success`**, 5 m 42 s (01:33:02Z → 01:38:44Z). Build Release 0 peringatan
  (`Time Elapsed 00:01:16.00`), tes 83 + 129 + 164 + 168 = **544 lulus, 0 gagal, 0 dilewati** (UI 2 m 57 s),
  keter-reproduksian **282 diperiksa, 303 lulus, 0 gagal, 20 lewati**. Ini hijau **ketiga** beruntun dan
  yang diujinya adalah suntingan pada berkas bukti yang sudah ada — jumlah berkasnya sengaja tetap 282,
  membuktikan penjaga membandingkan berkas, bukan jumlah komit.

- **Run 5 [`41972726233`](https://github.com/dodorobe-bot/BAKERY/actions/runs/41972726233)** pada commit
  `646c9cd`: **`success`**, 6 m 3 s (05:14:16Z → 05:20:19Z) — run pertama yang membawa **perbaikan kode
  pasca-paket** (komit `ac89217` + `646c9cd` terdorong sekali lewat push). Build Release
  `0 Warning(s) 0 Error(s)`, `Time Elapsed 00:01:45.40`; tes 83 + 129 + 164 + **171** =
  **547 lulus / 0 gagal / 0 dilewati** (UI 2 m 51 s). UI naik tepat tiga = tiga tes
  `InactiveIngredientStockTests`, jadi perbaikan stok bahan nonaktif terbukti berjalan di mesin kedua.
  Keter-reproduksian tetap **282/303 lulus, 0 gagal, 20 lewati**: penjaga hanya memindai
  `docs/paket-bukti-*` ([`scripts/uji-checkout-bukti.ps1:65`](../scripts/uji-checkout-bukti.ps1#L65)),
  sedangkan berkas yang berubah ada di `src/`, `frontend/`, `tests/` dan `docs/manual/` — **PDF buku
  petunjuk 7.785.633 bita tidak dibandingkan byte-per-byte oleh CI**. Nama ketiga tes itu disimpan sebagai
  berkas bukti baru [`ci/tes-nama-bahan-nonaktif.txt`](paket-bukti-2026-11-05/ci/tes-nama-bahan-nonaktif.txt);
  run berikutnya karena itu **harus** menunjukkan 283 berkas / 304 lulus. Angka itu sudah diukur lebih dulu
  di mesin ini dengan `scripts/uji-checkout-bukti.ps1` pada commit bukti ini: **283 dibandingkan, 304 lulus,
  0 gagal, 20 lewati** — naik tepat satu berkas, tidak ada berkas lama yang berubah byte.

Rincian baris log, tabel langkah, dan batas yang harus disebut ada di
[`docs/paket-bukti-2026-11-05/ci/gerbang-ci-1214.md`](paket-bukti-2026-11-05/ci/gerbang-ci-1214.md).

## Yang TIDAK dibuktikan (dan bukan kegagalan harness)

| Belum | Kenapa | Siapa yang menutup |
|---|---|---|
| Cetak ke printer termal/jaringan sungguhan | tidak ada perangkat fisik di mesin ini; emulator adalah perangkat lunak uji di dalam tamu | pemilik, di PC kasir — PASS hanya bila kertasnya dilihat |
| Pemindai barcode fisik | hanya simulasi keyboard wedge yang bisa dibuat di sini | pemilik |
| Laci uang | tidak ada jalur `ESC p` yang dikirim (sudah diverifikasi sebagai **negatif**), jadi ini keputusan rancangan + perangkat | pemilik setelah printer lulus |
| Uji di Windows 10 | mesin ini Windows 11; tidak ada VM Windows 10 yang bisa dijalankan dari sesi ini | pemilik / mesin kedua |
| Tanda tangan Authenticode | tidak ada sertifikat; semua paket dilaporkan apa adanya sebagai `NotSigned` | pemilik (keputusan biaya, tercatat ditunda sejak 1.2.4) |
| Build installer di CI | workflow CI sengaja tidak menjalankan `build-pro.ps1`/Inno/WiX — yang dibuktikannya hanya build Release + tes + keter-reproduksian berkas | pemilik, bila ingin paket ikut terbangkitkan di cloud |
| Penambalan data warisan di basis data nyata | `--rekonsiliasi-stok-bahan --terapkan` menyentuh saldo neraca pemilik; uji kering (tanpa `--terapkan`) tidak pernah dipakai di DB nyata | persetujuan pemilik |
| Perbaikan bahan nonaktif **terpasang** di paket | kode + tes sudah hijau di mesin pengembang dan di CI run 5, tetapi paket 1.2.14 tercetak sebelumnya dan `desktop/` tidak terkomit — belum ada EXE/MSI yang memuatnya | `scripts/build-pro.ps1` pada pohon ini (butuh keputusan versi: 1.2.15) |

## Catatan keterbatasan

- Payload EXE tidak dibandingkan berkas per berkas; hanya MSI yang diekstrak administratif.
- Harness tidak memasang paket ke sistem; pemasangan dilakukan oleh skrip kit sandbox, bukan oleh
  `build-pro`.
- Status tanda tangan hanya dilaporkan, tidak dibuat.
- Mesin pengembang ini punya 3.746 MB RAM; sandbox tamu memakai 1.536 MB. Run 106/106 tercapai
  pada kondisi itu, jadi waktu uji bisa lebih panjang pada mesin yang sama bebannya lebih tinggi.

## Berkas bukti

- `docs/paket-bukti-2026-11-05/upgrade-1213-1214-sandbox/run1/` — `status.txt`, `validasi.log`,
  `msi-*.log`, `exe-*.log`, `host-*.log`, `escpos-diterima.bin`, `selesai.txt`.
- `docs/paket-bukti-2026-11-05/upgrade-1213-1214-sandbox/` — `PANDUAN-KIT.md`, `run-sandbox.ps1`,
  `uji-1213-1214.ps1`, `hash-publish-1214.txt`, `port-kit-1214.py`, `KIT-SHA256SUMS.txt`.
  Kit itu sendiri dirakit di `C:\tmp\kit-uji-1213-1214` (paket `.exe`/`.msi` tidak dikomit).
- `docs/paket-bukti-2026-11-05/kontrol-1214/uji-build-pro-rilis-1214.json`.
- `docs/paket-bukti-2026-11-05/ci/gerbang-ci-1214.md` — salinan baris log GitHub Actions **lima run**
  (`41944547594` merah oleh harness; `41946780573`, `41948231824`, `41949270292` hijau 544 tes;
  `41972726233` hijau **547 tes**). Log mentah CI tidak diunggah sebagai artefak karena
  langkah unggah berkondisi `if: failure()`; sumber resminya URL run di berkas itu.
- `docs/paket-bukti-2026-11-05/ci/tes-nama-bahan-nonaktif.txt` — nama + durasi tiga tes
  `InactiveIngredientStockTests` dari jalurnya sendiri (log CI hanya mencetak rekap per majelis),
  beserta kedua kontrol negatif yang membuatnya tidak bisa lulus kebetulan.
- `dist/verify/verify-20261105-064252.json` + `.test.log`, `dist/installer/BUILD-INFO.json`,
  `dist/installer/VERIFY-REPORT.json`, `dist/installer/SHA256SUMS.txt`.
