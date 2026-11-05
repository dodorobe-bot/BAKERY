# Bukti gerbang CI 1.2.14 — GitHub Actions, 5 November 2026

Workflow: [`.github/workflows/gerbang.yml`](../../../.github/workflows/gerbang.yml) (`name: Gerbang build + tes`,
pelari `windows-latest`, pemicu `push` ke `main` + `pull_request` + `workflow_dispatch`).
Angka di bawah disalin apa adanya dari log yang diunduh GitHub (`gh run view --log`), bukan dari laporan lokal.
Kelima run memakai mesin cloud yang berbeda dari PC pengembang (RAM pelari Actions jauh lebih besar),
jadi mereka tidak membuktikan hal yang sama dengan gerbang lokal — mereka saling menguatkan.

| # | Run | Commit | Isi run | Kesimpulan |
|---|---|---|---|---|
| 1 | `41944547594` | `96e4389` | dokumen bukti 1.2.14 | **failure** (harness, bukan produk) |
| 2 | `41946780573` | `1861fb0` | perbaikan resolusi host DLL | success — 544 tes |
| 3 | `41948231824` | `5b0bbfa` | berkas bukti run 2 dibuat | success — 544 tes, 282 berkas |
| 4 | `41949270292` | `7103013` | berkas bukti run 3 disunting | success — 544 tes, 282 berkas |
| 5 | `41972726233` | `646c9cd` | **perbaikan kode bahan nonaktif + manual BAKAWAN** | success — **547 tes** |

## Run 1 — MERAH (`41944547594`)

| Medan | Nilai |
|---|---|
| Commit | `96e4389d40bd9c04f87403ab36140f24d6099fb8` ("Bukti 1.2.14: gerbang 33 langkah, Sandbox 1.2.13→1.2.14 lulus 106") |
| Pemicu | `push`, dibuat 2026-11-05T00:52:35Z, selesai 2026-11-05T00:57:53Z |
| Kesimpulan | `failure` |
| URL | https://github.com/dodorobe-bot/BAKERY/actions/runs/41944547594 |

Hasil langkah: `Set up job`, checkout, setup-dotnet, setup-node, `Pasang dependensi frontend`,
`Bangun bundel desktop`, `Unduh browser Playwright`, `Build Release` = **success**; `Tes Release` = **failed**;
`Unggah log bila ada kegagalan` = success; **`Keter-reproduksian berkas bukti` tidak pernah dijalankan**
(karena run sudah mati di langkah tes).

Rekap tes per majelis:

```text
Passed!  - Failed: 0, Passed:  83, Skipped: 0, Total:  83, Duration: 3 s     - BakeryDesktop.Domain.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 129, Skipped: 0, Total: 129, Duration: 6 s     - BakeryDesktop.Application.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 164, Skipped: 0, Total: 164, Duration: 35 s    - BakeryDesktop.Infrastructure.Tests.dll (net8.0)
Failed!  - Failed:   5, Passed: 163, Skipped: 0, Total: 168, Duration: 2 m 49 s - BakeryDesktop.UI.Tests.dll (net8.0)
```

Lima kegagalan, semuanya di kelas yang sama (`McpStdioTests`), dengan dua gejala:

```text
Failed BakeryDesktop.UI.Tests.Tests.McpStdioTests.Inisialisasi_ToolsList_DanPemanggilanSah [534 ms]
 System.Text.Json.JsonReaderException : 'P' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.
Failed BakeryDesktop.UI.Tests.Tests.McpStdioTests.Pegawai_DitolakLaporanHarian_TetapiTugasTetapBoleh_DanSemuaDicatatAudit [847 ms]
 System.Text.Json.JsonReaderException : 'P' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.
Failed BakeryDesktop.UI.Tests.Tests.McpStdioTests.TanpaAkunAgen_SemuaToolDitolak [221 ms]
 System.Text.Json.JsonReaderException : 'P' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.
Failed BakeryDesktop.UI.Tests.Tests.McpStdioTests.NotifikasiDariTransaksiHost_TerbacaDariProsesMcp [562 ms]
 System.Text.Json.JsonReaderException : 'P' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.
Failed BakeryDesktop.UI.Tests.Tests.McpStdioTests.Penolakan_ToolAsingArgumenSalahDanMetodeTakDikenal [553 ms]
 System.IO.IOException : The pipe is being closed.
```

