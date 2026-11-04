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
| `verify.ps1 -Configuration Release` | **32 langkah PASS, 0 gagal, 0 SKIP**, 239,9 s | `dist/verify/verify-20261103-172525.json` — lihat "Jejak versi gerbang": run ini berjalan pada pohon berversi **1.2.12** |
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

## Jejak versi gerbang (temuan + koreksi, 4 November 2026)

Laporan gerbang yang selama ini dikutip sebagai bukti 1.2.13 — `verify-20261103-172525.json` —
ternyata menandai commit `18f47fcb1e1f064eb083a9487b4991dcfdcc4697`, dan pada commit itu
`src/Host.Service/BakeryDesktop.Host.Service.csproj` masih berbunyi `<Version>1.2.12</Version>`.
Angkanya benar, **label versinya yang tidak** bisa dibuktikan oleh laporan itu. Rantai waktu lokal
(UTC+7), semuanya dibaca dari berkas, bukan dari ingatan:

| Waktu | Commit | `<Version>` csproj | Peristiwa |
|---|---|---|---|
| 3 Nov 17:24:44 | `18f47fc` | **1.2.12** | commit terakhir sebelum naik versi |
| 3 Nov 17:25:25 → 17:29:25 | `18f47fc` | 1.2.12 | gerbang Release: LULUS, 32 PASS, 523 tes, 239,9 s, `pohonKotor` 3 |
| 3 Nov 17:35:48 | `cbeae7d` | **1.2.13** | "Rilis: versi 1.2.13 di tiga sumber" |
| 3 Nov 17:37:14 | `5993072` | 1.2.13 | commit tempat `build-pro.ps1` berjalan (17:41:45, laporan `versi` 1.2.13, `pohonKotor` 0) |
| 3 Nov 18:25:51 | `bbe22e8` | 1.2.13 | target tag annotated `v1.2.13` |
| 4 Nov 08:45:06 → 08:50:41 | `b5dd62b` | **1.2.13** | gerbang Release **ulang** pada pohon berversi 1.2.13 |

Run ulang: hasil **LULUS**, `kodeKeluar` 0, **32 langkah PASS / 0 gagal / 0 SKIP**,
**523 tes lulus / 0 gagal / 0 dilewati**, pemeriksaan API 63 + 56 + 21 + 29 = **169, 0 gagal**,
durasi 335,1 s, `pohonKotor` 0, commit `b5dd62b4f4d5976716cf59bda05e220a934eb601`. Angka ini
**sama persis** dengan run 3 November — yang berubah hanya status buktinya: laporan sekarang
dibuat pada pohon yang benar-benar bertuliskan 1.2.13. Salinannya ada di
[`docs/paket-bukti-2026-11-04/gerbang-release-pohon-1213.json`](paket-bukti-2026-11-04/gerbang-release-pohon-1213.json)
(8.378 bita, SHA-256 `d9f09f4896447d38…`), karena `dist/verify/` diabaikan git
(`.gitignore:7`) dan laporan di sana tidak menjadi bukti repo.

Dua catatan pelengkap supaya tidak ada angka yang dibaca keliru:

- Run lama mencatat `pohonKotor: 3` (tiga berkas sumber belum di-commit saat gerbang jalan); run
  ulang `pohonKotor: 0`. Tidak ada langkah yang berubah status di antara keduanya.
- `build-pro.ps1` mencetak "12 langkah" sedangkan `BUILD-INFO.json` berisi **11** entri `langkah`.
  Bukan salah catat: langkah ke-12 (`laporan build`, tercatat di `scripts/build-pro.ps1:412`) baru
  didaftarkan **sesudah** berkas laporan ditulis, jadi ia tidak bisa memuat dirinya sendiri.
  Hitungan di badan rilis dan tabel di atas (12 langkah, 0 gagal, 1 peringatan) mengikuti
  ringkasan konsol, dan keduanya benar.

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
4. **Idempotensi** — kunci dari proses mati di tengah jalan kedaluwarsa 30 menit (`MenitPesananBasi`
   di `IdempotencyRepository.cs:11`, dihitung di SQL); baris selesai dibersihkan setelah 30 hari.
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
  **Berlaku sampai 4 November 2026 06:23 waktu lokal** (GitHub mencatat `2026-11-03T23:22:59Z`):
  paragraf di atas menggambarkan rilis saat pertama terbit. Buku dibangun ulang malam harinya
  (3 November 22:06) dan asetnya baru diganti pagi 4 November, jadi 9.316.970 bita /
  `eec028dc…` / 102 halaman tidak lagi cocok dengan unduhan publik hari ini — lihat subbab
  "Publikasi aset" di bawah.
