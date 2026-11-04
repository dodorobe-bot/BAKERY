**Status rilis: NOT READY.** Rilis ini menandai sumber paket 1.2.13, bukan persetujuan produksi.

## Kenapa mengambil 1.2.13, bukan 1.2.12

Paket 1.2.12 dibangun **2026-11-01**, jadi ia **tidak memuat** perbaikan 2–3 November. 1.2.13 adalah paket pertama yang memuat perbaikan itu. Skema basis data tetap **18** — tidak ada migrasi baru. Versi **1.2.11 tidak pernah diterbitkan**.

## Yang berubah

- **Keamanan jaringan.** Host menolak alamat dengar di luar loopback, baik dari `--urls` maupun `Kestrel:Endpoints`, dan penolakan terjadi **sebelum** kunci JWT serta basis data dibuat — salah ketik konfigurasi tidak lagi membuka port ke jaringan toko dan tidak meninggalkan berkas. Akses LAN hanya atas izin eksplisit `SERUNI_ALLOW_LAN=1`.
- **Konsistensi data.** Nilai yang menempel pada stok produk jadi kini sama dengan nilai yang dipakai HPP (sebelumnya akun 1-006 bisa bersaldo negatif begitu barang terjual); SPK tanpa unit baik membebani gaji/operasional alih-alih menguap ke aset; sisa pembulatan harga rata-rata dibukukan ke beban selisih persediaan.
- **Backup.** Retensi mengutamakan salinan yang **sudah terverifikasi**, sehingga cadangan rusak yang kebetulan terbaru tidak menghabiskan jatah periode sementara salinan sehat terhapus.
- **Idempotensi.** Kunci yang tertinggal karena proses mati di tengah jalan kedaluwarsa setelah 30 menit dan baris selesai dibersihkan setelah 30 hari — pengguna tidak lagi terkena HTTP 409 terus-menerus.
- **Antar muka.** Status perangkat dipakai ulang 15 detik dan dibersihkan saat pengaturan diubah; daftar piutang/utang dimuat bertahap dengan penanda "hanya baris yang termuat" alih-alih berhenti diam-diam pada 100 baris.
- **Otomatisasi.** Laporan gerbang JSON mencatat commit, status pohon, opsi, dan hasil tiap langkah; bukti kegagalan disimpan sebelum folder temp dibersihkan; validator paket memilih versi dari laporan build (sebelumnya mengikuti abjad); penjaga "versi terbit" kini terbukti tanpa memerlukan tag.

## Bukti yang sudah dijalankan (2026-11-03)

- Gerbang verifikasi pada konfigurasi **Release**: **32 langkah PASS, 0 gagal, 0 SKIP** — **523 tes otomatis** lulus dan **169 pemeriksaan API** (batch2 63, batch3 56, batch6 21, bug09 29) tanpa kegagalan.
- `scripts/build-pro.ps1`: 12 langkah 0 gagal; validasi paket **19 pemeriksaan, 0 gagal, 0 lewati**; payload **393/393 berkas** cocok manifes; kontrol negatif otomatisasi **14/14 lulus, 0 SKIP**.
- **Pasang / peningkatan / copot di Windows Sandbox bersih** (Windows 11 Enterprise build 26100, tanpa jaringan, tanpa .NET runtime bersama): **105 pemeriksaan lulus, 0 gagal** pada jalur **1.2.12 → 1.2.13** — `WIX_UPGRADE_DETECTED` + `RemoveExistingProducts`, tepat satu entri di Daftar Program, bundel frontend lama tersapu, sandi/produk/merek/logo pengguna bertahan, 0 migrasi baru di atas skema 18, dan copot **tidak** menghapus data pengguna.
- Pagar penolakan terbukti: turun versi ditolak (msiexec 1603), EXE menolak dipasang di atas MSI dan sebaliknya, pemasangan versi lebih lama di atas 1.2.13 ditolak.
- Cetak **ESC/POS raw dari biner hasil peningkatan**: 635 bita diterima emulator loopback, diawali `ESC @`, memuat `Rp 30.000` (bukan `Rp 300`), diakhiri `ESC d 3`, tanpa `GS V` saat potong dimatikan.

## Berkas

| Berkas | Ukuran | SHA-256 |
|---|---|---|
| `Seruni22Bakery_Setup_v1.2.13.exe` | 42.672.292 | `c1a0af29bd8f5bf9c439cadef3c1d8c891da0c49a3271384192fd682c444ed9d` |
| `Seruni22Bakery_v1.2.13_x64.msi` | 50.416.132 | `eb3f40fb0fa2fe1fb7e09bee733db524a8b29fb55af367b1bd8d0df1f5afd132` |

`SHA256SUMS.txt` disertakan dan lolos `sha256sum -c` (berkas itu hanya memuat kedua installer).