**Akar masalah — harness, bukan produk.** `tests/UI.Tests/Helpers/McpClient.cs:33` menambahkan argumen
proses anak dengan jalur yang dikunci ke konfigurasi Debug:

```csharp
// sebelum
psi.ArgumentList.Add(Path.Combine(root, "src", "Host.Service", "bin", "Debug", "net8.0",
    "Seruni22Bakery.Host.dll"));
```

CI hanya menjalankan `dotnet build -c Release`, jadi `bin\Debug\net8.0\Seruni22Bakery.Host.dll` tidak ada di
pelari. Perintah `dotnet <dll hilang>` mencetak pesannya sendiri ke stdout — baris yang diawali huruf `P`
("Possible reasons for this issue:") — dan `McpClient.KirimAsync` membaca satu baris stdout sebagai JSON,
sehingga galatnya muncul jauh dari penyebabnya. `IOException: The pipe is being closed` adalah varian gejala
yang sama: proses anak sudah berhenti sebelum balasan pertama.

Bukti pendukung bahwa kode produk tidak berubah di antara keduanya: Domain 83, Application 129 dan
Infrastructure 164 **sama persis** lulus di run 1 dan run 2; UI 163 lulus + 5 gagal di run 1 = 168 total,
dan 168 lulus di run 2.

## Perbaikan — commit `1861fb0`

Aturan pencarian host disatukan di satu tempat, `TestHost.ResolveHostDll` (dibuat `public`), dan empat
salinan privat yang tersebar di harness dihapus:

| Berkas | Perubahan |
|---|---|
| `tests/UI.Tests/Helpers/TestHost.cs` | `ResolveHostDll` jadi `public static` + komentar aturannya |
| `tests/UI.Tests/Helpers/McpClient.cs` | memakai `TestHost.ResolveHostDll(root)` |
| `tests/UI.Tests/Tests/ErrorEnvelopeTests.cs` | memakai `TestHost.ResolveHostDll(root)`; duplikat privat dihapus |
| `tests/UI.Tests/Tests/StartupSecurityTests.cs` | 2 panggilan memakai `TestHost.RepositoryRoot`; dua duplikat privat dihapus |
| `tests/UI.Tests/Tests/OrderPaginationUiTests.cs` | memakai `TestHost.ResolveHostDll(root)`; duplikat privat dihapus |

Aturan yang dipakai tetap: konfigurasi majelis tes lebih dulu, lalu konfigurasi lawannya sebagaifallback —
jadi `dotnet test -c Release` menguji host Release dan `-c Debug` menguji host Debug, tanpa diam-diam
memakai biner build lama.

Terbukti dua arah di mesin ini sebelum push: pada pohon yang hanya berisi `bin\Release`, klien MCP
menemukan dan menjalankan host (hijau); pada pohon yang path-nya dihapus dari kedua konfigurasi, harness
berhenti dengan pesan `Seruni22Bakery.Host.dll belum ada. Jalankan: dotnet build ...` — bukan `JsonReaderException`.

## Run 2 — HIJAU (`41946780573`)

| Medan | Nilai |
|---|---|
| Commit | `1861fb0f8ed716b5abf6c53e4063b4f53fd5cc48` ("Harness: satu aturan resolusi host DLL; klien MCP tidak lagi mengunci Debug") |
| Pemicu | `push`, dibuat 2026-11-05T01:11:22Z, selesai 2026-11-05T01:17:30Z (6 m 8 s) |
| Kesimpulan | **`success`** |
| URL | https://github.com/dodorobe-bot/BAKERY/actions/runs/41946780573 |

| # | Langkah | Kesimpulan |
|---|---|---|
| 1 | Set up job | success |
| 2 | Run actions/checkout@v4 | success |
| 3 | Run actions/setup-dotnet@v4 | success |
| 4 | Run actions/setup-node@v4 | success |
| 5 | Pasang dependensi frontend | success |
| 6 | Bangun bundel desktop | success |
| 7 | Unduh browser Playwright | success |
| 8 | Build Release | success |
| 9 | Tes Release | **success** |
| 10 | Unggah log bila ada kegagalan | **skipped** (sengaja: langkah ini berkondisi `if: failure()`) |
| 11 | Keter-reproduksian berkas bukti | **success** |
| 20–23 | Post steps + Complete job | success |

Rekap tes:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:01:20.26