- Yang **tidak** berubah karena publikasi ini: paket tetap **NotSigned**, dan daftar "Belum
  diuji" di atas tetap berlaku — label Latest bukan persetujuan produksi.

## Buku manual edisi 1.2.13 (dibangun ulang 3 November 2026, 22:06)

Aset yang terpublikasi di atas masih buku berstempel 1.2.12. Naskahnya lalu dibangun ulang
menjadi edisi 1.2.13 dengan `python docs/manual/cetak_manual.py`; hasil lokal:

- `docs/manual/Seruni22-Buku-Manual-Pengguna.pdf`, **9.793.167 bita**, SHA-256
  `77b5e042d9698f134bc1d99e8094292bfea5c7337d7b809a0eccac8cbcc11dac`, **107 halaman**
  (edisi 1.2.12: 102 halaman, 9.316.970 bita — halaman bertambah murni karena isi baru).
- Verifikasi mandiri skrip lulus seluruhnya: 45 penanda bab stabil antara cetakan pertama dan
  akhir, 45 entri daftar isi cocok dengan halaman aktual (diperiksa ulang terpisah:
  `Bab 0 → 6`, `Bab 39 → 96`, `Lampiran E → 106`), nomor kaki "Halaman n dari 107" di halaman
  2..107 tanpa nomor di sampul, dan 42 objek gambar dengan rujukan `Gambar 1..41` lengkap.
- Stempel versi kini **dibaca dari** `<Version>` di
  `src/Host.Service/BakeryDesktop.Host.Service.csproj` — sumber yang sama dengan penjaga versi di
  `scripts/build-pro.ps1` — bukan lagi tiga tulisan `1.2.12` yang dihardcode. Kepala halaman
  tercetak bertuliskan "Versi aplikasi 1.2.13" (diperiksa pada halaman 2 dan halaman terakhir),
  metadata `/Subject` dan `/Keywords` ikut versi yang sama, dan skrip berhenti lebih dulu bila
  judul `manual.html` tidak memuat versi paket.
- Isi baru yang ditulis dari sumber, bukan dari ingatan: tabel "Yang berubah pada versi 1.2.13",
  subbab 0.3 tentang `NetworkExposureGuard` (pesan verbatim *Alamat dengar "…" ditolak: aplikasi
  ini hanya untuk PC ini.* dan `SERUNI_ALLOW_LAN=1`), 2.6 tentang penolakan server yang kini
  tampil di dalam form beserta pesan bawaannya, kedaluwarsa kunci idempotensi **30 menit** dan
  masa simpan 30 hari (`IdempotencyRepository.cs:11,15`), 1-006/5-006 pada penyelesaian SPK
  (`AccountingService.cs:195-240`), muat bertahap piutang/utang (`FinanceView.jsx`,
  `bertahap.js:6,8`), cache status perangkat 15 detik (`statusPerangkat.js:6`), kunci per baris
  opname, dan retensi cadangan mengutamakan salinan terverifikasi (`BackupRetention.cs:7-19`
  beserta keputusan pembersihannya di bawahnya).
  Enam pesan baru masuk Lampiran D; Bab 39 bertambah baris "Akses dari PC lain" dan
  "Peningkatan dari versi lama".
- **Tangkap layar masih tangkapan 1.2.12** — ditulis apa adanya di baris "Sumber gambar" dan di
  kolofon. Kontrol 1.2.13 bersifat kondisional (hanya muncul saat daftar panjang, saat penolakan,
  atau saat pengaturan perangkat disimpan) sehingga tidak akan terlihat pada data contoh;
  nama dan letaknya ditulis persis seperti di layar.
- Koreksi dokumen: `CHANGELOG.md` menyebut kunci idempotensi kedaluwarsa setelah "30 jam",
  padahal `MenitPesananBasi = 30` dipakai sebagai `-30 minutes` di SQL. Sudah dibetulkan menjadi
  30 menit. Tidak ada migrasi baru antara 1.2.12 dan 1.2.13, sehingga bentuk basis data di buku
  tetap benar.

### Publikasi aset (izin pemilik, 4 November 2026 06:23–06:26 WIB)

Waktu pada subbab ini ditulis WIB (UTC+7), waktu mesin tempat perintah dijalankan. GitHub
mencatatnya sebagai UTC: `created_at` aset `2026-11-03T23:22:59Z` (= 4 Nov 06:22:59) dan
`updated_at` `2026-11-03T23:25:53Z` (= 06:25:53). Versi sebelumnya dari judul ini menulis
"3 November 23:23" — itu angka UTC yang disalahlabeli WIB.

