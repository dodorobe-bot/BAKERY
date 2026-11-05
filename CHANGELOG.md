# Changelog

Semua perubahan penting aplikasi Seruni 22 Bakery. Versi terbaru di atas.
Bukti mentah tiap rilis ada di `docs/verifikasi-paket-<versi>-<tanggal>.md` dan
`docs/paket-bukti-<tanggal>/`.

## [1.2.14] — 2026-11-05

Status: **NOT READY**. Paket installer sudah ada dan sudah terbukti di mesin bersih —
`Seruni22Bakery_Setup_v1.2.14.exe` (42.680.006 bita, SHA-256 `f48442d5…`) dan
`Seruni22Bakery_v1.2.14_x64.msi` (50.424.324 bita, SHA-256 `a8d998a6…`, ProductCode
`{75FF905B-6F22-4051-A437-5F9362F0EA2B}`), keduanya `NotSigned`. Yang menahan status bukan
kualitas build, melainkan empat hal di luar mesin pengembang ini: printer kasir fisik,
pemindai barcode fisik, laci uang, dan uji di Windows 10 — ditambah tanda tangan
Authenticode yang belum ada sertifikatnya. Angka bukti di bawah diambil dari paket yang
sama (commit `94073a3`): gerbang `scripts/verify.ps1 -Configuration Release` **33 langkah
lulus, 0 gagal, 0 SKIP**; `dotnet test BakeryDesktop.slnx -c Release` **544 tes lulus, 0
gagal, 0 dilewati** (Domain 83, Application 129, Infrastructure 164, UI 168) — 21 tes lebih
banyak dari gerbang 1.2.13; `scripts/build-pro.ps1` **12 langkah, 0 gagal, 0 dilewati, 1
peringatan**; pasang/peningkatan/copot **1.2.13 → 1.2.14 di Windows Sandbox bersih lulus
106/106, 0 gagal**. Rinciannya: `docs/verifikasi-paket-1.2.14-2026-11-05.md`.

- **Jurnal stok layar Bahan Baku.** Stok yang diketik pada kolom **Stok Awal** di layar Bahan Baku
  sebelumnya mengubah angka stok tanpa pernah membuat jurnal, sehingga saldo `1-003 Persediaan Bahan
  Baku` tertinggal dari kenyataan. Kini POST `/api/ingredients` membukukannya sebagai stok awal
  (Debet persediaan, Kredit `3-001 Modal`) dan PUT `/api/ingredients/{id}` membukukan selisihnya
  terhadap total stok riil ke `5-006 Beban Selisih Persediaan`; keduanya satu transaksi dengan mutasi
  ledger, dan penolakan validasi menjawab 400. Pemeriksaan mandiri **Periksa Basis Data** mendapat
  pagar baru `stok layar Bahan Baku punya jurnal` yang menandai data warisan yang belum terjamah.
  Data lama **tidak** ditambal otomatis lewat migrasi — angkanya masuk ke neraca pemilik.
  4 tes repository + 1 tes integritas + 4 tes API hidup.
- **Perintah `--rekonsiliasi-stok-bahan`.** Penambalan data warisan yang dimaksud di atas sekarang
  ada jalurnya, dijalankan sebagai perintah pemeliharaan sebelum host web menyala: tanpa `--terapkan`
  hanya mencetak daftar bahan, jumlah, dan nilainya (tidak satu baris jurnal pun ditulis, tidak ada
  cadangan dibuat); dengan `--terapkan` menolak bila aplikasi masih hidup (kode keluar `3`), membuat
  cadangan `pra-rekonsiliasi`, lalu menulis Dr `1-003 Persediaan Bahan Baku` / Cr `3-001 Modal` dalam
  satu transaksi. Idempoten: bahan yang sudah berjurnal — dari jalur baru maupun dari penambalan
  sebelumnya — dilewati. Alternatif tanpa baris perintah tetap tercatat (opname atau jurnal manual).
  6 tes penambalan di Infrastructure + 3 tes penjaga saklar di UI.Tests; kontrol negatif terbukti
  merah (2/3) ketika cabang saklar di `MaintenanceCommands.TryRun` dilepas.
