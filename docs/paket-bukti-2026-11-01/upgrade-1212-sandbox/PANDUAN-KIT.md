# Kit uji pasang/peningkatan Seruni 22 Bakery 1.2.12

Kit ini menguji tujuh hal pada mesin bersih:

1. MSI 1.2.9 dipasang dan diisi data (sandi, produk, merek) lewat API.
2. Installer EXE 1.2.12 menolak berjalan selama MSI 1.2.9 terpasang.
3. MSI ditingkatkan 1.2.9 → 1.2.12; 393 berkas payload, merek, produk, sandi, dan
   skema 18 harus tetap utuh. Peningkatan ini tidak boleh menjalankan migrasi baru,
   karena 1.2.9 sudah memakai skema 18.
4. Dari biner hasil peningkatan, mode **ESC/POS raw** diuji ke emulator loopback:
   byte struk diperiksa satu per satu (awalan `ESC @`, nominal contoh `Rp 30.000`,
   akhiran `ESC d 3`, tanpa `GS V` saat pemotongan mati), dan pengaturan perangkat
   harus bertahan setelah host di-restart.
5. MSI 1.2.9 menolak dipasang di atas 1.2.12 (pagar turun versi).
6. MSI dicopot, EXE 1.2.12 dipasang per pengguna dan dijalankan pada basis data baru;
   EXE 1.2.9 menolak menimpa versi lebih baru, dan MSI 1.2.12 menolak selama EXE ada.
7. Copot EXE tidak menghapus folder data pengguna.

Seluruh pengujian berjalan di dalam **Windows Sandbox** yang tanpa jaringan. Mesin
yang menjalankannya tidak dipasangi aplikasi, tidak membuka basis data pemilik, dan
tidak diubah selain folder kit ini.

## Syarat

- Windows 10/11 Pro, Enterprise, atau Education dengan fitur **Windows Sandbox**
  aktif. Windows Home tidak mendukung.
- RAM host minimal 4 GB dengan sekitar 2 GB bebas; 8 GB lebih aman. Tamu sandbox
  memakai 1,5 GB. Pada mesin 3,8 GB, tutup peramban dan aplikasi besar lain dulu:
  percobaan sandbox sebelumnya gagal boot karena memori virtual habis, bukan karena
  paketnya bermasalah.
- Ruang disk kosong sekitar 1 GB (kit 186 MB + bukti).
- Folder kit harus berada di disk lokal, bukan share jaringan/UNC.
- Tidak perlu hak administrator. Tidak perlu memasang .NET.

Cara mengaktifkan Sandbox bila belum aktif (butuh administrator, sekali saja):
Control Panel > Programs > Turn Windows features on or off > centang **Windows
Sandbox** > restart.

## Cara menjalankan

1. Salin seluruh folder kit ini ke mesin uji, misalnya `D:\kit-uji-1212`.
2. Buka PowerShell di folder itu.
3. Periksa kit tanpa menjalankan uji (opsional, beberapa detik):

   ```
   powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1 -Periksa
   ```

   Hasil yang benar: "Checksum kit: cocok semua" dan daftar prasyarat, tanpa jendela
   Sandbox.

4. Jalankan uji:

   ```
   powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1
   ```

5. Jendela Windows Sandbox akan terbuka dan menutup sendiri setelah selesai.
   Perkiraan waktu 20-40 menit. Jangan tutup jendela sandbox lebih awal.
6. Hasil muncul di layar dan tersimpan di `bukti\status.txt`. Baris terakhir harus
   berisi `| rekap | PASS |` dengan total pemeriksaan minimal 80, misalnya
   `lulus=88 gagal=0 total=88`. Rekap dengan lulus belasan berarti run terpotong dan
   dihitung FAIL oleh skrip.

Jangan menjalankan `uji-1212.ps1` langsung di PowerShell mesin host. Skrip itu
memanggil `shutdown.exe` dan menulis penanda ke folder data pengguna; skrip menolak
berjalan bila bukan di dalam Sandbox, tetapi tetap jangan dicoba.

## Yang dikirim balik

Kirim seluruh folder `bukti\` beserta `runner.log` dari mesin uji. Isinya:

- `status.txt` — satu baris per pemeriksaan, berakhir dengan rekap PASS/FAIL.
- `validasi.log` — transkrip lengkap.
- `msi-*.log`, `exe-*.log`, `host-*.log` — log mentah tiap tahap.
- `escpos-diterima.bin` — byte mentah yang diterima emulator printer.
- `runner.log` — catatan peluncur di sisi host.

## Bila gagal

- `runner.log` menyebut "sandbox diluncurkan" tetapi `bukti\status.txt` tidak ada:
  Sandbox tidak selesai boot. Periksa apakah RAM host mencukupi dan Hyper-V aktif.
- `status.txt` berisi FAIL: kirim apa adanya. Jangan mengubah atau menghapus baris.
  Satu FAIL sudah cukup untuk menahan putusan rilis 1.2.12.
- Jangan menilai keberhasilan dari "tidak ada pesan galat". Hanya baris rekap PASS
  yang dihitung lulus.

## Isi kit

| Berkas | Fungsi |
|---|---|
| `Seruni22Bakery_v1.2.9_x64.msi` | titik awal peningkatan |
| `Seruni22Bakery_v1.2.12_x64.msi` | paket yang diuji |
| `Seruni22Bakery_Setup_v1.2.9.exe` | uji kanal EXE lama |
| `Seruni22Bakery_Setup_v1.2.12.exe` | paket EXE yang diuji |
| `hash-publish-1212.txt` | manifes 393 berkas payload 1.2.12 |
| `uji-1212.ps1` | skrip tamu, 13 bagian pemeriksaan |
| `run-sandbox.ps1` | peluncur host; membuat `uji-jalan.wsb` |
| `KIT-SHA256SUMS.txt` | checksum seluruh berkas kit |

Semua paket `NotSigned`. Bila Windows menampilkan peringatan penerbit tidak dikenal,
itu diharapkan untuk paket tanpa sertifikat dan bukan kegagalan uji.

Emulator ESC/POS di bagian 7b adalah **perangkat lunak uji di dalam tamu**, bukan
printer fisik. Lulusnya bagian itu membuktikan byte yang dikirim sesuai rancangan,
bukan bahwa printer kasir tertentu mencetaknya dengan benar.