- `gh release upload v1.2.13 docs/manual/Seruni22-Buku-Manual-Pengguna.pdf --clobber` keluar 0.
  Aset pengganti: id numerik **707468203**, nama tetap `Seruni22-Buku-Manual-Pengguna.pdf`,
  **9.793.167 bita**, digest yang dihitung GitHub `sha256:77b5e042…1dac` — **sama persis**
  dengan hash berkas lokal. Buku lama 1.2.12 (`eec028dc…`, 102 halaman) tidak ada lagi di rilis.
  Tiga aset lain tidak tersentuh: EXE `c1a0af29…`, MSI `eb3f40fb…`, `SHA256SUMS.txt` `a71dd37d…`.
  `--clobber` menghapus lalu membuat ulang aset, jadi label lama ikut hilang. Label dipasang
  kembali lewat `PATCH /repos/…/releases/assets/707468203` (id **numerik**, bukan id GraphQL
  `RA_kwDO…` — id GraphQL mengembalikan 404): *Buku Manual Pengguna - edisi 1.2.13 (107 halaman;
  tangkapan layar dari aplikasi 1.2.12)*.
- **Unduh tanpa login** (`curl -sSL`, tanpa token, pada `browser_download_url`): HTTP 200,
  9.793.167 bita, SHA-256 `77b5e042…1dac` — identik dengan berkas di pohon kerja. Bukti
  pihak yang menerima rilis mendapatkan isi yang sama dengan yang diverifikasi di atas.
- Yang **belum** diubah pada saat itu: badan rilis `v1.2.13` — izin yang diberikan baru unggah
  aset, jadi suntingan teks publik berikutnya menunggu persetujuan terpisah.

### Suntingan badan rilis pertama (izin pemilik, 4 November 2026 07:16 WIB)

`gh release edit v1.2.13 --notes-file` keluar 0, lalu diulang dengan
`PATCH /repos/dodorobe-bot/BAKERY/releases/432611731` memakai `--input` berkas JSON (jalur
`/c/tmp/...` tidak dikenali binari Windows dan `-F "body@berkas"` bukan sintaks gh; `-F`
memakai `key=@berkas` hanya untuk berkas yang berisi JSON utuh). Isi badan: 7.089 → 7.415 bita,
66 → 65 baris, `updated_at` `2026-11-04T00:16:26Z`; judul `Seruni 22 Bakery 1.2.13 — NOT READY`,
`tag_name`, `draft=false`, `prerelease=false`, dan keempat aset beserta label tidak berubah
(dipindai ulang sesudah sunting). Tiga koreksi, dan hanya tiga:

1. Paragraf **"Aset tambahan — buku manual"** diganti: dulu 9.316.970 bita / `eec028dc…` /
   102 halaman / "Buku ini disusun untuk versi 1.2.12", sekarang 9.793.167 bita / `77b5e042…` /
   107 halaman / edisi 1.2.13 dengan cara stempel versi dibaca dari `<Version>` csproj, daftar
   delta yang sudah tertulis, dan keterangan jujur bahwa **tangkap layar masih 1.2.12** beserta
   alasannya; edisi lama disebut sebagai "sudah digantikan". Tiga butir "Yang belum tertulis di
   dalamnya" dihapus karena isinya sekarang memang tertulis di buku; kalimat pengantar blok
   perintah periksa hash tetap ada ("Periksa dulu sebelum menjalankan:").
2. Baris "Idempotensi" menyebut kunci kedaluwarsa **30 jam** — salah, sama seperti
   `CHANGELOG.md` yang sudah dibetulkan. Sumber: `MenitPesananBasi = 30` pada
   `src/Infrastructure.Sqlite/Repositories/IdempotencyRepository.cs:11`, dipakai sebagai
   `-30 minutes`. Kini **30 menit**. Daftar perbaikan di dokumen ini (butir 4) dan bullet yang
   sama ikut dibetulkan.
3. Daftar "Dokumen" menyebut "Manual pengguna (99 halaman, PDF)" menjadi "Manual pengguna edisi
   1.2.13 (107 halaman, PDF; tangkapan layar dari aplikasi 1.2.12)".

Pemeriksaan sesudah terbit (`gh release view --json body`, dibaca ulang dari GitHub): nol
kemunculan `9.316.970`, `eec028dc8f08`, `30 jam`, `(99 halaman`, dan "disusun untuk versi
1.2.12"; `77b5e042` satu kali, `9.793.167` satu kali, "107 halaman" dua kali, blok pagar
perintah utuh. Salinan badan rilis yang terbit disimpan sebagai bukti di
[`docs/paket-bukti-2026-11-03/badan-rilis-v1.2.13-terbit.md`](paket-bukti-2026-11-03/badan-rilis-v1.2.13-terbit.md)
— 7.415 bita, SHA-256 `82a557d61f7568dd…`, dibaca **kembali dari GitHub** sesudah sunting, bukan
disalin dari naskah yang dikirim, jadi salinan itu membuktikan apa yang benar-benar tampil.
Ukuran bita pada subbab pertama ini diukur pada teks LF — blob edisi 07:16 adalah 7.415 bita LF,
setara 7.480 bita dalam bentuk CRLF yang sesungguhnya dijawab GitHub; selisih 65 bita itu tepat
sebanyak baris dan dijelaskan di subbab berikutnya.

