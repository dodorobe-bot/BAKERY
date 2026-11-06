# Verifikasi paket 1.2.15 — 2026-11-06

Dokumen ini mencatat apa yang **sudah dijalankan** dan apa yang **belum** untuk paket
Seruni 22 Bakery 1.2.15. Angka di bawah dibaca ulang dari laporan di `dist/`, log di
`C:\tmp\rilis-1215`, dan `status.txt` sandbox pada 6 November 2026.

## Putusan

**NOT READY.** Semua yang bisa dibuktikan di mesin pengembang ini sudah dibuktikan dan seluruhnya
hijau: gerbang Release 33 langkah, **679 tes**, 169 pemeriksaan API, `build-pro` 12 langkah dengan
393/393 payload, validasi paket 19/19, kontrol negatif automasi 14/14, dan pasang/peningkatan/copot
**1.2.14 → 1.2.15** di Windows Sandbox bersih **107/107**. Yang menahan putusan tetap empat hal
yang tidak bisa dikerjakan dari mesin ini — cetak pada printer kasir fisik, pemindai barcode fisik,
laci uang, dan uji di Windows 10 — ditambah tanda tangan Authenticode yang sertifikatnya belum ada,
dan perintah penambalan data warisan yang menunggu persetujuan pemilik.

**Berbeda dari 1.2.14, paket ini memuat perubahan besar:** skema naik **18 → 23** dengan lima
migrasi (019–023), sehingga jalur peningkatan benar-benar menjalankan migrasi. Uji sandbox karena
itu memeriksa migrasi **x5**, bukan x0, dan memastikan data, sandi, merek, serta 393 berkas payload
tetap utuh sesudahnya.