- **Dialog Penyelesaian SPK.** Kolom yang berjudul **Sisa Stok Dapur** sebenarnya menampilkan takaran
  resep, bukan stok dapur, sehingga bisa dibaca sebagai "stok aman" padahal bahan tidak ada di dapur.
  Kolom itu dipecah menjadi **Stok Dapur** (stok riil di Dapur Produksi, sama dengan layar Gudang &
  Lokasi Rak) dan **Sisa Setelah Dipakai** (stok dapur dikurangi **Dipakai Aktual**; merah dan boleh
  negatif sebagai tanda penolakan). Bahan yang belum pernah dikirim ke dapur dihitung dari nol, bukan
  tanda tanya; tanda tanya hanya tampil bila muat stok gagal, dengan keterangan yang eksplisit. Semua
  angka kuantitas memakai pemisah desimal Indonesia sampai tiga digit, sehingga takaran ragi 0,002 kg
  tidak dibulatkan menjadi "0" dan 18,244 kg tidak lagi terbaca delapan belas ribu. 3 tes UI Playwright,
  kontrol negatif 3/3 merah pada kode sebelum perbaikan.
- **Keter-reproduksian berkas bukti.** `git clone` di mesin dengan `core.autocrlf=true`
  selama ini **menulis ulang** isi folder `docs/paket-bukti-*`: 178 dari 247 berkas bukti berubah
  byte, termasuk skrip kit yang SHA-256-nya dicatat di `KIT-SHA256SUMS.txt` (`run-sandbox.ps1` kit
  1.2.12 tercatat `9fe9e633…`/6930 bita, hasil clone 7120 bita) dan manifestnya sendiri, sehingga
  `sha256sum -c` menolak nama berkas yang dibaca. Artinya bukti yang dipegang orang lain bukan
  berkas yang dibuktikan. Aturan `docs/paket-bukti-*/** -text` di `.gitattributes` mematikannya,
  dan `scripts/uji-checkout-bukti.ps1` — clone sekali-pakai di folder temporer dengan konfigurasi
  yang merusak itu — memeriksa hasil checkout byte-per-byte terhadap blob lewat
  `hash-object --no-filters` (sengaja bukan `git status`: status membandingkan setelah filter
  akhir baris, jadi buta terhadap cacat ini), memastikan `check-attr` benar-benar
  `text: unset`, lalu mencocokkan tiap digest kit. Kontrol negatif pada HEAD tanpa aturan: **69
  lulus, 195 gagal, 16 lewati** (keluar 1). Pada HEAD yang sudah memuat aturan (commit
  `2b8fa45`): **251 berkas** dibandingkan, **268 lulus, 0 gagal, 16 lewati** (keluar 0); pohon
  tanpa `.git` dilaporkan **BELUM DIPERIKSA** (keluar 2), bukan lulus. Artefak
  `.exe`/`.msi` tidak dikomit sehingga 16 entri manifest memang tetap dilewati, bukan dihitung lulus.
  Pagar ini jadi langkah baru `scripts/verify.ps1`; tiga log hasilnya ada di
  `docs/paket-bukti-2026-11-04/checkout-reproduksian/`.
- **Buku manual.** Enam belas blok **Contoh** beserta tangkapan layar berisi ditambahkan per menu, peta
  urutan pengisian pertama (§0.6), dan rujukan silang yang sudah usang direnomor (gambar 1..57).
  Tabel **Yang berubah pada versi 1.2.14** ditambahkan dan daftar edisi 1.2.13 dipertahankan di
  bawahnya. Baris **Peningkatan dari versi lama** (Bab 39) diperbarui setelah uji mesin bersih
  1.2.13 → 1.2.14 lulus, dan peringatan batas pengujian perangkat (Bab 32) kini menyebut bahwa
  byte ESC/POS juga sudah diperiksa dari biner hasil peningkatan, bukan hanya dari mesin
  pengembang. PDF dibangun ulang: **128 halaman, 58 gambar, 45 entri daftar isi cocok
  sebaris**, kepala/kaki terverifikasi di halaman 2..128, dan versi tercetak diambil dari
  `<Version>` csproj lalu dicocokkan dengan judul naskah (keduanya 1.2.14).
