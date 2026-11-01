# Perangkat Kasir Windows — Peta Perangkat, Driver, dan Panduan Offline

Dokumen ini mencatat **apa yang benar-benar diuji** pada perangkat keras kasir, apa yang baru
disiapkan antarmukanya, dan apa yang belum pernah diuji. Tidak ada pernyataan "kompatibel
universal" di sini: yang tertulis hanya kombinasi perangkat + driver + koneksi + versi Windows
yang benar-benar diperiksa.

Tanggal pemeriksaan: 2026-10-28. Lingkungan uji: Windows 11 Pro 10.0.26300, 64-bit, .NET 8.
Tidak ada printer thermal, pemindai, laci uang, layar pelanggan, atau timbangan fisik yang
terhubung saat pengujian ini. Semua hasil perangkat keras di bawah karena itu adalah
**hasil simulasi/sisi perangkat lunak**, kecuali disebut lain.

> **Diperbarui 2026-11-01**: jalur **ESC/POS langsung** (`escpos-raw`) sudah dibangun dan
> terbukti terhadap emulator ESC/POS loopback buatan sendiri di mesin ini. Baris perangkat fisik
> tetap seperti semula: **belum ada printer kasir fisik yang diuji**, di jalur driver maupun
> ESC/POS. Tanggal 2026-10-28 tetap berlaku untuk angka yang tertulis di bagian 5.

---

## 1. Arsitektur aktual (hasil audit, bukan usulan)

Aplikasi ini **tidak memakai Tauri**. Yang berjalan adalah:

```
React (Vite)  ──HTTP loopback, bearer JWT──▶  ASP.NET Core host (.NET 8)
                                                    │
                                    PosDeviceService │ adaptor perangkat
                                                    ▼
        windows-driver ▶ Windows print spooler / driver (lewat driver terpasang)
        escpos-raw     ▶ TCP ke port 9100 printer, byte ESC/POS, tanpa driver
                                                    ▼
                                                SQLite (WAL)
```

- Antarmuka React dilayani host lokal dari folder `desktop/`.
- Peramban tidak pernah memegang USB, driver, atau berkas basis data. Satu-satunya jalan ke
  perangkat adalah endpoint `/api/devices/*` dan `/api/orders/{id}/receipt` yang memeriksa
  peran, memvalidasi masukan, lalu memanggil adaptor di server.
- Prinsip "secure command boundary" dari usulan Tauri tetap berlaku, hanya medianya HTTP
  loopback ber-JWT dengan daftar perintah tertutup (bukan IPC Tauri). Tidak ada perintah shell
  bebas, tidak ada akses basis data dari layar.

Endpoint perangkat:

| Endpoint | Peran | Guna |
|---|---|---|
| `GET /api/devices` | administration (Owner) | daftar printer nyata, pengaturan, catatan |
| `GET /api/devices/status` | sales | ringkasan untuk layar kasir (tanpa daftar printer toko) |
| `PUT /api/devices/settings` | administration | simpan mode cetak, printer/alamat, lebar, auto-cetak, potongan, akhiran pindai |
| `POST /api/devices/test-print` | administration | struk uji |
| `GET /api/devices/print-jobs` | administration | riwayat pekerjaan cetak |
| `POST /api/orders/{id}/receipt` | sales | cetak/cetak ulang struk pesanan tersimpan |

## 2. Peta perangkat: status sebenarnya

Keterangan: **Teruji** = ada bukti otomatis pada build ini; **Terpasang** = jalur kode ada dan
dipakai endpoint, tetapi belum diuji pada perangkat fisik; **Antarmuka saja** = hanya kontrak
kode, tidak ada implementasi produksi dan tidak ada endpoint.

