# Verifikasi paket 1.2.13 — 2026-11-03

Dokumen ini mencatat apa yang **sudah dijalankan** dan apa yang **belum** untuk paket
Seruni 22 Bakery 1.2.13. Angka di bawah dibaca ulang dari berkas laporan, bukan dari ingatan.

## Putusan

**NOT READY.** Gerbang dan automasi lulus penuh pada konfigurasi Release, dan
pasang / peningkatan / copot di Windows Sandbox bersih sudah lulus 105/105 untuk jalur
1.2.12 → 1.2.13, tetapi perangkat fisik (printer thermal 80 mm, printer jaringan, pemindai
barcode, laci uang) belum diuji pada kertas dan perangkat sungguhan, Windows 10 belum diuji,
dan paket belum bertanda tangan.

## Artefak

| Berkas | Ukuran | SHA-256 | Tanda tangan |
|---|---|---|---|
| `Seruni22Bakery_Setup_v1.2.13.exe` | 42.672.292 | `c1a0af29bd8f5bf9c439cadef3c1d8c891da0c49a3271384192fd682c444ed9d` | NotSigned |
| `Seruni22Bakery_v1.2.13_x64.msi` | 50.416.132 | `eb3f40fb0fa2fe1fb7e09bee733db524a8b29fb55af367b1bd8d0df1f5afd132` | NotSigned |

Sumber: `dist/installer/SHA256SUMS.txt` dan `dist/installer/BUILD-INFO.json`.

Build dibungkus dari commit **`5993072`** dengan **pohon sumber bersih** (`pohonKotor=0`;
`kotorArtefakBuild=5` adalah laporan yang baru saja ditulis build itu sendiri). UpgradeCode
`B975F792-6F80-4D56-8A64-D274A044DCF1` sama dengan paket 1.2.4–1.2.12, jadi peningkatan
menggantikan pemasangan lama, bukan menumpuk.