**Aset tambahan — buku manual:** `Seruni22-Buku-Manual-Pengguna.pdf`, 9.793.167 bita, SHA-256 `77b5e042d9698f134bc1d99e8094292bfea5c7337d7b809a0eccac8cbcc11dac`, 107 halaman. Buku ini **edisi 1.2.13** dan terbit 3 November 2026: stempel versi dibaca dari `<Version>` di `src/Host.Service/BakeryDesktop.Host.Service.csproj` — sumber yang sama dengan penjaga versi `build-pro.ps1` — jadi kepala halaman, metadata PDF, dan judul Bab 39 mengikuti paket ini. Delta 1.2.13 sudah tertulis di dalamnya: tabel "Yang berubah pada versi 1.2.13", subbab 0.3 (penolakan alamat dengar non-loopback dan `SERUNI_ALLOW_LAN=1`), subbab 2.6 (penolakan server tampil di dalam form, kunci idempotensi **30 menit**, masa simpan 30 hari), tenaga kerja dan overhead SPK yang pindah ke akun 1-006 dengan selisih pembulatan ke 5-006, muat bertahap piutang/utang, cache status perangkat 15 detik, kunci opname per baris bahan, serta dua baris baru pada Bab 39. **Tangkap layar masih tangkapan aplikasi 1.2.12** dan ditulis apa adanya di halaman "Sumber gambar" dan di kolofon: kontrol 1.2.13 bersifat kondisional (baru muncul saat daftar panjang, saat server menolak, atau saat pengaturan perangkat disimpan) sehingga tidak akan tampak pada tangkapan data contoh — nama tombol dan letaknya ditulis persis seperti di layar. Edisi 1.2.12 (102 halaman, SHA-256 `eec028dc…`) sudah digantikan dan tidak lagi tersedia sebagai aset.

Periksa dulu sebelum menjalankan:

```
sha256sum -c SHA256SUMS.txt          # Git Bash / Linux
certutil -hashfile Seruni22Bakery_v1.2.13_x64.msi SHA256   # PowerShell/CMD
```

MSI memakai `UpgradeCode` tetap `B975F792-6F80-4D56-8A64-D274A044DCF1` (sama dengan 1.2.4–1.2.12), `ProductCode` `{CE68DB95-1278-4892-B835-7F875F0DAB63}`. Kedua paket **NotSigned** — Windows akan menampilkan penerbit "Tidak dikenal"; integritas dibuktikan lewat SHA-256 di berita acara serah terima.

**Cara memasang:** MSI untuk seluruh mesin (`msiexec /i Seruni22Bakery_v1.2.13_x64.msi /qn /norestart`, perlu elevated); EXE untuk satu pengguna saja. Jangan pasang kedua kanal di mesin yang sama — penolakan itu disengaja dan sudah diuji. Data aplikasi berada di `%LOCALAPPDATA%\Seruni22Bakery` dan **tidak dihapus** saat copot.

## Batasan yang masih terbuka (inilah alasan NOT READY)

- Cetak ke **printer thermal 80 mm** dan **printer jaringan port 9100 fisik** belum diuji. Percobaan ke pencetak virtual Windows gagal karena port `PORTPROMPT:` menuntut dialog simpan — itu **bukan** bukti bahwa printer thermal gagal maupun lulus.
- **Pemindai barcode fisik** belum diuji (yang lulus baru simulasi keyboard wedge 14/14). **Laci uang** belum diimplementasikan.
- **Windows 10** belum diuji; seluruh uji mesin bersih memakai Windows 11 build 26100.
- Peningkatan yang terbukti adalah **1.2.12 → 1.2.13**; jalur langsung **1.2.9/1.2.10 → 1.2.13** belum dijalankan.
- **Pemasangan oleh pemilik di PC kasir** dan pemakaian pada data nyata belum dilakukan.
- Sertifikat **Authenticode** belum ada, jadi paket tidak bertanda tangan.
- Tidak ada CI: seluruh gerbang bergantung satu mesin pembangunan, sehingga kegagalan bisa berasal dari memori mesin, bukan dari produk.

## Dokumen

- Rincian verifikasi paket: `docs/verifikasi-paket-1.2.13-2026-11-03.md`
- Bukti mentah uji Sandbox (log `status.txt`, log `msiexec`, byte ESC/POS): `docs/paket-bukti-2026-11-03/upgrade-1213-sandbox/`
- Kontrol negatif otomatisasi: `docs/paket-bukti-2026-11-03/kontrol-1213/`
- Panduan uji perangkat di PC kasir: `docs/pos-uji-fisik.md` dan `docs/paket-bukti-2026-11-01/uji-fisik-1212/PANDUAN-PC-KASIR.md`
- Manual pengguna edisi 1.2.13 (107 halaman, PDF; tangkapan layar dari aplikasi 1.2.12): `docs/manual/Seruni22-Buku-Manual-Pengguna.pdf`
- Pemulihan & backup: `docs/pemulihan-bencana.md`