| Perangkat | Jalur | Status | Bukti / catatan |
|---|---|---|---|
| Komputer kasir Windows 64-bit | host .NET 8 + SQLite lokal | Teruji (mesin uji) | build + 226 tes + pagar `verify.ps1` |
| Printer terpasang di Windows | enumerasi registry printer + printer bawaan | Teruji (mesin uji) | halaman Perangkat Kasir membaca daftar dari Windows (mesin uji: 2 printer virtual); tes UI menuntut jumlah opsi = jumlah printer yang dilaporkan server |
| Cetak struk lewat driver Windows | `PrintDocument` + spooler, tanpa dialog | Terpasang | isi struk & antrean diuji; **belum ada keluaran kertas fisik** |
| Pemotong kertas (cutter) | driver: mengikuti printer; ESC/POS: `GS V 0` saat pilihan dihidupkan | Belum diuji perangkat | byte potong terbukti tiba di socket emulator; pada jalur driver tidak ada perintah potong terpisah, dan di ESC/POS belum diketahui apakah printer fisik benar-benar memotong |
| Pemindai barcode USB (keyboard wedge) | kolom aktif + Enter | Teruji (simulasi ketik + Enter di peramban) | tes UI mengisi produk & total dari katalog, kode tak dikenal ditolak; **perangkat fisik belum diuji** |
| Lebar kertas 58 mm / 80 mm | 32 / 48 kolom | Teruji | tes renderer: tidak ada baris melebihi lebar, deskripsi dibungkus |
| Laci uang lewat printer | `ICashDrawerAdapter` | Antarmuka saja | belum ada implementasi & endpoint; sengaja tidak diklaim |
| Layar pelanggan (OPOS/SDK) | `ICustomerDisplayAdapter` | Antarmuka saja | idem |
| Timbangan (serial/COM/SDK) | `IScaleAdapter` | Antarmuka saja | idem |
| ESC/POS langsung (raw) | mode `escpos-raw`, TCP ke port printer | Terpasang (teruji terhadap emulator) | encoder byte, penjaga alamat, dan transport diuji dengan **emulator ESC/POS loopback milik sendiri** (tanpa unduhan pihak ketiga, tanpa hak admin): `dotnet test --filter FullyQualifiedName~EscPos` pada Application (77) dan UI (18), termasuk byte per byte yang sampai ke socket; **belum diuji pada printer kasir fisik** |
| Cetak struk lewat ESC/POS ke kertas | byte ESC/POS ke port 9100 | Belum diuji | keluaran kertas nyata pada printer thermal jaringan belum pernah diperiksa |
| EDC/kartu | — | Di luar cakupan | integrasi terpisah |

## 3. Driver dan protokol yang dipakai

- **Printer**: driver resmi Windows (spooler), dipilih dari daftar printer yang benar-benar
  terpasang. Aplikasi **tidak** memasang/mengganti driver sendiri. Kertas diatur dari ukuran
  halaman kustom (lebar 58/80 mm, tinggi mengikuti isi struk).
- **Pemindai**: mode HID keyboard wedge. Aplikasi hanya membaca karakter yang masuk ke kolom
  dan mengakhiri dengan Enter. Pemindai yang tidak mengirim Enter harus diprogram di
  perangkatnya; pemindai USB-serial/Bluetooth belum didukung.
- **ESC/POS langsung**: host dan port dibaca dari `AppSettings`, struk disusun sebagai teks
  monospace lalu disandikan menjadi byte ESC/POS (`ESC @` untuk setelan awal, baris ASCII diakhiri
  `LF`, `ESC d 3` untuk umpan kertas, dan `GS V 0` bila pemotongan dihidupkan). Tidak ada driver,
  tidak ada berkas spool, tidak ada perintah shell. Perintah yang sengaja **tidak** pernah
  dikeluarkan dan dijaga oleh tes: `ESC a` (perataan — renderer sudah memusatkan teks, menyalakan
  perataan menghasilkan teks ganda di tengah), `ESC E` (huruf tebal melebarkan lebar huruf pada kertas
  58 mm), `ESC t` (kode halaman bergantung vendor), dan `ESC p` (laci uang harus tetap aksi
  terpisah, bukan efek samping mencetak).
- **Tidak dipakai**: OPOS/JavaPOS, SDK vendor, port serial, perintah shell.

## 4. Aturan yang dijaga server

1. Pesanan tersimpan di SQLite **sebelum** pekerjaan cetak dibuat. Jalur cetak tidak pernah
   mengubah pesanan, pembayaran, atau stok.
2. Kegagalan printer (mati, kertas habis, nama printer hilang, waktu tunggu 20 detik) hanya
   menandai pekerjaan `Failed` dan mengembalikan pesan; pesanan tetap ada dan bisa dicetak ulang.
