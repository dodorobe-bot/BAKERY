# Kit uji pasang/peningkatan Seruni 22 Bakery 1.2.9 → 1.2.13

Kit ini menguji jalur peningkatan tertua yang masih beredar di lapangan: 1.2.9 adalah
paket pilot yang dipasang di PC kasir, sedangkan yang akan dipasang sekarang adalah
1.2.13. Tujuh hal yang diperiksa pada mesin bersih:

1. MSI 1.2.9 dipasang dan diisi data (sandi, produk, merek) lewat API-nya sendiri.
2. Installer EXE 1.2.13 menolak berjalan selama MSI 1.2.9 terpasang.
3. MSI ditingkatkan 1.2.9 → 1.2.13; 393 berkas payload, merek, produk, sandi, dan
   skema 18 harus tetap utuh. Peningkatan ini **tidak boleh** menjalankan migrasi baru:
   migrasi 018 (`018_CostOfGoodsAndInventoryJournals.sql`) sudah ada di pohon sumber tag
   `v1.2.9`, jadi 1.2.9 sudah memakai skema 18. Aset web lama (`index-BksMbgv0.js`,
   `index-CoYZy02P.css` — nama ini dibaca dari tabel `File` MSI 1.2.9, bukan dari
   ingatan) harus sudah ada sebelum peningkatan dan sudah tersapu sesudahnya; itu yang
   membuat klaim "aset lama tersapu" punya pijakan.
4. Dari biner hasil peningkatan, mode **ESC/POS raw** diuji ke emulator loopback: byte
   struk diperiksa satu per satu (awalan `ESC @`, nominal contoh `Rp 30.000`, akhiran
   `ESC d 3`, tanpa `GS V` saat pemotongan mati), dan pengaturan perangkat harus
   bertahan setelah host di-restart.
5. MSI 1.2.9 menolak dipasang di atas 1.2.13 (pagar turun versi).
6. MSI dicopot, EXE 1.2.13 dipasang per pengguna dan dijalankan pada basis data baru;
   **EXE 1.2.9 yang asli** ditolak saat dipasang di atas EXE 1.2.13 — `setup.iss` tag
   `v1.2.9` sudah memuat `BandingkanVersi`/`Pagar versi`, jadi tidak perlu menaikkan
   penanda versi secara buatan — dan MSI 1.2.13 menolak selama EXE ada.
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

Jangan menjalankan `uji-129-1213.ps1` langsung di PowerShell mesin host. Skrip itu
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
  Satu FAIL sudah cukup untuk menahan putusan rilis 1.2.13 pada jalur 1.2.9.
- Jangan menilai keberhasilan dari "tidak ada pesan galat". Hanya baris rekap PASS yang
  dihitung lulus.
- Riwayat yang relevan: jalur 1.2.9 → 1.2.12 pernah diuji 1 November dan run pertamanya
  berakhir 100 lulus / 5 gagal. Kelima kegagalan itu cacat skrip uji (regex teks log,
  berkas dibaca sebelum host berhenti, lokasi pintasan Inno), bukan produk, dan run
  keduanya 105/105. Perbaikan itu sudah ikut terport ke skrip kit ini.

## Isi kit

| Berkas | Fungsi |
|---|---|
| `Seruni22Bakery_v1.2.9_x64.msi` | titik awal peningkatan (ProductCode `{7609C8A3-2349-4606-A02D-652FE121FB78}`) |
| `Seruni22Bakery_v1.2.13_x64.msi` | paket yang diuji |
| `Seruni22Bakery_Setup_v1.2.9.exe` | uji pagar turun versi kanal EXE |
| `Seruni22Bakery_Setup_v1.2.13.exe` | paket EXE yang diuji |
| `hash-publish-1213.txt` | manifes 393 berkas payload 1.2.13 |
| `uji-129-1213.ps1` | skrip tamu, 13 bagian pemeriksaan |
| `run-sandbox.ps1` | peluncur host; membuat `uji-jalan.wsb` |
| `KIT-SHA256SUMS.txt` | checksum seluruh berkas kit |

Semua paket `NotSigned`. Bila Windows menampilkan peringatan penerbit tidak dikenal, itu
diharapkan untuk paket tanpa sertifikat dan bukan kegagalan uji.

Emulator ESC/POS adalah **perangkat lunak uji di dalam tamu**, bukan printer fisik.
Lulusnya bagian itu membuktikan byte yang dikirim sesuai rancangan, bukan bahwa printer
kasir tertentu mencetaknya dengan benar.