Cara membangun ulang:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-pro.ps1 -SinkronkanBundelDesktop
```

## Yang dijalankan hari ini

| Pagar | Hasil | Bukti |
|---|---|---|
| `verify.ps1 -Configuration Release` | **32 langkah PASS, 0 gagal, 0 SKIP**, 239,9 s | `dist/verify/verify-20261103-172525.json` |
| xUnit unit + integrasi + UI/Playwright | **523 lulus, 0 gagal, 0 dilewati** (dijumlah dari log, bukan dari exit code) | `dist/verify/verify-20261103-172525.test.log` |
| Verifikasi perilaku lewat API nyata | **169 pemeriksaan, 0 gagal** — batch2 63, batch3 56, batch6 21, bug09 29 — host loopback + DB sementara | tabel `[PASS]` di laporan gerbang |
| `build-pro.ps1` | **12 langkah, 0 gagal, 0 dilewati, 1 peringatan** (penimpaan atas `-IzinkanTulisUlang`) | `dist/installer/BUILD-INFO.json` |
| `validate-paket.ps1` | **19 pemeriksaan, 0 gagal, 0 lewati, 0 peringatan** | `dist/installer/VERIFY-REPORT.json` |
| Isi payload MSI vs manifest publish | **393/393 berkas** cocok hash dan ukuran | `dist/publish-single.manifest.txt` |
| Publish bebas berkas terlarang | lulus — tidak ada basis data, kunci JWT, kredensial, atau berkas debug | `VERIFY-REPORT.json` |
| Kontrol negatif automasi (`uji-build-pro.ps1`) | **14/14 lulus, 0 SKIP** — baseline tetap hijau sebelum dan sesudah sabotase | [`docs/paket-bukti-2026-11-03/kontrol-1213/uji-build-pro-rilis-1213.json`](paket-bukti-2026-11-03/kontrol-1213/uji-build-pro-rilis-1213.json) |
| Kontrol negatif diulang terhadap skrip yang sudah di-commit | **14/14 lulus, 0 SKIP** — hash EXE/MSI setelah run tetap `c1a0af29…`/`eb3f40fb…`, jadi artefak rilis tidak tersentuh sabotase | [`docs/paket-bukti-2026-11-03/kontrol-1213/uji-build-pro-rilis-1213-ulang.json`](paket-bukti-2026-11-03/kontrol-1213/uji-build-pro-rilis-1213-ulang.json) |
| Pasang/peningkatan/copot di **Windows Sandbox bersih** | **105/105 lulus, 0 gagal** — 1.2.12 → 1.2.13 dengan data utuh, 393/393 berkas, tanpa migrasi baru, pagar turun versi dan kanal terbukti | [`docs/paket-bukti-2026-11-03/upgrade-1213-sandbox/run1/status.txt`](paket-bukti-2026-11-03/upgrade-1213-sandbox/run1/status.txt) |

Kontrol negatif membuktikan validator menolak: hash EXE salah, MSI tak tercantum, manifest payload
salah, MSI hilang, versi EXE tertukar, kunci di folder publish, laporan menyebut artefak hantu,
nama ber-`..` di SHA256SUMS, EXE hilang (tanpa crash), manifest hilang (tidak boleh jadi SKIP), dan
`JsonPath` di luar repo/temp. Penjaga versi terbit menolak build ulang dan **tidak** menghapus
laporan paket yang sudah ada.

## Perbaikan yang masuk 1.2.13 (tidak ada di paket 1.2.12)

1. **Keamanan jaringan** — host menolak alamat dengar di luar loopback, dari `--urls` maupun
   `Kestrel:Endpoints`, sebelum kunci JWT dan basis data dibuat; LAN hanya atas
   `SERUNI_ALLOW_LAN=1`. 20 tes unit + 1 tes proses yang juga memastikan tidak ada DB/kunci yang
   tertinggal. Tidak ada skrip atau instalator yang membuka port firewall; diperiksa sebagai pagar
   statis di gerbang.
2. **Konsistensi data** — nilai yang menempel pada stok produk jadi sama dengan nilai yang dipakai
   HPP; SPK tanpa unit baik membebani gaji/operasional; sisa pembulatan harga rata-rata masuk beban
   selisih persediaan.
3. **Backup** — retensi mengutamakan salinan yang sudah terverifikasi.
4. **Idempotensi** — kunci dari proses mati di tengah jalan kedaluwarsa 30 jam (UTC dihitung di
   SQL); baris selesai dibersihkan setelah 30 hari.
5. **Antar muka** — status perangkat dipakai ulang 15 s dan disegarkan saat pengaturan berubah;
   daftar piutang/utang dimuat bertahap dengan penanda "hanya baris yang termuat".
6. **Automasi** — laporan JSON tertelusur per run, jumlah tes dibaca dari log, bukti kegagalan
   disimpan di `dist/verify/bukti`, tiga pagar statis, `sign-paket` memilih versi dari laporan
   build (bukan abjad), `dotnet test` tanpa satu pun tes tercatat menggagalkan build, jalur
   `OutRoot` bertanda junction/symlink ditolak, dan hitungan "pohon kotor" memisahkan kode sumber
   dari artefak yang ditulis build.

Skema basis data: **18** — tidak ada migrasi baru, jadi pemasangan di atas data lama tidak
menyentuh struktur.

## Belum diuji untuk 1.2.13

- Cetak ke printer thermal 80 mm dan ke printer jaringan fisik; jalur ESC/POS raw terbukti
  sampai emulator loopback, termasuk dari biner hasil peningkatan (635 byte diperiksa).
- Potong kertas, pemindai barcode fisik (hanya lulus simulasi wedge 14/14 pada 1.2.12), laci uang.
- Pemasangan oleh pemilik di PC kasir sungguhan.
- Peningkatan dari **1.2.9 dan 1.2.10** langsung ke 1.2.13; yang diuji baru 1.2.12 → 1.2.13.
- Windows 10 x64 (Sandbox memakai tamu Windows 11 Enterprise build 26100).
- Tanda tangan Authenticode: 0 sertifikat terpasang, `signtool verify /pa` akan menolak.

## Sudah terhapus dari daftar "belum"

Pasang/peningkatan/copot di Windows Sandbox bersih **sudah** dijalankan hari ini dan lulus
105/105: peningkatan senyap 1.2.12 → 1.2.13 dengan sandi, produk, merek, dan logo tetap utuh;
393/393 berkas payload cocok dengan manifes; bundel frontend lama tersapu; tidak ada migrasi
baru di atas skema 18; EXE menolak kanal MSI, MSI lama menolak turun versi, dan copot tidak
menghapus folder data pengguna. Lihat
[`docs/paket-bukti-2026-11-03/upgrade-1213-sandbox/README.md`](paket-bukti-2026-11-03/upgrade-1213-sandbox/README.md).

## Publikasi 2026-11-03

- Tag annotated **`v1.2.13`**: objek `ff09aa57eb2fc1e8c901f4989a958f2bea6009fd` menunjuk commit
  `bbe22e844642918abb2d6ba8ee304bee90a9860d` dan **tidak** mengubah
  tag `v1.2.9`/`v1.2.10`/`v1.2.12`.
- `main` dan tag di-push ke `origin` (fast-forward `107559c..bbe22e8`, 14 commit, tanpa force);
  sesudah `git fetch`, `origin/main` = `HEAD` dan selisih 0/0.
- **Release GitHub:** <https://github.com/dodorobe-bot/BAKERY/releases/tag/v1.2.13> — judul
  "Seruni 22 Bakery 1.2.13 — NOT READY", `draft=false`, `prerelease=false`, `createdAt`
  `2026-11-03T11:25:51Z`, `publishedAt` `2026-11-03T11:36:30Z`. Release ini sekarang menyandang
  label **Latest**; rilis `v1.2.12` tetap ada dan tidak diubah.
- Aset terunggah dan **digest SHA-256 yang dihitung GitHub sama dengan hash lokal**:
  EXE `c1a0af29bd8f5bf9c439cadef3c1d8c891da0c49a3271384192fd682c444ed9d` (42.672.292 bita),
  MSI `eb3f40fb0fa2fe1fb7e09bee733db524a8b29fb55af367b1bd8d0df1f5afd132` (50.416.132 bita),
  `SHA256SUMS.txt` `a71dd37d6e24965c37548feb7c4f544b9c64a9c9b3d8ee08557a90c6af435341` (196 bita).
- Ulang-alih unduhan: `SHA256SUMS.txt` diunduh dari URL publik → **byte-identik** dengan berkas
  lokal (`cmp` bersih); MSI diunduh penuh dari URL publik → 50.416.132 bita, SHA-256
  `eb3f40fb…` sama dengan paket hasil build. Halaman rilis menjawab 200 tanpa login.
- Aset keempat, buku manual: `Seruni22-Buku-Manual-Pengguna.pdf`, 9.316.970 bita, SHA-256
  `eec028dc8f08c6ee6e048bdab109ce0f91a8ee070bbd0706a6ddd68b2ec116b0` (digest yang dihitung
  GitHub sama dengan berkas lokal), 102 halaman. Aset diberi label eksplisit
  **"Buku Manual Pengguna — disusun untuk versi 1.2.12 (bukan 1.2.13)"** karena stempel versi di
  sampul, Bab 0 (nama berkas pemasangan `Seruni22Bakery_Setup_v1.2.12.exe`) dan Bab 39
  "Batasan Versi 1.2.12" masih mengikuti 1.2.12; buku ini **tidak** dibangun ulang untuk 1.2.13.
  Unduhan ulang dari URL publik byte-identik dengan berkas lokal (`cmp` bersih) dan dibuka
  sebagai 102 halaman. Perilaku 1.2.13 yang belum tertulis di dalamnya — pagination piutang/utang
  di layar Keuangan (`Muat <n> pesanan lagi`, `Muat <n> dokumen lagi`,
  `Tampilkan 100 dokumen berikutnya (<n> tersisa)`, `· hanya baris yang termuat`) dan cache status
  perangkat 15 detik — dicatat di badan rilis, bukan diklaim sebagai isi buku.
- Yang **tidak** berubah karena publikasi ini: paket tetap **NotSigned**, dan daftar "Belum
  diuji" di atas tetap berlaku — label Latest bukan persetujuan produksi.

## Batas bukti

Peralatan uji berada pada PC dengan RAM 3,7 GB; hasil "lulus" di atas adalah hasil satu mesin.
Angka 105/105 yang tercatat untuk 1.2.13 berasal dari run sandbox 1.2.12 → 1.2.13 hari ini;
angka 105/105 milik 1.2.12 (jalur 1.2.9 → 1.2.12) adalah bukti untuk paket itu sendiri.