3. Pekerjaan `Pending` yang menggantung saat aplikasi berhenti ditandai `Failed` pada start
   berikutnya supaya antrean tidak berbohong.
4. Pengaturan perangkat disimpan di `AppSettings` (bukan state peramban), sehingga bertahan
   setelah aplikasi ditutup, Windows di-restart, atau aplikasi diperbarui.
5. Nilai yang tidak masuk akal ditolak server: lebar selain 58/80, nama printer yang tidak ada di
   Windows saat mode driver dipakai, mode selain `windows-driver`/`escpos-raw`, akhiran pindai
   selain `enter`, kunci pengaturan asing, dan mode `escpos-raw` yang alamat/port-nya belum lengkap
   atau belum sah.
6. Kasir hanya boleh mencetak pesanan yang ia catat sendiri; daftar printer dan riwayat cetak
   milik Owner.
7. Batas khusus jalur ESC/POS: hanya alamat loopback atau jaringan privat yang diterima
   (127.0.0.0/8, 10/8, 172.16–31.x, 192.168.x, 169.254.x, `fe80::/10`, `fc00::/7`) karena isi struk
   memuat nama dan telepon pelanggan. Nama host diperiksa sintaksnya saat menyimpan dan
   **diperiksa ulang terhadap hasil DNS saat mencetak**, supaya hostname yang kemudian menunjuk ke
   alamat publik tidak berubah menjadi kiriman data pelanggan ke internet. IP tidak lengkap
   ("192.168.1") ditolak, bukan ditafsirkan diam-diam. Satu pekerjaan dibatasi 65 536 byte dan
   diukur **sebelum** socket dibuka, jadi muatan yang terlalu besar tidak pernah menyentuh printer.
8. Pesan galat cetak ditulis dalam bahasa Indonesia dan disimpan apa adanya di riwayat pekerjaan
   cetak. Teks mentah `SocketException` dari sistem operasi (berbahasa Inggris) tidak dijadikan
   satu-satunya penjelasan: sambungan ditolak menjadi "Printer <alamat>:<port> menolak sambungan…",
   dan nama host yang tidak ter-resolution menjadi "Nama '<host>' tidak dikenali jaringan…".
9. Yang dikirim ke printer selalu **tujuan yang tersimpan di server**. Layar Perangkat Kasir
   menampilkan mode dan tujuan tersimpan, dan menambahkan catatan kuning bila pilihan di layar
   belum disimpan — supaya "sudah memilih ESC/POS" di layar tidak disalahartikan sebagai
   "struk sudah dikirim ke alamat itu".

## 5. Bukti pengujian pada build ini

| Pemeriksaan | Hasil |
|---|---|
| `dotnet build BakeryDesktop.slnx` | PASS, 0 error |
| `dotnet test BakeryDesktop.slnx` | PASS 226 tes (46 Application, 57 Domain, 68 Infrastructure, 55 UI) |
| `scripts/verify.ps1 -SkipTests` | PASS 7 langkah: build + verify-batch2 (62), batch3 (54), batch6 (21) |
| Tes renderer struk | 13 kasus: 58/80 kolom, rupiah, qty × harga, DP/sisa, LUNAS, cetak ulang, lebar baris |
| Tes layanan perangkat (adaptor palsu) | 19 kasus: sukses → `Printed`; gagal → `Failed` + pesan "Transaksi tetap tersimpan"; tanpa printer → tidak ada pekerjaan; batas 20 detik; validasi pengaturan; pemulihan pekerjaan menggantung |
| Tes migrasi 014 | 7 kasus: `PrintJobs`, kolom barcode, barcode ganda ditolak (termasuk beda huruf), produk tanpa barcode sah, rollback penuh |
| Tes API perangkat | 11 kasus: RBAC Owner/Kasir, lebar 57 ditolak 400, printer karangan 409, cetak tanpa printer 409 dengan pesanan utuh, tidak ada pekerjaan dibuat, kasir lain 404, barcode ganda 409, pengaturan bertahan |
| Tes UI peramban | 2 kasus: halaman Perangkat Kasir membaca daftar printer server dan menyimpan pilihan; kolom pindai mengisi produk + total Rp 12.500 dari katalog dan menolak kode tak dikenal |
| Alat uji fisik `scripts/pos-physical-check.ps1` | PASS pada mode lingkungan dan `-SkipPrint`: printer terbaca, host/login/pengaturan/pesanan pada DB sementara. **Perbaruan 2026-11-01:** cabang cetak sudah benar-benar ditembakkan — ke pencetak virtual Windows dan ke emulator ESC/POS loopback, termasuk cabang `-Mode Failure`; perangkat thermal fisik tetap belum (lihat §5b.2) |