Passed!  - Failed: 0, Passed:  83, Skipped: 0, Total:  83, Duration: 3 s      - BakeryDesktop.Domain.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 129, Skipped: 0, Total: 129, Duration: 6 s      - BakeryDesktop.Application.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 164, Skipped: 0, Total: 164, Duration: 1 m 10 s - BakeryDesktop.Infrastructure.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 168, Skipped: 0, Total: 168, Duration: 3 m 2 s  - BakeryDesktop.UI.Tests.dll (net8.0)
```

**Total 544 lulus / 0 gagal / 0 dilewati** — identik dengan gerbang Release lokal
(`dist/verify/verify-20261105-080610.json`: 33 langkah lulus, 544 tes).

Langkah keter-reproduksian (pertama kali berjalan di CI; run 1 tidak pernah sampai ke sana):

```text
[MULAI] Clone sekali-pakai dengan core.autocrlf=true (konfigurasi yang merusak bukti): C:\Users\RUNNER~1\AppData\Local\Temp\seruni-checkout-bukti-07d6e362
[MULAI] 281 berkas bukti dibandingkan byte-per-byte dengan blob
[OK] -text berlaku (contoh docs/paket-bukti-2026-11-05/upgrade-1213-1214-sandbox/run-sandbox.ps1)
[RINGKAS] 302 lulus, 0 gagal, 20 lewati (artefak .exe/.msi tidak dikomit) dari 5 manifest
```

`20 lewati` adalah entri `.exe`/`.msi` yang memang tidak dikomit — bukan pemeriksaan yang tidak jalan.

## Run 3 — HIJAU (`41948231824`): commit dokumentasi bukti ini

| Medan | Nilai |
|---|---|
| Commit | `5b0bbfaba9c651915c9ffeff6ed1dc849a074a47` ("Bukti CI 1.2.14: run 41946780573 hijau, run 41944547594 merah oleh harness") |
| Pemicu | `push`, dibuat 2026-11-05T01:23:48Z, selesai 2026-11-05T01:27:59Z (4 m 11 s) |
| Kesimpulan | **`success`** |
| URL | https://github.com/dodorobe-bot/BAKERY/actions/runs/41948231824 |

Semua 14 langkah sama seperti run 2 (`Unggah log bila ada kegagalan` = `skipped` karena `if: failure()`),
dengan angka yang menguatkan dua hal berbeda:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed!  - Failed: 0, Passed:  83, Skipped: 0, Total:  83, Duration: 1 s     - BakeryDesktop.Domain.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 129, Skipped: 0, Total: 129, Duration: 4 s      - BakeryDesktop.Application.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 164, Skipped: 0, Total: 164, Duration: 46 s     - BakeryDesktop.Infrastructure.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 168, Skipped: 0, Total: 168, Duration: 2 m 6 s  - BakeryDesktop.UI.Tests.dll (net8.0)

[MULAI] 282 berkas bukti dibandingkan byte-per-byte dengan blob
[OK] -text berlaku (contoh docs/paket-bukti-2026-11-04/upgrade-1210-1213-sandbox/uji-1210-1213.ps1)
[RINGKAS] 303 lulus, 0 gagal, 20 lewati (artefak .exe/.msi tidak dikomit) dari 5 manifest
```

- **544 tes lulus untuk kedua kalinya di mesin kedua**, tanpa satu pun perubahan kode: UI 168 lulus pada
  dua majelis biner yang berbeda waktu (run 2 3 m 2 s, run 3 2 m 6 s). Ini yang membedakan "hijau" dari
  "kebetulan hijau".
- Jumlah berkas bukti naik tepat satu (281 → 282) dan yang lulus naik tepat satu (302 → 303) pada run yang
  mengomitkan berkas ini sendiri — bukti keter-reproduksian dokumen ini terverifikasi di klon CI yang baru,
  dengan `core.autocrlf=true`, bukan hanya di klon sekali-pakai lokal.

## Run 4 — HIJAU (`41949270292`): suntingan berkas bukti ini

| Medan | Nilai |
|---|---|
| Commit | `710301313ce234c78a32daabce9d3bbc96a18a33` ("Bukti CI 1.2.14: run 41948231824 hijau, 544 tes lulus dua kali beruntun") |
| Pemicu | `push`, dibuat 2026-11-05T01:33:02Z, selesai 2026-11-05T01:38:44Z (5 m 42 s) |
| Kesimpulan | **`success`** |
| URL | https://github.com/dodorobe-bot/BAKERY/actions/runs/41949270292 |

Langkah `Unggah log bila ada kegagalan` kembali `skipped` (`if: failure()`); yang lain success.