### Suntingan badan rilis kedua (izin pemilik, 4 November 2026 08:59 WIB)

`PATCH /repos/dodorobe-bot/BAKERY/releases/432611731` dengan `--input` berkas JSON keluar 0;
GitHub mencatat `updated_at` `2026-11-04T01:59:23Z` (= 08:59:23 WIB). Isi badan yang terbit:
**7.949 bita**, 65 baris isi + satu baris kosong di ekor (pencetak `gh release view -q .body`
menambahkan satu `newline`; badan yang dikirim 7.883 bita LF), SHA-256 `e011ebe7c45415b1…`.
Judul `Seruni 22 Bakery 1.2.13 — NOT READY`, `tag_name`, `draft=false`, `prerelease=false`, dan
keempat aset (id 707468203 / 705373238 / 705373244 / 705373239, ukuran dan label) diperiksa ulang
sesudah sunting — tidak ada yang berubah.

Dua koreksi, keduanya konsekuensi temuan di subbab "Jejak versi gerbang":

1. Butir pertama "Bukti yang sudah dijalankan" kini menyebutkan **siapa yang menjalankan bukti
   gerbang pada versi apa**: run 3 November memakai commit `18f47fc` saat csproj masih 1.2.12
   (versi 1.2.13 baru naik di `cbeae7d`, 3 November pukul 17:35 lokal) dan angka yang sama
   dibuktikan ulang 4 November pada commit `b5dd62b` dengan `pohonKotor` 0, dirujuk ke
   `docs/paket-bukti-2026-11-04/gerbang-release-pohon-1213.json`.
2. Kalimat "Buku ini **edisi 1.2.13** dan terbit 3 November 2026" — tidak tepat, karena buku
   dibangun 3 November 22:06 tetapi baru menjadi aset rilis 4 November 06:23 — diganti menjadi
   "(naskah dibangun 3 November 2026 22:06 WIB, diganti sebagai aset rilis 4 November 2026
   06:23 WIB)".

Pemeriksaan sesudah terbit, dibaca ulang dari GitHub: nol kemunculan `30 jam`, `dan terbit
3 November 2026`, `9.316.970`, dan `(99 halaman`; `18f47fc`, `cbeae7d`, `b5dd62b`, dan
`gerbang-release-pohon-1213.json` masing-masing satu kali. Salinan yang terbit disimpan di
[`docs/paket-bukti-2026-11-04/badan-rilis-v1.2.13-terbit.md`](paket-bukti-2026-11-04/badan-rilis-v1.2.13-terbit.md);
salinan edisi 07:16 (`82a557d61f7568dd…`, 7.415 bita) tetap di
[`docs/paket-bukti-2026-11-03/badan-rilis-v1.2.13-terbit.md`](paket-bukti-2026-11-03/badan-rilis-v1.2.13-terbit.md)
sebagai riwayat, bukan sebagai keadaan rilis hari ini.

Catatan byte: yang disajikan GitHub berakhir dengan CRLF, sedangkan `core.autocrlf=true` pada mesin
ini menormalkan CRLF→LF saat komit — salinan edisi 07:16 karenanya tersimpan sebagai 7.415 bita LF
(`82a557d6…`), 65 bita lebih pendek daripada bentuk CRLF yang benar-benar dijawab GitHub. Sejak
komit ini ketiga berkas bukti (`badan-rilis-v1.2.13-terbit.md` kedua dan
`gerbang-release-pohon-1213.json`) ditandai `-text` di `.gitattributes`, sehingga isi blob sama
persis dengan berkas di pohon kerja dan digest yang dicatat dokumen bisa diperiksa ulang tanpa
menebak konversi line ending. Cara periksa:
`git cat-file blob :docs/paket-bukti-2026-11-04/badan-rilis-v1.2.13-terbit.md | sha256sum` →
`e011ebe7…`, sama dengan `sha256sum` berkasnya (7.949 bita CRLF; 7.884 bita tanpa `newline`
tambahan dari pencetak `gh`).

## Batas bukti

Peralatan uji berada pada PC dengan RAM 3,7 GB; hasil "lulus" di atas adalah hasil satu mesin.
Angka 105/105 yang tercatat untuk 1.2.13 berasal dari run sandbox 1.2.12 → 1.2.13 hari ini;
angka 105/105 milik 1.2.12 (jalur 1.2.9 → 1.2.12) adalah bukti untuk paket itu sendiri.