Bukti mentah (log) ada di `C:\tmp\full-test3.log`, `C:\tmp\verify-batch8.log`,
`C:\tmp\ui8-tests.log`, `C:\tmp\ui8ui-tests.log`, `C:\tmp\fisik-env.log`,
`C:\tmp\fisik-skip2.log`.

## 5b. Bukti tambahan 2026-11-01 — jalur ESC/POS raw

Lingkungan: mesin uji yang sama (Windows 11 Pro 10.0.26300, sesi tanpa hak admin). Lawan bicara
printer adalah **emulator ESC/POS loopback milik sendiri**
(`tests/UI.Tests/Helpers/LoopbackEscPosEmulator.cs`): socket TCP nyata di 127.0.0.1 port acak yang
menerima, mencatat byte, dan bisa dibuat macet. Tidak ada perangkat lunak pihak ketiga yang
diunduh atau dipasang untuk bukti ini.

| Pemeriksaan | Hasil |
|---|---|
| `dotnet build` Release (seluruh solusi) | PASS, 0 peringatan, 0 galat |
| `npm run lint` (oxlint) | 0 galat; 1 peringatan di `DeviceView.jsx` adalah `set-state-in-effect` yang sudah ada sebelum perubahan ini |
| `npm run build` (bundel SPA di `frontend/dist`) | PASS, 1958 modul, `index-DhfzdJVh.js` 595,31 kB; disalin ke `desktop/` dan JS ber-hash lama dihapus |
| `dotnet test` empat proyek | PASS **489** tes, 0 gagal, 0 dilewati (Domain 83, Application 129, Infrastructure 143, UI 134; UI 3 mnt 38 dtk) — log `C:\tmp\full-test5.log` |
| `dotnet test --filter FullyQualifiedName~EscPos` | PASS 77 (Application) + 19 (UI) |
| Byte yang dikirim ke emulator | diuji per byte: `ESC @` di awal, tiap baris ASCII diakhiri `LF`, `ESC d 3`, dan `GS V 0` hanya bila potongan dihidupkan; `ESC a`/`ESC E`/`ESC t`/`ESC p` dipastikan tidak pernah muncul |
| Struk pesanan lewat ESC/POS | nama pelanggan dan "Rp 25.000" terbaca kembali dari byte yang tiba; pesanan dan status tidak berubah; cetak ulang menambah pekerjaan kedua |
| Kegagalan | port ditolak → pekerjaan `Failed` + HTTP 409, pesanan tetap utuh; muatan di atas 65 536 byte → ditolak sebelum socket dibuka (sambungan tetap 0); alamat publik (`8.8.8.8`, `100.64.0.1`), bermorfologi salah (`http://192.168.1.20`, `192.168.1`) → 400 dan pengaturan tidak berubah |
| Jalur driver | tes memastikan mode `windows-driver` tidak pernah membuka socket ke emulator |

### 5b.1 Jalan di peramban (bukan hanya tes otomatis)

Host uji dijalankan dengan **DB scratch** `C:\tmp\escpos-ui.db` di port 5177 (instalasi nyata
pemilik tidak disentuh), lawan bicarnya socket TCP nyata di `127.0.0.1:9111`
(`C:\tmp\escpos-listener.mjs`) yang mencatat byte yang tiba.

Catatan kejujuran tentang bukti mentah: berkas tangkapan `C:\tmp\escpos-listener.log` dan
`C:\tmp\escpos-diterima.bin` hasil jalan di peramban ini **terhapus** saat perkakas uji dijalankan
ulang (perintah pembersih berkas di awal run emulator), dan nama yang sama kemudian diisi ulang oleh
run berikutnya dengan isi yang berbeda. Jadi angka byte pada tabel di bawah adalah pembacaan yang
dicatat saat run berlangsung, bukan sesuatu yang bisa dibuka kembali dari berkas itu. DB scratch
`C:\tmp\escpos-ui.db` dan skrip `C:\tmp\escpos-listener.mjs` masih ada; tangkapan byte yang **utuh
dan bisa diperiksa ulang** adalah milik §5b.2 (`C:\tmp\escpos-bukti\escpos-diterima.bin`).