```text
    0 Warning(s)
Time Elapsed 00:01:16.00

Passed!  - Failed: 0, Passed:  83, Skipped: 0, Total:  83, Duration: 3 s   - BakeryDesktop.Domain.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 129, Skipped: 0, Total: 129, Duration: 5 s   - BakeryDesktop.Application.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 164, Skipped: 0, Total: 164, Duration: 36 s  - BakeryDesktop.Infrastructure.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 168, Skipped: 0, Total: 168, Duration: 2 m 57 s - BakeryDesktop.UI.Tests.dll (net8.0)

[MULAI] 282 berkas bukti dibandingkan byte-per-byte dengan blob
[OK] -text berlaku (contoh docs/paket-bukti-2026-11-01/uji-fisik-1212/auth/uji-auth-1212.ps1)
[RINGKAS] 303 lulus, 0 gagal, 20 lewati (artefak .exe/.msi tidak dikomit) dari 5 manifest
```

Yang dikuatkan run 4, bukan run 2 atau 3: **544 tes lulus untuk ketiga kalinya berturut-turut** di pelari
yang berbeda (UI 2 m 57 s, durasi ketiga berbeda dari 3 m 2 s dan 2 m 6 s), dan jumlah berkas bukti **tetap
282** walaupun commit ini menyunting `gerbang-ci-1214.md` — penjaganya membandingkan **berkas**, bukan
suntingan, jadi suntingan pada berkas yang sudah terdaftar tidak menaikkan hitungan. Ini justru pemeriksaan
yang lebih keras: versi baru dari berkas bukti yang sama harus tetap cocok byte-per-byte dengan blob-nya di
klon `core.autocrlf=true`.

## Run 5 — HIJAU (`41972726233`): run pertama yang memuat perbaikan bahan nonaktif

| Medan | Nilai |
|---|---|
| Commit | `646c9cd3fcac2b0bca1a5cb23a392284c2a09636` ("Buku petunjuk ringkas gaya BAKAWAN: 60 halaman, stempel 1.2.14") |
| Pemicu | `push` 2026-11-05T05:14:16Z, selesai 2026-11-05T05:20:19Z (6 m 3 s) — push ini membawa **dua** commit sekaligus (`ac89217` kode+tes, `646c9cd` manual) |
| Kesimpulan | **`success`** |
| URL | https://github.com/dodorobe-bot/BAKERY/actions/runs/41972726233 |

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:01:45.40

Passed!  - Failed: 0, Passed:  83, Skipped: 0, Total:  83, Duration: 3 s    - BakeryDesktop.Domain.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 129, Skipped: 0, Total: 129, Duration: 6 s    - BakeryDesktop.Application.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 164, Skipped: 0, Total: 164, Duration: 42 s   - BakeryDesktop.Infrastructure.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 171, Skipped: 0, Total: 171, Duration: 2 m 51 s - BakeryDesktop.UI.Tests.dll (net8.0)

