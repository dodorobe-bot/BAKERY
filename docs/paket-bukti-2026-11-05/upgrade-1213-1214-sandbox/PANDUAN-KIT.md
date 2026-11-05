# Kit uji pasang/peningkatan Seruni 22 Bakery 1.2.13 → 1.2.14

Kit ini menguji jalur peningkatan dari paket yang baru saja beredar (1.2.13) ke paket
yang akan dirilis (1.2.14). Tujuh hal yang diperiksa pada mesin bersih:

1. MSI 1.2.13 dipasang dan diisi data (sandi, produk, merek) lewat API-nya sendiri.
2. Installer EXE 1.2.14 menolak berjalan selama MSI 1.2.13 terpasang.
3. MSI ditingkatkan 1.2.13 → 1.2.14; 393 berkas payload, merek, produk, sandi, dan
   skema 18 harus tetap utuh. Peningkatan ini **tidak boleh** menjalankan migrasi baru:
   migrasi 018 (`018_CostOfGoodsAndInventoryJournals.sql`) sudah ada di 1.2.13, jadi
   skema 18 sudah terpasang sebelum peningkatan. Aset web sebelum peningkatan harus
   `index-BZfyiTsC.js` + `index-CUfTbA7B.css` dan sesudahnya `index-BmCxycbD.js` +
   `index-CUfTbA7B.css` (nama-nama ini dibaca dari tabel `File` di kedua MSI, bukan
   dari ingatan). Catatan jujur: berkas CSS tidak berganti nama antara 1.2.13 dan 1.2.14
   karena hash isi CSS memang tidak berubah, jadi bukti "aset lama tersapu" di sini
   bertumpu pada bundel JS dan pada jumlah berkas di `wwwroot\assets` yang harus tetap
   dua — bukan pada lenyapnya nama CSS.
4. Dari biner hasil peningkatan, mode **ESC/POS raw** diuji ke emulator loopback: byte
   struk diperiksa satu per satu (awalan `ESC @`, nominal contoh `Rp 30.000`, akhiran
   `ESC d 3`, tanpa `GS V` saat pemotongan mati), dan pengaturan perangkat harus
   bertahan setelah host di-restart.
5. MSI 1.2.13 menolak dipasang di atas 1.2.14 (pagar turun versi).
6. MSI dicopot, EXE 1.2.14 dipasang per pengguna dan dijalankan pada basis data baru;
   **EXE 1.2.13 yang asli** ditolak saat dipasang di atas EXE 1.2.14, dan MSI 1.2.14
   menolak selama EXE ada.
7. Copot EXE tidak menghapus folder data pengguna.

Seluruh pengujian berjalan di dalam **Windows Sandbox** yang tanpa jaringan. Mesin yang
menjalankannya tidak dipasangi aplikasi, tidak membuka basis data pemilik, dan tidak
diubah selain folder kit ini.

## Syarat

- Windows 10/11 Pro, Enterprise, atau Education dengan fitur **Windows Sandbox** aktif.
  Windows Home tidak mendukung.
- RAM host minimal 4 GB dengan sekitar 2 GB bebas; 8 GB lebih aman. Tamu sandbox memakai
  1.536 MB. Pada mesin 3,8 GB, tutup peramban dan aplikasi besar lain dulu.
- Ruang disk kosong sekitar 1 GB (kit ±186 MB + bukti).
- Folder kit harus berada di disk lokal, bukan share jaringan/UNC.
- Tidak perlu hak administrator. Tidak perlu memasang .NET.

## Cara menjalankan

1. Buka PowerShell di folder kit ini.
2. Periksa kit tanpa menjalankan uji (beberapa detik):

   ```
   powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1 -Periksa
   ```

   Hasil yang benar: "Checksum kit: cocok semua" dan daftar prasyarat, tanpa jendela
   Sandbox.

3. Jalankan uji:

   ```
   powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1
   ```

4. Jendela Windows Sandbox terbuka dan menutup sendiri setelah selesai. Perkiraan
   waktu 20–40 menit; jangan tutup jendela lebih awal.
5. Hasil muncul di layar dan tersimpan di `bukti\status.txt`. Baris terakhir harus
   berisi `| rekap | PASS |`. Rekap dengan lulus belasan berarti run terpotong dan
   dihitung FAIL oleh skrip.

Jangan menjalankan `uji-1213-1214.ps1` langsung di PowerShell mesin host. Skrip itu
memanggil `shutdown.exe` dan menulis penanda ke folder data pengguna; skrip menolak
berjalan bila bukan di dalam Sandbox, tetapi tetap jangan dicoba.

## Yang dikirim balik

Seluruh folder `bukti\` beserta `runner.log`: `status.txt` (satu baris per pemeriksaan),
`validasi.log` (transkrip penuh), `msi-*.log` / `exe-*.log` / `host-*.log` (log mentah
tiap tahap), `escpos-diterima.bin` (byte yang diterima emulator), `runner.log`.

## Bila gagal

- `runner.log` menyebut "sandbox diluncurkan" tetapi `bukti\status.txt` tidak ada:
  Sandbox tidak selesai boot. Periksa RAM host dan Hyper-V.
- `status.txt` berisi FAIL: kirim apa adanya. Jangan mengubah atau menghapus baris.
  Satu FAIL sudah cukup untuk menahan putusan rilis 1.2.14 pada jalur 1.2.13.
- Jangan menilai keberhasilan dari "tidak ada pesan galat". Hanya baris rekap PASS yang
  dihitung lulus.
- Riwayat yang relevan: harness ini adalah hasil port dari kit 1.2.9 → 1.2.13 yang
  lulus 105/105 pada 4 November. Semua konstanta sisi 1.2.14 dibaca ulang dari disk
  (SHA-256 + ukuran paket, ProductCode dan nama bundel dari tabel MSI, hash
  `Seruni22Bakery.Host.dll` dari manifes publish), bukan diketik dari ingatan; port
  menolak jalan bila satu pun jumlah occurrence tidak cocok.

## Isi kit

| Berkas | Fungsi |
|---|---|
| `Seruni22Bakery_v1.2.13_x64.msi` | titik awal peningkatan (ProductCode `{CE68DB95-1278-4892-B835-7F875F0DAB63}`) |
| `Seruni22Bakery_v1.2.14_x64.msi` | paket yang diuji (ProductCode `{75FF905B-6F22-4051-A437-5F9362F0EA2B}`) |
| `Seruni22Bakery_Setup_v1.2.13.exe` | uji pagar turun versi kanal EXE |
| `Seruni22Bakery_Setup_v1.2.14.exe` | paket EXE yang diuji |
| `hash-publish-1214.txt` | manifes 393 berkas payload 1.2.14 |
| `uji-1213-1214.ps1` | skrip tamu, 13 bagian pemeriksaan |
| `run-sandbox.ps1` | peluncur host; membuat `uji-jalan.wsb` |
| `KIT-SHA256SUMS.txt` | checksum seluruh berkas kit |

Semua paket `NotSigned`. Bila Windows menampilkan peringatan penerbit tidak dikenal, itu
diharapkan untuk paket tanpa sertifikat dan bukan kegagalan uji.

Emulator ESC/POS adalah **perangkat lunak uji di dalam tamu**, bukan printer fisik.
Lulusnya bagian itu membuktikan byte yang dikirim sesuai rancangan, bukan bahwa printer
kasir tertentu mencetaknya dengan benar.