| Aksi di layar | yang terbaca |
|---|---|
| Ganti ke **ESC/POS langsung** | kolom **Alamat printer**, **Port**, dan centang **Potong kertas setelah struk selesai** muncul; pilihan printer Windows hilang |
| **Cetak Struk Uji** sebelum menyimpan | tombol mati dan muncul catatan "Struk uji hanya dikirim ke tujuan yang sudah tersimpan di server…" |
| Simpan `127.0.0.1:9111` + potong → cetak uji | banner "Struk uji terkirim ke tcp://127.0.0.1:9111."; riwayat menampilkan `Struk uji` / `Tercetak` / `Percobaan ke-1`; **636 byte, 18 baris** tiba di socket, kepala `1b 40`, ekor `1b 64 03 1d 56 00` |
| Simpan mode driver (printer lain) lalu muat ulang | alamat, port, dan centang ESC/POS **tetap tersimpan** — penyimpanan mode driver tidak menghapus pengaturan jalur lain |
| Buat pesanan di **Kasir Cepat** → **Cetak Struk** | **928 byte** tiba: `No. Nota ORD-20261101-0001`, `Pelanggan SINTA LIVE UI`, `Telepon 081234567890`, `1 x Rp 25.000`, `Total Rp 25.000`; `ESC @` di kepala dan `ESC d 3` + `GS V 0` di ekor |
| Matikan "printer", lalu cetak ulang | banner "Struk gagal dicetak: Printer 127.0.0.1:9111 menolak sambungan… Transaksi tetap tersimpan"; riwayat menambah baris `Gagal`; baris pesanan tetap `Rp 25.000` / `Belum Bayar` |
| Layar belum disimpan tetapi mode di layar beda | catatan kuning "Pilihan di layar ini belum disimpan. Cetak masih memakai tujuan tersimpan …" + status selalu berbunyi **"Mode tersimpan di server: …"** |

Dua cacat ditemukan dan ditutup selama jalan di peramban ini, keduanya soal kejujuran teks:
(1) catatan mode mencetak mencampur pilihan di layar dengan keadaan tersimpan, sampai berbunyi
"ESC/POS langsung ke Microsoft Print to PDF"; (2) kegagalan sambung menampilkan teks
`SocketException` berbahasa Inggris. Keduanya sekarang diuji (lihat aturan 8 dan 9 di §4).

### 5b.2 Alat uji fisik (`scripts/pos-physical-check.ps1`) ikut diperluas

Skrip yang semula hanya tahu jalur driver sekarang menerima `-CetakMode escpos-raw`
(`-EscPosHost`, `-EscPosPort`, `-EscPosPotong`). Yang berubah di dalam skrip, dan alasannya:

| Bagian | Perilaku |
|---|---|
| Validasi argumen | `windows-driver` tanpa `-PrinterName` dan `escpos-raw` tanpa `-EscPosHost` → keluar `GAGAL:` dengan kode 2, tidak sampai menyentuh host |
| Bloking lingkungan | pada ESC/POS tertulis `Driver printer: tidak dipakai (byte ESC/POS dikirim langsung ke port printer)` + `Port printer: tcp://…` + hasil `Test-NetConnection` ("Port terbuka sebelum cetak: ya/tidak"). Tidak ada `Get-Printer` sama sekali, jadi printer jaringan tanpa driver Windows tetap bisa diuji |
| Muatan pengaturan | hanya kunci milik mode aktif yang dikirim, sama seperti layar Perangkat Kasir. Mengirim `device.printer.name` saat mode ESC/POS akan ditolak server, dan mengirim host kosong saat mode driver akan menghapus alamat tersimpan |
| Daftar periksa manual | item 4 bercabang: ESC/POS tidak punya antrean Windows, jadi setiap kegagalan wajib tercatat `Gagal` dengan pesanan utuh dan struk tidak rangkap |

Tiga run nyata pada 2026-11-01, semua terhadap pengganti printer di `127.0.0.1:9111` (emulator Node
milik sendiri) dan basis data sementara — **bukan** printer fisik:

| Run | Perintah | Hasil |
|---|---|---|
| Cetak ESC/POS | `-CetakMode escpos-raw -EscPosHost 127.0.0.1 -EscPosPort 9111 -EscPosPotong -AllowPrint` | `Simpan pengaturan: HTTP 200`, pesanan uji `#1 (HTTP 201)`, kedua cetak `HTTP 200`, dua pekerjaan `status=Printed` tujuan `tcp://127.0.0.1:9111`, total `2500000 / 2500000`, **HASIL OTOMATIS: PASS**. Bukti: `%TEMP%\seruni-pos-fisik-20261101-104601\bukti.txt` dan byte utuh di `C:\tmp\escpos-bukti\escpos-diterima.bin` (**1572 byte** = struk uji 636 byte + struk pesanan 936 byte; kepala `1b 40`, ekor `1b 64 03 1d 56 00`, `No. Nota ORD-20261101-0001`, `2 x Rp 12.500`, `Total Rp 25.000` terbaca kembali) |
| Gagal ESC/POS | sama, ditambah `-Mode Failure -AllowPrint -Configuration Release`, pendengar dimatikan lebih dulu | `Port terbuka sebelum cetak: tidak`, kedua cetak `HTTP 409 PRINT_FAILED` dengan pesan bahasa toko `Printer 127.0.0.1:9111 menolak sambungan. Pastikan printer menyala dan port 9111 benar. Transaksi tetap tersimpan; periksa printer lalu cetak ulang.`, riwayat dua baris `status=Failed attempts=1` dengan `galat=` berisi pesan Indonesia yang sama (bukan `SocketException` Inggris), pesanan `2500000 / 2500000` dan status tetap, **PASS**. Bukti: `%TEMP%\seruni-pos-fisik-20261101-105531\bukti.txt` |
| Regresi driver | `-CetakMode windows-driver -PrinterName "Microsoft Print to PDF" -SkipPrint` | Tetap PASS seperti sebelum perubahan: driver/port/status Windows dan ukuran kertas tercatat, `TIDAK ADA pencetakan dilakukan`. Bukti: `%TEMP%\seruni-pos-fisik-20261101-104632\bukti.txt` |
| Regresi driver dengan cetak | `-CetakMode windows-driver -PrinterName "Microsoft Print to PDF" -PaperWidthMm 80 -AllowPrint -Configuration Release` | Setelah rombak cabang, jalur driver masih menembus spooler: kedua cetak `HTTP 200`, dua pekerjaan `status=Printed` tujuan `Microsoft Print to PDF` dengan `galat=` kosong, pesanan `2500000 / 2500000`, **PASS**. Ini **bukan** bukti kertas: pencetak PDF virtual hanya menerima pekerjaan. Bukti: `%TEMP%\seruni-pos-fisik-20261101-140257\bukti.txt` |

| Validasi argumen | `-CetakMode escpos-raw -SkipPrint` (tanpa `-EscPosHost`) dan `-CetakMode windows-driver -SkipPrint` (tanpa `-PrinterName`) | Masing-masing keluar `GAGAL: …wajib diisi…` dengan **kode 2** sebelum host apa pun dijalankan, jadi tidak ada basis data sementara dan tidak ada pekerjaan cetak yang tertinggal |

Angka 636 byte pada run ESC/POS ini sama persis dengan struk uji yang terbaca di peramban pada
§5b.1, jadi renderer-nya konsisten antara jalur API dan jalur perkakas uji.

Yang **tetap belum terbukti** setelah semua ini: keluaran kertas pada printer kasir fisik (jalur
driver maupun ESC/POS), perintah potong pada perangkat nyata, kecepatan/antrean printer sungguhan,
perilaku saat jaringan putus di tengah pengiriman, dan laci uang. Pemindai barcode fisik juga masih
belum diuji. Verdict rilis tidak berubah karena kekurangan tersebut.

## 6. Daftar yang belum diuji (jujur)

Panduan langkah demi langkah dan alat bukti untuk poin-poin di bawah ada di
[pos-uji-fisik.md](pos-uji-fisik.md) dan `scripts/pos-physical-check.ps1`. Keduanya sudah
ditembakkan terhadap pengganti printer di loopback (§5b.2) tetapi **belum sekali pun dijalankan
pada perangkat nyata**.