- **Peningkatan dibuktikan di mesin bersih.** Kit `docs/paket-bukti-2026-11-05/upgrade-1213-1214-sandbox/`
  memasang MSI 1.2.13 di Windows Sandbox tanpa jaringan, mengisi sandi/produk/merek lewat API-nya
  sendiri, meningkatkan ke 1.2.14 secara senyap, lalu memeriksa 106 hal dalam satu run: **106 lulus,
  0 gagal**. `WIX_UPGRADE_DETECTED` memuat ProductCode 1.2.13 `{CE68DB95-…}`, `RemoveExistingProducts`
  berjalan, tepat satu entri **Seruni 22 Bakery 1.2.14** tertinggal di Daftar Program, 393/393 berkas
  payload cocok dengan manifes, `Seruni22Bakery.Host.dll` hasil pasang sama byte dengan manifes
  publish (`f710ccc9…` — bukti perbaikan jurnal bahan benar-benar ikut terkemas), bundel
  `index-BZfyiTsC.js` tergantikan `index-BmCxycbD.js`, skema tetap **18** dengan **0 migrasi baru**
  (1.2.13 memang sudah memakai 18), sandi/produk/merek/logo pemilik bertahan, ESC/POS raw dari biner
  hasil peningkatan diterima emulator 635 bita dengan `ESC @` di depan, `ESC d 3` di belakang, tanpa
  `GS V` saat potong mati, dan pengaturan perangkat bertahan setelah host di-restart. Pagar turun
  versi (MSI 1.2.13 di atas 1.2.14 → exit 1603), kanal EXE (EXE 1.2.13 di atas EXE 1.2.14 ditolak;
  MSI 1.2.14 ditolak selama EXE ada) dan copot EXE yang tidak menghapus folder data pengguna juga
  lulus. Nama berkas CSS sengaja tidak berubah antara 1.2.13 dan 1.2.14 karena isi CSS memang identik,
  jadi bukti "aset lama tersapu" di jalur ini bertumpu pada bundel JS dan jumlah berkas aset, bukan
  pada lenyapnya nama CSS. Semua konstanta kit dibaca ulang dari disk (hash+ukuran paket, tabel
  `Property`/`File` MSI, manifes publish), bukan diketik dari ingatan.