CI `Gerbang build + tes` di GitHub Actions menjalankan pohon ini pada commit `0482e51`
(pohon sama dengan artefak; bedanya hanya dokumen dan PDF): **`success`** dalam 4m28s,
**679 tes lulus, 0 gagal, 0 dilewati**, keter-reproduksian **363 lulus, 0 gagal, 24 lewati**
dari 6 manifest ([run 42198387598](https://github.com/dodorobe-bot/BAKERY/actions/runs/42198387598)).
Angka lain di dokumen ini berasal dari mesin pengembang.

## Artefak

| Berkas | Ukuran | SHA-256 | Tanda tangan |
|---|---|---|---|
| `Seruni22Bakery_Setup_v1.2.15.exe` | 42.742.848 | `99f6b7a4014579a216a96480705daa6c4f83067c9e3e8a8290ad143dae93dc83` | NotSigned |
| `Seruni22Bakery_v1.2.15_x64.msi` | 50.485.764 | `77f36f55260d5cec0865ea0d9ff3be9d6df7fb7038d573fa304054375ec6e975` | NotSigned |

Sumber: `dist/installer/BUILD-INFO.json`, `dist/installer/SHA256SUMS.txt`, dan
`dist/installer/VERIFY-REPORT.json`.

Dibangun **6 November 2026 pukul 15:15:06** dari commit **`a43ca84babb0df133427f4500a25b1f2c44156c1`**
dengan `pohonKotor=0`. Langkah `penjaga versi terbit` memberi **WARN** karena artefak 1.2.15 sudah
ada di `dist\installer` dan ditimpa atas permintaan `-IzinkanTulisUlang` — bukan galat.

`UpgradeCode` `B975F792-6F80-4D56-8A64-D274A044DCF1` sama dengan paket 1.2.4–1.2.14, jadi
peningkatan menggantikan pemasangan lama. `ProductCode` 1.2.15 adalah
`{B217D2A7-FA77-4273-8B74-DB8760518A2F}` (1.2.14: `{75FF905B-6F22-4051-A437-5F9362F0EA2B}`).

Cara membangun ulang:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-pro.ps1 -SinkronkanBundelDesktop
```

## Yang terbukti masuk paket (bukan hanya ada di sumber)

| Bukti | Nilai |
|---|---|
| `Seruni22Bakery.Host.dll` hasil pasang = manifes publish | `72347b2fc27b9d473c3ef4978c99b92416cfc650722e57eb49a50e941b4f1190` (386.048 bita), **sama byte** dengan hasil `Get-FileHash` di dalam sandbox |
| Hash berkas yang sama pada paket 1.2.14 | `f710ccc9a0430ad2dfe0528b535bd18495314ec77a56e8c54389032f20cfcc77` — **berbeda**, jadi biner host benar-benar berganti |
| Bundel web | `index-BmCxycbD.js` + `index-CUfTbA7B.css` (1.2.14) → `index-B1TuJsgD.js` + `index-DOW8Zhjg.css` (1.2.15); total berkas `wwwroot\assets` tetap dua |
| Payload MSI | 393/393 berkas cocok hash dan ukuran terhadap `dist/publish-single.manifest.txt` |
| Skema | 23; lima migrasi 019–023 dijalankan pada basis data 1.2.14 |

## Yang dijalankan 6 November 2026

| Pagar | Hasil | Bukti |
|---|---|---|
| `verify.ps1 -Configuration Release` | **33 langkah PASS, 0 gagal, 0 SKIP**, 361,8 s, keluar 0, konfigurasi Release, commit `a43ca84…` | `dist/verify/verify-20261106-150156.json` |
| xUnit unit + integrasi + UI/Playwright | **679 lulus, 0 gagal, 0 dilewati** — Domain 136, Application 139, Infrastructure 207, UI 197 (dijumlah dari baris `Passed!`) | `dist/verify/verify-20261106-150156.test.log` |
| Verifikasi perilaku lewat API nyata | **169 pemeriksaan, 0 gagal** — batch2 63, batch3 56, batch6 21, bug09 29 — host loopback + basis data sementara | langkah `verifikasi verify-*.mjs` di laporan gerbang |
| Keter-reproduksian berkas bukti | **304 lulus, 0 gagal, 20 lewati** dari 5 manifest; 20 yang dilewati adalah artefak `.exe`/`.msi` yang memang tidak dikomit — dihitung **lewati**, bukan lulus | langkah `keter-reproduksian berkas bukti` |
| `build-pro.ps1` | **12 langkah, 0 gagal, 0 dilewati, 1 peringatan** (peringatan = timpa ulang artefak 1.2.15 yang diminta) | `dist/installer/BUILD-INFO.json` |
| `validate-paket.ps1` | **19 pemeriksaan, lulus 19, gagal 0, lewati 0, peringatan 0** | `dist/installer/VERIFY-REPORT.json` |
| Publish bebas berkas terlarang | lulus — tidak ada basis data, kunci JWT, kredensial, atau berkas debug | `VERIFY-REPORT.json` |
| Kontrol negatif automasi (`uji-build-pro.ps1 -Version 1.2.15`) | **14/14 lulus, 0 gagal, 0 dilewati** pada paket hari ini pukul 16:14 — baseline hijau sebelum dan sesudah sabotase, penjaga versi terbit menolak build ulang 1.2.15 (pemicu: artefak di folder keluaran) dan tidak menghapus laporan yang ada | [`docs/paket-bukti-2026-11-06/kontrol-1215/uji-build-pro-rilis-1215.json`](paket-bukti-2026-11-06/kontrol-1215/uji-build-pro-rilis-1215.json) |
| Pasang/peningkatan/copot **1.2.14 → 1.2.15** di Windows Sandbox bersih | **107 lulus, 0 gagal** dalam satu run (15:40–16:04), tanpa jaringan, tamu 1.536 MB | [`docs/paket-bukti-2026-11-06/upgrade-1214-1215-sandbox/run2/status.txt`](paket-bukti-2026-11-06/upgrade-1214-1215-sandbox/run2/status.txt) |
| CI `Gerbang build + tes` (mesin kedua) | **`success`** pada commit `0482e51`, 4m28s — **679 tes lulus, 0 gagal, 0 dilewati**; keter-reproduksian **363 lulus, 0 gagal, 24 lewati** dari 6 manifest | [run 42198387598](https://github.com/dodorobe-bot/BAKERY/actions/runs/42198387598) |

## Sandbox 1.2.14 → 1.2.15: dua run, satu di antaranya merah karena harness

- **Run 1** (`run1/`, kit v2, 15:21–15:38): **105 lulus, 2 gagal**. Kedua kegagalan adalah cacat
  harness, bukan produk: (a) emulator ESC/POS diberi batas 10 detik dan `Start-Job` di tamu
  1.536 MB belum siap, padahal 635 bita tetap diterima dan semua pemeriksaan byte lulus;
  (b) regex penjaga versi masih mencari `1\.2\.13` karena port hanya mengganti teks biasa, padahal
  log tamu sudah berbunyi `Pagar versi: HKCU Version=1.2.15 lebih baru dari paket 1.2.14.`
- Harness diperbaiki: batas emulator 60 detik, dan penggantian versi ber-*escape* memakai sentinel
  plus pagar. **Produk tidak diubah dan paket tidak dibangun ulang.**
- **Run 2** (`run2/`, kit v3, 15:40–16:04): **107 lulus, 0 gagal**. Kedua pemeriksaan itu kini
  PASS. Rekap: `| rekap | PASS | lulus=107 gagal=0 total=107`.
- Rincian jujur kedua run ada di
  [`docs/paket-bukti-2026-11-06/upgrade-1214-1215-sandbox/CATATAN-RUN.md`](paket-bukti-2026-11-06/upgrade-1214-1215-sandbox/CATATAN-RUN.md).

## Isi 107 pemeriksaan sandbox (yang paling penting)

- **Mesin bersih** terbukti: belum ada penanda kanal di HKCU/HKLM; manifes 393 baris terbaca;
  393 berkas payload cocok hash+ukuran setelah pasang; `Seruni22Bakery.Host.dll` sama byte dengan
  manifes.
- **Peningkatan senyap MSI 1.2.14 → 1.2.15** exit 0; `WIX_UPGRADE_DETECTED` memuat ProductCode
  1.2.14 `{75FF905B-…}`; `RemoveExistingProducts` berjalan; entri **1.2.14** hilang dan yang
  tersisa tepat satu: **Seruni 22 Bakery 1.2.15**.
- **Data pemilik bertahan**: sandi hasil ganti, produk (`Roti Uji 1215`, harga 15.000), merek
  (`Toko Uji Sandbox 1215`), dan logo tetap sama sesudah peningkatan; `mustChangePassword=False`;
  periksa-diri **18 pemeriksaan, 0 gagal**.
- **Skema 18 → 23 dengan lima migrasi** (`migrasi dijalankan x5`), `versi skema` melaporkan
  `terpasang 23, skema terbaru yang ikut terkemas 23. semua migrasi sudah dijalankan.`, dan
  endpoint **`GET /api/periods`** hidup sesudah migrasi 022 (`daftar periode: 1`).
- **Aset web lama tersapu**: `index-BmCxycbD.js` + `index-CUfTbA7B.css` terbukti ada sebelum
  peningkatan dan `index-B1TuJsgD.js` + `index-DOW8Zhjg.css` ada sesudahnya, total tetap dua.
- **ESC/POS raw dari biner hasil peningkatan**: emulator loopback port 9100 menerima **635 bita**;
  diawali `ESC @` (`0x1B 0x40`), diakhiri `ESC d 3` (`0x1B 0x64 0x03`), nominal contoh
  `2 x Rp 15.000 … Rp 30.000` hadir dan `Rp 300` lama tidak ada lagi, `GS V` tidak dikirim saat
  pemotongan mati, riwayat cetak mencatat pekerjaan `Test` berstatus `Printed`, dan pilihan
  perangkat bertahan setelah host di-restart.
- **Pagar**: EXE 1.2.15 menolak berjalan di atas instalasi MSI; MSI 1.2.14 ditolak di atas 1.2.15
  (exit 1603, alasan turun versi tercatat); EXE 1.2.14 asli ditolak di atas EXE 1.2.15
  (`Pagar versi`); MSI 1.2.15 ditolak selama kanal EXE ada (exit 1603, alasan kanal tercatat) dan
  tidak meninggalkan folder program maupun entri ARP.
- **Copot EXE** menghapus folder program, pintasan, entri ARP, dan penanda HKCU — tetapi folder
  data pengguna dan berkas basis data di dalamnya **tetap ada**. Tidak ada proses yatim.

Distribusi hasil: 107 baris `PASS` ditambah 1 baris rekap `PASS`; ada pula baris `INFO` yang hanya
mencatat aksi harness (menghentikan host). Tidak ada `FAIL`, tidak ada `SKIP`.

## Yang TIDAK dibuktikan (dan bukan kegagalan harness)

| Belum | Kenapa | Siapa yang menutup |
|---|---|---|
| Cetak ke printer termal/jaringan sungguhan | tidak ada perangkat fisik di mesin ini; emulator adalah perangkat lunak uji di dalam tamu | pemilik, di PC kasir — PASS hanya bila kertasnya dilihat |
| Pemindai barcode fisik | hanya simulasi keyboard wedge yang bisa dibuat di sini | pemilik |
| Laci uang | tidak ada jalur `ESC p` yang dikirim (sudah diverifikasi sebagai **negatif**) | pemilik setelah printer lulus |
| Uji di Windows 10 | mesin ini Windows 11; tidak ada VM Windows 10 yang bisa dijalankan dari sesi ini | pemilik / mesin kedua |
| Tanda tangan Authenticode | tidak ada sertifikat; semua paket dilaporkan `NotSigned` | pemilik (keputusan biaya, ditunda sejak 1.2.4) |
| Build installer di CI | workflow CI sengaja tidak menjalankan `build-pro.ps1`/Inno/WiX | pemilik, bila ingin paket terbangkitkan di cloud |
| Penambalan data warisan di basis data nyata | `--rekonsiliasi-stok-bahan --terapkan` menyentuh saldo neraca pemilik; uji kering tidak pernah dipakai di DB nyata | persetujuan pemilik |
| Jalur peningkatan dari 1.2.9/1.2.10/1.2.12 langsung ke 1.2.15 | yang diuji di mesin bersih adalah 1.2.14 → 1.2.15; lompatan lama belum diuji dengan migrasi 019–023 | pemilik, bila diperlukan — naik berurutan lebih aman |

## Catatan keterbatasan

- Payload EXE tidak dibandingkan berkas per berkas; hanya MSI yang diekstrak administratif.
- Harness tidak memasang paket ke sistem; pemasangan dilakukan oleh skrip kit sandbox.
- Status tanda tangan hanya dilaporkan, tidak dibuat.
- Mesin pengembang ini punya 3.746 MB RAM; sandbox tamu memakai 1.536 MB. Run 107/107 tercapai
  pada kondisi itu, dengan dua run berturut-turut pada hari yang sama.
- Penjaga keter-reproduksian hanya memindai `docs/paket-bukti-*`; PDF manual **tidak**
  dibandingkan byte-per-byte.

## Berkas bukti

- `docs/paket-bukti-2026-11-06/upgrade-1214-1215-sandbox/run1/` dan `run2/` — `status.txt`,
  `validasi.log`, `msi-*.log`, `exe-*.log`, `host-*.log`, `escpos-diterima.bin`, `selesai.txt`.
- `docs/paket-bukti-2026-11-06/upgrade-1214-1215-sandbox/` — `CATATAN-RUN.md`, `PANDUAN-KIT.md`,
  `run-sandbox.ps1`, `uji-1214-1215.ps1`, `hash-publish-1215.txt`, `port-kit-1215.py`,
  `KIT-SHA256SUMS.txt`, `runner-run1.log`, `runner-run2.log`. Kit dirakit di
  `C:\tmp\kit-uji-1214-1215-v3` (paket `.exe`/`.msi` tidak dikomit).
- `docs/paket-bukti-2026-11-06/kontrol-1215/uji-build-pro-rilis-1215.json` + `.log`.
- `dist/verify/verify-20261106-150156.json` + `.test.log`, `dist/installer/BUILD-INFO.json`,
  `dist/installer/VERIFY-REPORT.json`, `dist/installer/SHA256SUMS.txt`.
- Dokumen pengguna distempel ulang dan dicetak pada 2026-11-06: `docs/manual/manual.html` dan
  `docs/manual/petunjuk.html` menyebut 1.2.15; `Seruni22-Buku-Manual-Pengguna.pdf`
  (131 halaman, 58 gambar, 45 entri daftar isi, 12.997.441 bita) dan
  `Seruni22-Buku-Petunjuk-Pengguna.pdf` (60 halaman, 45 gambar, 7.785.632 bita). Pemeriksaan
  halaman, daftar isi, kepala/kaki, dan gambar dijalankan sendiri oleh alat cetak dan lulus.
