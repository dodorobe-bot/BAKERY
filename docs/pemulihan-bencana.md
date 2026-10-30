# Cadangan & Pemulihan Bencana — Seruni 22 Bakery

Dokumen ini untuk dua pembaca: **pemilik toko** (bagian 1–4, tanpa istilah teknis) dan
**teknisi** yang dipanggil saat ada masalah (bagian 5–9). Semua menu dan tombol ditulis
persis seperti yang terlihat di layar.

Aplikasi berjalan sepenuhnya di komputer toko. Tidak ada data yang dikirim ke internet,
tidak ada server cadangan otomatis di luar komputer ini. Karena itu **satu-satunya jalan
keluar saat komputer rusak adalah salinan cadangan yang Anda simpan sendiri di tempat lain**.

---

## 1. Apa yang dicadangkan

Setiap cadangan adalah **satu berkas** berisi seluruh data usaha: pesanan, pelanggan,
jurnal, stok, pembelian, produksi, pengaturan, dan akun pengguna. Berkasnya berekstensi
`.db` dengan nama seperti `auto-20261029-075055.db`.

Di sebelah setiap cadangan ada berkas keterangan kecil berakhiran `.json` (manifes). Isinya
mencatat waktu pembuatan, ukuran, sidik SHA-256, versi skema, jumlah pesanan, dan jumlah
entri jurnal. Jangan menghapus berkas `.json` ini: tanpa manifes, aplikasi tetap bisa
memulihkan, tetapi tidak bisa membuktikan berkas belum berubah sejak dibuat.

Lokasi bawaan:

| Isi | Letak |
|---|---|
| Basis data yang sedang dipakai | `%LOCALAPPDATA%\Seruni22Bakery\seruni22bakery.db` |
| Folder cadangan | `%LOCALAPPDATA%\Seruni22Bakery\backups\` |
| Catatan penghapusan cadangan lama | `backups\retention.log` |
| Catatan aplikasi | `%LOCALAPPDATA%\Seruni22Bakery\logs\` |

## 2. Cadangan otomatis dan manual

- **Otomatis**: sekali sehari (setiap 24 jam) selama aplikasi dipakai, diperiksa setiap
  30 menit. Basis data yang masih kosong tidak dicadangkan.
- **Manual**: buka **Pengaturan → Cadangan & penyimpanan**, tekan **Buat cadangan sekarang**.
  Setelah selesai, layar menampilkan nama berkas dan ukurannya.
- Setiap cadangan yang dibuat aplikasi **langsung diperiksa**: integritas halaman SQLite,
  foreign key, versi skema, lalu sidik SHA-256 ditulis ke manifes. Cadangan yang gagal
  diperiksa tidak dianggap cadangan sehat.

## 3. Menyimpan salinan di luar komputer (wajib)

Cadangan di folder yang sama **ikut hilang** bila cakram komputer rusak, terkena
ransomware, atau komputer dicuri. Karena itu lakukan ini secara rutin:

1. Colokkan flash disk atau hubungkan folder komputer lain.
2. Buka **Pengaturan → Cadangan & penyimpanan**.
3. Isi kolom **Salin cadangan terbaru ke folder lain (flash disk/komputer lain)**, misalnya
   `D:\Cadangan Seruni`, lalu jalankan penyalinan.
4. Aplikasi menyalin berkas cadangan terbaru beserta manifesnya dan menampilkan sidik
   SHA-256 hasil salinan. Sidik harus sama dengan yang tercatat.

Saran irama: **setiap hari kerja** salin ke flash disk yang bergantian (dua flash disk),
dan **sekali seminggu** simpan satu salinan di tempat lain (rumah pemilik, brankas).
Minimal tiga salinan di dua tempat berbeda.

Folder tujuan harus berupa path lengkap dengan huruf drive. Path relatif, `..`, dan
folder cadangan aplikasi sendiri ditolak — supaya tidak ada yang "menyalin ke dirinya
sendiri" lalu merasa aman.

## 4. Kalau terjadi masalah (panduan cepat pemilik)

| Kejadian | Yang dilakukan |
|---|---|
| Aplikasi lambat atau menampilkan "Ruang penyimpanan perlu perhatian" | Buka **Pengaturan → Cadangan & penyimpanan**, lihat sisa ruang, pindahkan salinan lama ke flash disk, dan hapus berkas lain di komputer bila cakram hampir penuh. |
| Aplikasi menampilkan "Ruang penyimpanan kritis" | Segera salin cadangan terbaru ke luar komputer, lalu hubungi teknisi. Jangan menunda sampai cakram penuh. |
| Cadangan otomatis gagal (muncul di **Pemberitahuan**) | Tekan **Buat cadangan sekarang**. Bila tetap gagal, catat pesan galatnya dan hubungi teknisi. |
| Layar **Periksa Basis Data** melaporkan masalah | Jangan mengetik apa pun ke berkas basis data. Salin cadangan terbaru ke luar komputer lebih dulu, lalu hubungi teknisi. |
| Komputer mati total / hilang | Pasang aplikasi di komputer pengganti, lalu jalankan pemulihan dari salinan di flash disk (bagian 6). Tanpa salinan di luar komputer, data tidak bisa dikembalikan. |

**Jangan pernah menghapus atau mengganti nama berkas `seruni22bakery.db`** sebagai cara
"memperbaiki" aplikasi. Berkas itu berisi seluruh riwayat usaha. Semua tindakan perbaikan
dimulai dari cadangan, bukan dari menghapus data.

---

## 5. Untuk teknisi: aturan dasar

1. Aplikasi harus **tertutup** sebelum memulihkan. Perintah pemulihan menolak berjalan bila
   masih ada `Seruni22Bakery.Host` lain yang hidup (kode keluar `3`).
2. Sebelum menimpa apa pun, pemulihan **selalu** membuat salinan pengaman
   (`salinan-pengaman-*.db`) dari basis data yang sedang ada. Jangan hapus berkas itu sampai
   data hasil pemulihan terbukti benar.
3. Pemulihan **tidak** dijalankan dari layar aplikasi. Berkas basis data sedang dibuka oleh
   proses host, jadi pemulihan berjalan lewat baris perintah saat aplikasi tertutup.
4. Jangan menyunting berkas `.db` dengan alat SQLite lain saat aplikasi hidup.

## 6. Memulihkan dari cadangan (langkah per langkah)

1. Tutup aplikasi Seruni 22 Bakery sepenuhnya. Periksa Task Manager: tidak boleh ada
   `Seruni22Bakery.Host` atau `Seruni22Bakery.exe`.
2. Buka Command Prompt di folder aplikasi terpasang (bawaan:
   `C:\Program Files\Seruni 22 Bakery\`).
3. Tentukan berkas cadangan. Pilih yang **terbaru dan bertanda terverifikasi** di layar
   **Pengaturan → Cadangan & penyimpanan**. Untuk bencana (cakram rusak), berkasnya berasal
   dari flash disk.
4. Jalankan:

   ```
   Seruni22Bakery.Host.exe --restore "D:\Cadangan Seruni\auto-20261029-075055.db"
   ```

5. Perhatikan keluarannya. Yang diperiksa berurutan sebelum dan sesudah menimpa:
   - ukuran berkas sama dengan manifes;
   - sidik SHA-256 sama dengan manifes (berkas tidak berubah setelah dibuat);
   - `PRAGMA integrity_check` = `ok`;
   - `PRAGMA foreign_key_check` kosong (tidak ada baris yang menunjuk induk hilang);
   - skema cadangan tidak lebih baru daripada aplikasi yang dipasang;
   - setelah disalin, migrasi dijalankan dan kedua pemeriksaan diulang pada hasilnya.
6. Bila ada langkah yang gagal, aplikasi mengembalikan basis data ke keadaan sebelum
   pemulihan dari salinan pengaman dan keluar dengan kode ≠ 0. **Data lama tidak hilang.**
7. Bila berhasil, jalankan aplikasi seperti biasa dan periksa tiga hal: jumlah pesanan hari
   ini, saldo kas di dashboard, dan laporan stok. Bandingkan dengan catatan terakhir yang
   dipegang pemilik.

Kode keluar perintah `--restore`:

| Kode | Arti |
|---|---|
| `0` | Pemulihan selesai dan lolos semua pemeriksaan |
| `1` | Pemulihan gagal; basis data dikembalikan ke keadaan sebelum pemulihan |
| `2` | Argumen salah atau berkas cadangan tidak ditemukan |
| `3` | Aplikasi masih berjalan; tutup dulu |

## 7. Membuat cadangan dari baris perintah (tanpa membuka aplikasi)

```
Seruni22Bakery.Host.exe --backup
```

Perintah ini membuat cadangan bertanda `manual`, menerapkan retensi, lalu mencetak path
berkasnya. Berguna saat aplikasi tidak bisa dibuka tetapi berkas basis data masih ada.
Bila `--Database:Path` tidak disebut, perintah bekerja pada lokasi bawaan; selalu sebutkan
path secara eksplisit bila basis data berada di tempat lain:

```
Seruni22Bakery.Host.exe --backup --Database:Path="D:\Data Seruni\seruni22bakery.db"
```

**Penting (1.2.7):** pakai `Seruni22Bakery.Host.exe`, bukan peluncur `Seruni22Bakery.exe`.
Peluncur hanya mengenali `--tanpa-browser` dan tidak meneruskan `--backup`/`--restore`,
sehingga perintah lewat peluncur akan menyalakan aplikasi seperti biasa tanpa membuat
cadangan atau memulihkan apa pun.

## 8. Retensi: cadangan mana yang dihapus

Kebijakan bawaan (bisa diubah lewat pengaturan):

| Pengaturan | Bawaan | Arti |
|---|---|---|
| `backup.retention.keep_all_days` | 7 | semua cadangan 7 hari terakhir disimpan |
| `backup.retention.daily_days` | 14 | satu per hari selama 14 hari |
| `backup.retention.weekly_weeks` | 8 | satu per minggu selama 8 minggu |
| `backup.retention.monthly_months` | 12 | satu per bulan selama 12 bulan |
| `backup.retention.minimum_copies` | 3 | jumlah salinan tidak pernah kurang dari ini |
| `backup.auto.interval_hours` | 24 | jarak cadangan otomatis |
| `backup.max_age_hours` | 48 | umur cadangan terverifikasi sebelum dianggap terlambat |

Tiga hal yang **tidak pernah** dihapus oleh retensi:

1. salinan sehat paling baru, walau umurnya melewati semua jendela retensi (misalnya toko
   tutup sebulan);
2. cadangan pra-upgrade dan pra-restore — satu-satunya jalan kembali ke skema lama;
3. salinan sampai batas minimum terpenuhi.

Setiap penghapusan ditulis ke `backups\retention.log` beserta alasannya.

## 9. Monitoring kapasitas dan batasnya

Layar **Pengaturan → Cadangan & penyimpanan** menilai tiga tingkat: **normal**,
**perhatian**, **kritis**. Penilaian memakai ukuran berkas yang sebenarnya (basis data,
WAL, cadangan, log) dan sisa ruang cakram. Ambang bawaan: basis data > 1 GB (perhatian) dan
> 4 GB (kritis); sisa ruang < 2 GB (perhatian) dan < 512 MB (kritis); cadangan terverifikasi
terakhir lebih tua dari 48 jam. Nilai ini bisa diubah di pengaturan
(`storage.db_warn_mb`, `storage.db_critical_mb`, `storage.free_warn_mb`,
`storage.free_critical_mb`, `backup.max_age_hours`).

Peringatan kapasitas hanya muncul di **Pemberitahuan** di dalam aplikasi. Tidak ada email,
WhatsApp, atau pesan keluar — sesuai prinsip offline.

Batas yang perlu diketahui teknisi:

- Seluruh berkas cadangan berada dalam satu folder. Bila folder itu berada di cakram yang
  sama dengan basis data, keduanya bisa hilang bersamaan. Karena itu salinan ke luar
  komputer bersifat wajib, bukan tambahan.
- Berkas cadangan **tidak dienkripsi**. Simpan flash disk di tempat terkunci; jangan
  menaruh salinan di folder berbagi publik atau layanan awan tanpa persetujuan pemilik.
- Folder `%LOCALAPPDATA%` dilindungi ACL pengguna Windows yang berjalan. Akun lain di
  komputer yang sama tidak bisa membaca basis data tanpa hak administrator.
- Aplikasi belum ditandatangani secara digital (belum ada sertifikat). Windows SmartScreen
  akan menampilkan peringatan saat pemasangan; itu bukan tanda kerusakan.