- **CI sebagai pagar kedua — merah lalu hijau, dan yang merah bukan produk.**
  [`.github/workflows/gerbang.yml`](.github/workflows/gerbang.yml) (`Gerbang build + tes`, `windows-latest`)
  menjalankan `npm ci` + bundel desktop, `dotnet build -c Release`, `dotnet test -c Release` dan
  `scripts/uji-checkout-bukti.ps1` pada setiap push ke `main` / pull request. **Ia tidak membangun installer**,
  jadi kedudukannya pagar kedua, bukan pengganti gerbang lokal. Run pertama
  [`41944547594`](https://github.com/dodorobe-bot/BAKERY/actions/runs/41944547594) di commit `96e4389`
  **gagal**: Domain 83, Application 129, Infrastructure 164 lulus penuh, tetapi UI 163 lulus / **5 gagal** —
  kelimanya `McpStdioTests` (`'P' is an invalid start of a value` empat kali, `IOException: The pipe is
  being closed` sekali). Akarnya harness: `tests/UI.Tests/Helpers/McpClient.cs` mengunci jalur
  `bin\Debug\net8.0\Seruni22Bakery.Host.dll` sementara CI hanya membangun Release, sehingga `dotnet`
  mencetak pesannya sendiri ke stdout dan baris itu dibaca sebagai JSON. Perbaikan `1861fb0` menyatukan
  aturan pencarian host di `TestHost.ResolveHostDll` (konfigurasi majelis tes lebih dulu) dan menghapus
  empat salinan privat di harness. Run kedua
  [`41946780573`](https://github.com/dodorobe-bot/BAKERY/actions/runs/41946780573) di `1861fb0`
  **`success`** dalam 6 m 8 s: `Build succeeded. 0 Warning(s) 0 Error(s)`, **544 tes lulus / 0 gagal /
  0 dilewati** (83+129+164+168 — identik dengan gerbang lokal), dan langkah yang belum pernah berjalan di
  CI, `Keter-reproduksian berkas bukti`, lulus dengan **302 dibandingkan byte-per-byte, 0 gagal,
  20 lewati** (entri `.exe`/`.msi` yang memang tidak dikomit). Langkah `Unggah log bila ada kegagalan`
  berstatus `skipped` karena berkondisi `if: failure()` — itu rancangan, bukan kelulusan.
  Rincian: [`docs/paket-bukti-2026-11-05/ci/gerbang-ci-1214.md`](docs/paket-bukti-2026-11-05/ci/gerbang-ci-1214.md).

## [1.2.13] — 2026-11-03

Status: **NOT READY**. Yang sudah dibuktikan: gerbang verifikasi dijalankan pada konfigurasi
**Release** dan lulus penuh — **32 langkah, 0 gagal, 0 SKIP**, **523 tes otomatis lulus, 0 gagal,
0 dilewati**, **169 pemeriksaan API** (batch2 63, batch3 56, batch6 21, bug09 29) tanpa kegagalan, dan
`build-pro.ps1` menghasilkan EXE + MSI dengan **393/393 berkas payload** cocok manifest serta
validasi paket **19 pemeriksaan, 0 gagal, 0 lewati**.

Acuan bukti gerbang adalah run pada pohon yang **sudah** bertuliskan versi 1.2.13:
[`docs/paket-bukti-2026-11-04/gerbang-release-pohon-1213.json`](docs/paket-bukti-2026-11-04/gerbang-release-pohon-1213.json)
— commit `b5dd62b`, `<Version>1.2.13</Version>`, pohon kotor 0, hasil LULUS, keluar 0, 335,1 s. Run
2026-11-03 (`dist\verify\verify-20261103-172525.json`, 239,9 s) menghasilkan angka yang identik,
tetapi berjalan pada commit `18f47fc` yang `csproj`-nya masih **1.2.12** — kenaikan versi ke 1.2.13
terjadi sesudah gerbang itu, jadi run tersebut bukti pra-naik-versi, bukan bukti paket 1.2.13.

Isi versi ini adalah perbaikan yang dibangun 2–3 November dan **tidak ada di paket 1.2.12**:

- **Keamanan jaringan.** Host menolak alamat dengar di luar loopback — baik dari `--urls` maupun
  `Kestrel:Endpoints` — sebelum kunci JWT dan basis data dibuat, sehingga salah ketik konfigurasi
  tidak membuka port ke jaringan toko dan tidak meninggalkan berkas. LAN hanya atas izin eksplisit
  `SERUNI_ALLOW_LAN=1`. 20 tes unit + 1 tes proses tingkat biner.
- **Konsistensi data.** Nilai yang menempel pada stok produk jadi kini sama dengan nilai yang
  dipakai HPP (sebelumnya 1-006 bisa bersaldo negatif begitu barang terjual); SPK tanpa unit baik
  membebani gaji/operasional alih-alih menguap ke aset; sisa pembulatan harga rata-rata dibukukan
  ke beban selisih persediaan.
- **Backup.** Retensi mengutamakan salinan yang sudah terverifikasi, jadi cadangan rusak yang
  kebetulan terbaru tidak lagi menghabiskan jatah periode sementara salinan sehat terhapus.
- **Idempotensi.** Kunci yang tertinggal oleh proses mati di tengah jalan kedaluwarsa setelah
  30 menit (dihitung di SQL dengan UTC yang sama dengan `ReservedAt`), dan baris selesai dibersihkan
  setelah 30 hari — pengguna tidak lagi terkena 409 terus-menerus.
- **Antar muka.** Status perangkat dipakai ulang 15 detik dan dibersihkan saat pengaturan diubah;
  daftar piutang/utang keuangan dimuat bertahap dengan penanda "hanya baris yang termuat" alih-alih
  berhenti diam-diam pada 100 baris.
- **Otomatisasi.** Laporan gerbang JSON mencatat commit, status pohon kotor, opsi, hasil tiap
  langkah, dan jumlah tes dari log; bukti kegagalan disimpan sebelum folder temp dibersihkan;
  tiga pagar statis menjaga agar guard loopback tidak terhapus diam-diam dan agar tidak ada skrip
  membuka port firewall; `sign-paket` memilih versi dari laporan build (dulu mengikuti abjad, bisa
  menandatangani v1.2.10 saat yang dimaksud v1.2.12); `dotnet test` yang lulus tanpa satu pun tes
  tercatat kini menggagalkan build; jalur `OutRoot` bertanda junction/symlink ditolak; kontrol
  negatif "penjaga versi terbit" tidak lagi SKIP pada versi yang belum bertag — ia menanam artefak
  palsu di `OutRoot` sementara, jadi penjaga itu terbukti tepat saat rilis (14/14, 0 SKIP).

Yang sudah dibuktikan setelah itu: **pasang/peningkatan/copot di Windows Sandbox bersih** —
jalur 1.2.12 → 1.2.13 dengan **105 pemeriksaan lulus 0 gagal**
(`docs\paket-bukti-2026-11-03\upgrade-1213-sandbox\`), jalur 1.2.10 → 1.2.13 dengan
**106 pemeriksaan lulus 0 gagal**
(`docs\paket-bukti-2026-11-04\upgrade-1210-1213-sandbox\`), dan jalur tertua yang masih
beredar di lapangan, 1.2.9 → 1.2.13, juga **106 pemeriksaan lulus 0 gagal**
(`docs\paket-bukti-2026-11-04\upgrade-129-1213-sandbox\`; satu run, tanpa run gagal
sebelumnya). Di ketiga jalur: sandi, produk, merek dan logo tetap utuh, 393/393 berkas
payload cocok manifes, bundel lama tersapu, tanpa migrasi baru di atas skema 18,
ESC/POS raw terbukti byte-per-byte dari biner hasil peningkatan, pagar turun versi dan pagar
kanal bekerja, dan copot tidak menghapus folder data pengguna. Tangkapan ESC/POS ketiga run
sama kecuali digit cap waktu (dihitung ulang dengan `cmp -l` atas berkas `.bin` yang
tersimpan): 1.2.9 vs 1.2.10 berbeda 2 byte, dan masing-masing vs run 1.2.12 → 1.2.13
berbeda 3 dan 4 byte — semuanya angka tanggal/jam pada baris waktu struk. Run 1.2.9 dan
1.2.10 memakai harness yang diperkuat: keberadaan bundel lama
diperiksa sebelum peningkatan (jadi klaim "aset lama tersapu" punya dasar) dan penolakan EXE
lama memakai pagar versi nyata di `setup.iss` tag masing-masing — `v1.2.9` sudah memuat
`BandingkanVersi` — bukan lagi penanda `HKCU Version=9.9.9` yang direkayasa.

Yang **belum** untuk 1.2.13: cetak ke printer thermal 80 mm maupun printer jaringan fisik,
pemindai barcode fisik, laci uang, pemotong kertas, pemasangan oleh pemilik di PC kasir,
dan uji di Windows 10. Paket **NotSigned**. Skema basis data: **18** — tidak ada migrasi baru.
Tag: `v1.2.13` — menandai sumber paket, bukan persetujuan rilis. Paket dibangun dari commit
bersih `5993072`; pohon aplikasi pada tag ini identik dengan commit itu, yang menyusul hanya
dokumen bukti dan metadata. Hash EXE/MSI ada di tabel artefak
`docs/verifikasi-paket-1.2.13-2026-11-03.md`.

## [1.2.12] — 2026-11-01

Status: **NOT READY**. Yang sudah dibuktikan: build Release, 489 tes otomatis lulus, gerbang
verifikasi 28 langkah (169 pemeriksaan API, 0 gagal), isi paket EXE/MSI cocok dengan hasil
publish, dua siklus hidup "server mati → hidup lagi" di peramban sungguhan, dan
pasang/peningkatan/copot di Windows Sandbox bersih — 1.2.9 → 1.2.12, **105 pemeriksaan lulus
0 gagal**, termasuk jalur ESC/POS dari biner hasil peningkatan dan pembuktian data pengguna
tetap utuh setelah copot (`docs\paket-bukti-2026-11-01\upgrade-1212-sandbox\`). Yang **belum**:
pencetakan ke printer fisik (thermal 80 mm maupun printer jaringan), pemindai barcode fisik, dan
laci uang — jalur ESC/POS baru terbukti sampai emulator loopback; pemasangan oleh pemilik di PC
kasir dan uji di Windows 10 juga belum. Uji perangkat 2026-11-01 hanya bisa membuktikan alur
pemindai lewat simulasi (14/14 lulus), sementara percobaan cetak driver ke pencetak virtual gagal
karena port `PORTPROMPT:` menuntut dialog simpan; keduanya dicatat di
`docs\paket-bukti-2026-11-01\uji-fisik-1212\`. Paket **NotSigned** — 0 sertifikat Authenticode
terpasang dan `signtool verify /pa` menolak keduanya. Skema basis data: **18** — tidak ada migrasi baru.

Versi **1.2.11 tidak pernah diterbitkan**. Perbaikan struk yang semula direncanakan untuk 1.2.11
digabung ke paket ini bersama jalur ESC/POS raw dan perbaikan ketahanan sambungan.

Artefak:

| Berkas | Ukuran | SHA-256 |
|---|---|---|
| `Seruni22Bakery_Setup_v1.2.12.exe` | 42.675.344 | `f43f4e8583cc1df84354039925f45046aafe93a6f3996b112c80501229a506c1` |
| `Seruni22Bakery_v1.2.12_x64.msi` | 50.399.748 | `5ee109f9ece7cd24b4125adb5f999aedd8cd90d701a77bb9b2d8e9a8bd8fc859` |

Berkas di dalam paket: host `Seruni22Bakery.Host.dll`
`ccfdc91b193a87b9acb9ab9418cbbb6cf4304f3de604101b4c331f8305f9ad7d`, peluncur
`Seruni22Bakery.exe` `44290eb7a43cc662e607f097b7bb670cf8fdbf4d1fdf82baf58308c814ea5e27`,
bundel web `wwwroot/index.html` `954c5d65e5e15270d40d4c9b9c435da260172be631bc820f4f84012dc96b4617`,
aset `index-dCrlHoiC.js` (596.507 bita) `602b04bf13dbcc1bd04ce0a56801824a78c8139e366f0f89870d61034cf77135`
dan `index-RvdFb41s.css` (58.177 bita) `f3f9c629d71f73ca11b8fff08d8daafe5ffe5b9ad8ef2efae5d911b08ea06180`.
MSI 1.2.12 memakai ProductCode `{1D0B5C27-C2B9-49AD-A675-BA390B33E3E3}`; UpgradeCode
`B975F792-6F80-4D56-8A64-D274A044DCF1` dipertahankan sehingga peningkatan dari 1.2.9/1.2.10
dikenali. Kedua paket bertanda tangan **NotSigned**. `dist\installer\SHA256SUMS.txt` memuat
EXE dan MSI sekaligus dan lolos `sha256sum -c`; `build-msi.ps1` kini menambahkan baris MSI
sendiri, karena sebelumnya daftar itu hanya berisi EXE.

### Ditambahkan

- **Jalur cetak ESC/POS raw.** Pengaturan Perangkat Kasir kini punya dua jalur: driver Windows
  (GDI/spooler) dan ESC/POS langsung ke printer jaringan lewat TCP 9100. Urutan byte yang
  dikirim: `ESC @` (inisialisasi), teks ASCII + LF, `ESC d 3` (umpan kertas), dan `GS V 0` hanya
  bila opsi potong menyala. Perintah `ESC a/E/t/p` tidak pernah dikirim. Riwayat pekerjaan cetak
  mencatat tujuan `tcp://ALAMAT:PORT`, dan kegagalan sambung dilaporkan dengan pesan bahasa toko.
- **Bukti byte-level tanpa printer.** Emulator ESC/POS loopback menerima 1.572 bita untuk dua
  struk uji (636 + 936) dan isinya diperiksa byte demi byte (`docs/pos-hardware.md` §5b.2).
  Ini membuktikan perangkat lunak, **bukan** printer fisik.
- **Alat uji perangkat fisik diperluas.** `scripts/pos-physical-check.ps1` menerima
  `-CetakMode windows-driver|escpos-raw`, `-EscPosHost`, `-EscPosPort`, `-EscPosPotong`;
  langkah A1–A7 di `docs/pos-uji-fisik.md` memuat perintah untuk kedua jalur.
- **Ketahanan sambungan (banner "Server lokal tidak menjawab").** Tiga lapis:
  1. Kestrel memakai `KeepAliveTimeout` 10 menit, jadi peramban tidak lagi memakai soket yang
     sudah ditutup server setelah lama didiamkan.
  2. Klien membedakan "server mati" dari "sambungan putus sekejap": kegagalan jaringan diprobes
     ke `/health` (tanpa token). Aksi ber-kunci idempotensi boleh diulang karena gerbang server
     (`src/Host.Service/Idempotensi.cs`) mengerjakan satu niat satu kali.
  3. Layar tidak lagi menyuruh menekan "Coba lagi" saat server mati: ia menanyakan `/health`
     setiap 4 detik dan memuat ulang sendiri begitu server hidup, dengan pesan
     "Menunggu server hidup lagi…".

### Diperbaiki

- **Nominal contoh pada struk uji.** Baris contoh mencetak `2 x Rp 15.000 → Rp 300`; nilainya
  sekarang `Rp 30.000` (`Money.Format(3_000_000)` di `src/Application/Devices/ReceiptRenderer.cs`).
- **Batas waktu cetak.** Permintaan cetak memakai batas 60 detik, bukan 15 detik; pengukuran
  nyata menunjukkan cetak ke antrean Windows bisa memakan ~12 detik dan dulu berakhir dengan
  pesan gagal padahal struk tercetak.
- **Pesan kegagalan jaringan tidak lagi menyesatkan.** Sebelumnya semua kegagalan jaringan
  berbunyi "Server lokal tidak menjawab". Sekarang: server mati → pesan menunggu; GET yang
  terputus sekejap → "Tekan Coba lagi"; aksi simpan yang terputus → peringatan agar tidak
  menekan Simpan dua kali.

### Pagar dan tes baru

- `RenderTest_ContohTransaksiKonsisten` (kontrol negatif: MERAH pada kode lama) dan seluruh
  `ReceiptRenderer` 17/17.
- Tes encoder ESC/POS dan emulator loopback (urat byte dan ketiadaan perintah yang dilarang).
- `scripts/verify.ps1` §2d: empat pagar baru — `KeepAliveTimeout` di host, probe `/health` di
  klien, penantian server di layar, dan **bundel `desktop/` yang benar-benar disajikan** memuat
  teks penantian itu (bundel basi pernah lolos di rilis sebelumnya).

Rincian bukti: `docs/verifikasi-paket-1.2.12-2026-11-01.md`,
`docs/paket-bukti-2026-11-01/uji-cetak-1210/README.md`, dan `docs/pos-hardware.md` §5b.

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
bundel web `wwwroot/index.html` `d9f8f20f0609a070f147f17fce9f15a562c33a3a0e352fc983d78ad7a73e2d1c`,
aset `index-B8lfbLcy.js` (589.329 bita) `834b32cd054c2d36b1ad598a8b435c5bab873f5f255811d4a9fc39abbae7fc54`
dan `index-CoYZy02P.css` (57.262 bita) `9a26e0402c5cbe4c245a6a259b647e4c6a44ef53eac118923ce4664a36da23df`.
MSI 1.2.10 memakai ProductCode `{F74073DE-74DE-47CB-B9A7-358F0DE5E8FF}`; UpgradeCode
`B975F792-6F80-4D56-8A64-D274A044DCF1` dipertahankan sehingga peningkatan dari 1.2.9 dikenali.

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
| Verifikasi API hidup (host `bin\Release`, dipakai gerbang) | batch2 63, batch3 56, batch6 21, bug09 29 — masing-masing 0 gagal |
| Verifikasi API hidup pada **biner yang dikemas** (`dist\publish-single\Seruni22Bakery.Host.exe`, `--Desktop` menunjuk `wwwroot` paket) | empat skrip di atas diulang pada empat basis data sementara: 63 + 56 + 21 + 29 = **169 pemeriksaan, 0 gagal**; `/health` menjawab `1.2.10`; setiap basis data berhenti di skema **18** dengan `integrity_check` ok |
| Isi paket | 393 berkas; host `1.2.10.0`, peluncur `1.2.10`; bebas `.pdb`, `appsettings.Development.json`, `*.db/-wal/-shm`, `jwt.key`, `*.env`; hash cocok `BUILD-INFO.txt` dan `SHA256SUMS.txt` |
| Isi MSI | 393 entri `File`; hasil decompilasi paket menampilkan `MajorUpgrade` dengan pesan turun versi, `LaunchCondition NOT (EXECHANNEL = "exe") AND NOT EXEINSTALLED`, dan `ChannelMarker` HKLM `Version=1.2.10` |

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
