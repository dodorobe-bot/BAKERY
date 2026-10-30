# Perangkat Kasir Windows — Peta Perangkat, Driver, dan Panduan Offline

Dokumen ini mencatat **apa yang benar-benar diuji** pada perangkat keras kasir, apa yang baru
disiapkan antarmukanya, dan apa yang belum pernah diuji. Tidak ada pernyataan "kompatibel
universal" di sini: yang tertulis hanya kombinasi perangkat + driver + koneksi + versi Windows
yang benar-benar diperiksa.

Tanggal pemeriksaan: 2026-10-28. Lingkungan uji: Windows 11 Pro 10.0.26300, 64-bit, .NET 8.
Tidak ada printer thermal, pemindai, laci uang, layar pelanggan, atau timbangan fisik yang
terhubung saat pengujian ini. Semua hasil perangkat keras di bawah karena itu adalah
**hasil simulasi/sisi perangkat lunak**, kecuali disebut lain.

---

## 1. Arsitektur aktual (hasil audit, bukan usulan)

Aplikasi ini **tidak memakai Tauri**. Yang berjalan adalah:

```
React (Vite)  ──HTTP loopback, bearer JWT──▶  ASP.NET Core host (.NET 8)
                                                    │
                                    PosDeviceService │ adaptor perangkat
                                                    ▼
                                    Windows print spooler / driver
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
| `PUT /api/devices/settings` | administration | simpan printer/lebar/auto-cetak/akhiran pindai |
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
| Pemotong kertas (cutter) | mengikuti driver/printer | Belum diuji | tidak ada perintah potong terpisah; tergantung model |
| Pemindai barcode USB (keyboard wedge) | kolom aktif + Enter | Teruji (simulasi ketik + Enter di peramban) | tes UI mengisi produk & total dari katalog, kode tak dikenal ditolak; **perangkat fisik belum diuji** |
| Lebar kertas 58 mm / 80 mm | 32 / 48 kolom | Teruji | tes renderer: tidak ada baris melebihi lebar, deskripsi dibungkus |
| Laci uang lewat printer | `ICashDrawerAdapter` | Antarmuka saja | belum ada implementasi & endpoint; sengaja tidak diklaim |
| Layar pelanggan (OPOS/SDK) | `ICustomerDisplayAdapter` | Antarmuka saja | idem |
| Timbangan (serial/COM/SDK) | `IScaleAdapter` | Antarmuka saja | idem |
| ESC/POS langsung (raw) | mode `escpos-raw` | Ditolak server | server hanya menerima mode `windows-driver`; raw belum diverifikasi |
| EDC/kartu | — | Di luar cakupan | integrasi terpisah |

## 3. Driver dan protokol yang dipakai

- **Printer**: driver resmi Windows (spooler), dipilih dari daftar printer yang benar-benar
  terpasang. Aplikasi **tidak** memasang/mengganti driver sendiri. Kertas diatur dari ukuran
  halaman kustom (lebar 58/80 mm, tinggi mengikuti isi struk).
- **Pemindai**: mode HID keyboard wedge. Aplikasi hanya membaca karakter yang masuk ke kolom
  dan mengakhiri dengan Enter. Pemindai yang tidak mengirim Enter harus diprogram di
  perangkatnya; pemindai USB-serial/Bluetooth belum didukung.
- **Tidak dipakai**: ESC/POS raw, OPOS/JavaPOS, SDK vendor, port serial, perintah shell.

## 4. Aturan yang dijaga server

1. Pesanan tersimpan di SQLite **sebelum** pekerjaan cetak dibuat. Jalur cetak tidak pernah
   mengubah pesanan, pembayaran, atau stok.
2. Kegagalan printer (mati, kertas habis, nama printer hilang, waktu tunggu 20 detik) hanya
   menandai pekerjaan `Failed` dan mengembalikan pesan; pesanan tetap ada dan bisa dicetak ulang.
3. Pekerjaan `Pending` yang menggantung saat aplikasi berhenti ditandai `Failed` pada start
   berikutnya supaya antrean tidak berbohong.
4. Pengaturan perangkat disimpan di `AppSettings` (bukan state peramban), sehingga bertahan
   setelah aplikasi ditutup, Windows di-restart, atau aplikasi diperbarui.
5. Nilai yang tidak masuk akal ditolak server: lebar selain 58/80, printer yang tidak terpasang,
   mode selain `windows-driver`, akhiran pindai selain `enter`, kunci pengaturan asing.
6. Kasir hanya boleh mencetak pesanan yang ia catat sendiri; daftar printer dan riwayat cetak
   milik Owner.

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
| Alat uji fisik `scripts/pos-physical-check.ps1` | PASS pada mode lingkungan dan `-SkipPrint`: printer terbaca, host/login/pengaturan/pesanan pada DB sementara. **Cabang cetak belum dijalankan** karena tidak ada printer fisik |

Bukti mentah (log) ada di `C:\tmp\full-test3.log`, `C:\tmp\verify-batch8.log`,
`C:\tmp\ui8-tests.log`, `C:\tmp\ui8ui-tests.log`, `C:\tmp\fisik-env.log`,
`C:\tmp\fisik-skip2.log`.

## 6. Daftar yang belum diuji (jujur)

Panduan langkah demi langkah dan alat bukti untuk poin-poin di bawah ada di
[pos-uji-fisik.md](pos-uji-fisik.md) dan `scripts/pos-physical-check.ps1`; keduanya belum
dijalankan pada perangkat nyata.

- Cetak fisik ke printer thermal 58/80 mm, termasuk hasil potong dan kualitas huruf.
- Pemindai barcode fisik (model, akhiran Enter, kecepatan ketik).
- Laci uang, layar pelanggan, timbangan: belum ada implementasi.
- Layar sentuh pada perangkat all-in-one POS.
- Instalasi bersih di PC baru **tanpa internet**, termasuk pemasangan driver printer offline.
- Peningkatan versi lewat installer pada build Batch-8 ini (EXE/MSI belum dipaket ulang dan
  belum diuji pada build ini).
- Uji rendam berjam-jam: antrean cetak macet, pertumbuhan memori, kegagalan berulang.
- Windows versi lain (hanya Windows 11 Pro 10.0.26300 x64 yang dipakai).

## 7. Panduan singkat pemasangan offline

1. Di PC kasir: pasang driver printer resmi dari media/berkas vendor (tanpa internet), lalu
   pastikan printer muncul di **Settings → Bluetooth & devices → Printers & scanners** dan
   mencetak halaman uji Windows.
2. Pasang aplikasi Seruni 22 Bakery (installer) di PC yang sama. Aplikasi berjalan lokal;
   tidak butuh internet.
3. Masuk sebagai Owner → menu **Perangkat Kasir**.
4. Pilih printer dari daftar (daftar ini dibaca dari Windows), pilih lebar 58/80 mm, aktifkan
   "Cetak struk otomatis" bila diinginkan, lalu **Simpan Pengaturan**.
5. Tekan **Cetak Struk Uji**; periksa kertas. Bila gagal, pesan galat ditampilkan apa adanya —
   aplikasi tidak memasang driver atau mengubah pengaturan Windows sendiri.
6. Uji pemindai: buka **Daftar Pesanan & Penjualan → Tambah Transaksi**, fokuskan kolom
   "Pindai Barcode Produk", pindai produk yang barcode-nya sudah diisi di katalog. Produk dan
   total harus terisi otomatis.
7. Cadangkan berkas basis data secara berkala; pengaturan perangkat ikut tersimpan di dalamnya.

## 8. Langkah berikutnya (butuh perangkat fisik)

Ikuti [pos-uji-fisik.md](pos-uji-fisik.md). Urutan yang disarankan, satu per satu, tanpa
melompat ke laci uang/layar pelanggan:

1. Cetak struk fisik pada satu model printer thermal 80 mm + driver resminya; catat model,
   driver, dan versi Windows.
2. Uji kertas habis, printer dimatikan, dan antrean macet; pastikan pesanan tetap ada dan
   pesan galat muncul.
3. Uji pemindai fisik dan akhiran Enter-nya.
4. Baru setelah itu: laci uang lewat port DK printer, lalu layar pelanggan dan timbangan sesuai
   model yang tersedia.

Sampai langkah-langkah itu selesai, aplikasi ini hanya boleh disebut **siap untuk jalur printer
Windows + pemindai keyboard wedge**, bukan "kompatibel dengan semua perangkat POS".