[MULAI] Clone sekali-pakai dengan core.autocrlf=true (konfigurasi yang merusak bukti): C:\Users\RUNNER~1\AppData\Local\Temp\seruni-checkout-bukti-0ab55719
[MULAI] 282 berkas bukti dibandingkan byte-per-byte dengan blob
[OK] -text berlaku (contoh docs/paket-bukti-2026-11-01/uji-cetak-1210/periksa-media-box.ps1)
[RINGKAS] 303 lulus, 0 gagal, 20 lewati (artefak .exe/.msi tidak dikomit) dari 5 manifest
```

- **UI naik dari 168 menjadi 171 = tepat tiga tes** yang ditambahkan `ac89217`
  (`InactiveIngredientStockTests`), sehingga CI memastikan perbaikan bahan nonaktif ikut dibangun,
  dijalankan, dan lulus di mesin kedua — bukan hanya di PC pengembang.
- Domain 83, Application 129, Infrastructure 164 tidak berubah, sesuai dugaan: perubahannya ada di
  `LocationStockRepository` + dua view React, dan jalur bacanya diuji di UI.Tests, bukan Infrastructure.Tests.
- Log CI tidak menyebut nama tes yang lulus (hanya rekap per majelis), jadi nama tiga tes itu diambil dari
  jalurnya sendiri di mesin ini dengan penyaring:

  ```text
  Passed BakeryDesktop.UI.Tests.Tests.InactiveIngredientStockTests.BahanDinonaktifkan_BarisStoknyaTetapMembawaHargaMaster [1 s]
  Passed BakeryDesktop.UI.Tests.Tests.InactiveIngredientStockTests.SisaStokBahanNonaktif_DapatDihabiskanLewatPenyetelanStok [304 ms]
  Passed BakeryDesktop.UI.Tests.Tests.InactiveIngredientStockTests.LayarOpname_MenawarkanBahanNonaktifDanBolehDisetelNol [1 s]
  Total tests: 3
  ```

  Perintahnya `dotnet test tests/UI.Tests/BakeryDesktop.UI.Tests.csproj -c Release --no-build --filter
  "FullyQualifiedName~InactiveIngredientStockTests" -l "console;verbosity=normal"`. Baris ini bukan bukti
  baru bahwa produknya benar — bukti terkuatnya justru **kontrol negatif** sebelumnya: dengan SELECT
  repository lama tes pertama gagal (`Expected … to be False, but found True`) dan dengan bundel
  `OpnameView` lama tes Playwright gagal `TimeoutException` menunggu baris bahan.
- **Run 5 tidak menaikkan angka keter-reproduksian (tetap 282/303) dan itu harus disebut apa adanya**:
  penjaga hanya memindai berkas di bawah `docs/paket-bukti-*`
  ([`scripts/uji-checkout-bukti.ps1:65`](../../../scripts/uji-checkout-bukti.ps1#L65) menyaring baris
  yang tidak memuat `docs/paket-bukti-`), sedangkan `ac89217` dan `646c9cd` menambah berkas di
  `src/`, `frontend/`, `tests/` dan `docs/manual/`. Artinya **PDF buku petunjuk 7.785.633 bita dan
  `petunjuk.html` tidak dibandingkan byte-per-byte oleh CI** — yang mereka buktikan di CI hanya bahwa
  build dan tes pada pohon yang memuatnya hijau. Nama ketiga tes itu justru disimpan sebagai berkas bukti
  baru, [`tes-nama-bahan-nonaktif.txt`](tes-nama-bahan-nonaktif.txt), sehingga run berikutnya harus
  menunjukkan **283 berkas** (naik satu dari 282) dan **304 lulus**. Prediksi itu sudah lebih dulu diuji
  di mesin ini pada commit bukti ini, `scripts/uji-checkout-bukti.ps1`:

  ```text
  [MULAI] 283 berkas bukti dibandingkan byte-per-byte dengan blob
  [OK] -text berlaku (contoh docs/paket-bukti-2026-11-01/uji-1212/run-sandbox.ps1)
  [RINGKAS] 304 lulus, 0 gagal, 20 lewati (artefak .exe/.msi tidak dikomit) dari 5 manifest
  ```

  Naik tepat satu berkas dan tidak ada berkas lama yang berubah byte; `.txt` ikut terlindungi aturan
  `docs/paket-bukti-*/** -text` (`git check-attr text` → `text: unset`), jadi `core.autocrlf=true`
  tidak merusak barisnya. CI masih harus mengulangi angka yang sama di klon miliknya sendiri.

## Batas yang harus disebut

- CI **tidak** membangun installer. Yang dibuktikannya adalah build Release + tes (544 pada run 2–4, 547
  pada run 5) + keter-reproduksian berkas bukti di mesin kedua; EXE/MSI 1.2.14 dan uji Sandbox
  1.2.13→1.2.14 tetap hanya ada di gerbang lokal.
- CI tidak menjalankan 169 pemeriksaan API `verify.ps1` maupun kontrol negatif otomatisasi — ia menjalankan
  `dotnet test`, bukan gerbang 33 langkah.
- **Yang hijau di CI belum tentu ada di paket.** Run 5 membuktikan pohon `646c9cd` sehat, tetapi berkas
  `desktop/` hasil `npm run build` di-gitignore (`.gitignore:12`) dan paket 1.2.14 sudah tercetak sebelum
  `ac89217`. Jadi perbaikan bahan nonaktif **tidak ada di EXE/MSI mana pun** sampai `build-pro.ps1`
  dijalankan lagi pada pohon ini.
- Langkah `Unggah log bila ada kegagalan` berstatus `skipped` pada run hijau karena dikondisikan
  `if: failure()`. Ini kondisi yang dirancang, bukan pemeriksaan yang lolos; SKIP tidak dihitung sebagai lulus.
- Log mentah run berada di luar repo (artifact hanya diunggah saat gagal). Berkas ini menyimpan baris-baris
  yang dikutip; sumber resminya adalah URL run di atas.