- Cetak fisik ke printer thermal 58/80 mm, pada **kedua** jalur (`windows-driver` dan
  `escpos-raw`), termasuk hasil potong dan kualitas huruf.
- Pemindai barcode fisik (model, akhiran Enter, kecepatan ketik).
- Laci uang, layar pelanggan, timbangan: belum ada implementasi.
- Layar sentuh pada perangkat all-in-one POS.
- Instalasi bersih di PC baru **tanpa internet**, termasuk pemasangan driver printer offline untuk
  jalur `windows-driver`. Jalur `escpos-raw` tidak butuh driver, tapi belum pernah dicoba di PC
  kasir sungguhan.
- Peningkatan versi lewat installer pada build Batch-8 ini (EXE/MSI belum dipaket ulang dan
  belum diuji pada build ini).
- Uji rendam berjam-jam: antrean cetak macet, pertumbuhan memori, kegagalan berulang.
- Windows versi lain (hanya Windows 11 Pro 10.0.26300 x64 yang dipakai).

## 7. Panduan singkat pemasangan offline

1. Di PC kasir, pilih salah satu jalur:
   - **Driver Windows**: pasang driver resmi printer dari media/berkas vendor (tanpa internet),
     lalu pastikan printer muncul di **Settings → Bluetooth & devices → Printers & scanners** dan
     mencetak halaman uji Windows.
   - **ESC/POS langsung**: tidak perlu driver. Cukup catat alamat IP printer dan port-nya
     (umumnya 9100) dari struk konfigurasi atau panel printer.
2. Pasang aplikasi Seruni 22 Bakery (installer) di PC yang sama. Aplikasi berjalan lokal;
   tidak butuh internet.
3. Masuk sebagai Owner → menu **Perangkat Kasir**.
4. Di kartu **Cara mencetak**, pilih mode yang dipakai:
   - **Driver Windows** → pilih printer dari daftar (daftar ini dibaca dari Windows).
   - **ESC/POS langsung** → isi **Alamat printer** dan **Port**, lalu centang
     **Potong kertas setelah struk selesai** hanya bila printer punya pemotong.
   Atur lebar 58/80 mm, aktifkan "Cetak struk otomatis" bila diinginkan, lalu
   **Simpan Pengaturan**.
5. Tekan **Cetak Struk Uji**; periksa kertas. Tombol ini hanya aktif bila tujuan sudah tersimpan,
   dan yang dipakai selalu tujuan tersimpan di server — bukan isi kolom yang belum disimpan.
   Bila gagal, pesan galat ditampilkan apa adanya — aplikasi tidak memasang driver atau mengubah
   pengaturan Windows sendiri.
6. Uji pemindai: buka **Daftar Pesanan & Penjualan → Tambah Transaksi**, fokuskan kolom
   "Pindai Barcode Produk", pindai produk yang barcode-nya sudah diisi di katalog. Produk dan
   total harus terisi otomatis.
7. Cadangkan berkas basis data secara berkala; pengaturan perangkat ikut tersimpan di dalamnya.

## 8. Langkah berikutnya (butuh perangkat fisik)

Ikuti [pos-uji-fisik.md](pos-uji-fisik.md). Urutan yang disarankan, satu per satu, tanpa
melompat ke laci uang/layar pelanggan:

1. Cetak struk fisik pada satu model printer thermal 80 mm, **masing-masing dua kali**: sekali
   lewat driver resminya (`windows-driver`) dan sekali lewat ESC/POS langsung ke IP printer
   (`escpos-raw`); catat model, driver, firmware, dan versi Windows.
2. Uji kertas habis, printer dimatikan, dan antrean macet di kedua jalur; pastikan pesanan tetap
   ada dan pesan galat muncul.
3. Uji pemindai fisik dan akhiran Enter-nya.
4. Baru setelah itu: laci uang lewat port DK printer, lalu layar pelanggan dan timbangan sesuai
   model yang tersedia.

Sampai langkah-langkah itu selesai, aplikasi ini hanya boleh disebut **siap untuk jalur printer
Windows + pemindai keyboard wedge, dan jalur ESC/POS langsung yang sudah terbukti terhadap
emulator**, bukan "kompatibel dengan semua perangkat POS".
