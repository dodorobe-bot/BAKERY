# Verifikasi Paket 1.2.4 — Bundel Performa 29 Oktober 2026

Lingkup: EXE (Inno Setup) dan MSI (WiX) 1.2.4 dibangun ulang setelah perbaikan performa perpindahan menu, lalu diuji pasang/copot pada mesin ini.
Keputusan: kedua paket **lulus pasang/copot** dan memuat bundel baru. MSI juga **lulus uji mesin bersih** (Windows Sandbox, 30/30 pemeriksaan). Paket belum ditandatangani, dan aplikasi hasil paket belum dijalankan di mesin pemilik.

## 1. Artefak

| Berkas | Ukuran | SHA-256 | Dibuat |
| --- | ---: | --- | --- |
| `Seruni22Bakery_Setup_v1.2.4.exe` | 42.629.888 bita | `763d43845285254f8c59e56817cdc0d962e6070d39961bf8b79d963729c660f2` | 2026-10-29 14:31 |
| `Seruni22Bakery_v1.2.4_x64.msi` | 50.357.095 bita | `2972b145e3fd77df2500b654d9af290421d03ce52e1fbb1416333c8ca7f23114` | 2026-10-29 14:31 |

Keduanya `NotSigned` (belum ada sertifikat). Peluncur di dalam paket melaporkan versi berkas 1.2.4.

## 2. Isi paket

- `wwwroot/index.html` di kedua paket: SHA-256 `5364f0b36969d616e0d5fc2302698d5a7c602f01edd5355fa9ccefe40f302042`, sama dengan `frontend/dist` dan `desktop/` yang diukur harness performa.
- Aset JavaScript yang dirujuk adalah `index-CKgTNzb_.js` (573.778 bita), bukan bundel lama.
- MSI: ekstraksi administratif (`msiexec /a`) menghasilkan 393 berkas yang **identik byte-per-byte** dengan `dist/publish-single`.
- EXE: setelah pasang senyap, seluruh berkas wajib yang sama ada di folder tujuan (ditambah `app.ico` dari skrip Inno); tidak ada `appsettings.Development.json` dan tidak ada PDB.

## 3. Uji EXE (Inno Setup)

Dipasang ke folder sementara `C:\tmp\seruni-exe-baru`, tanpa menjalankan aplikasi (`skipifsilent`), lalu dicopot.

| Pemeriksaan | Hasil |
| --- | --- |
| Pasang senyap | exit 0 |
| Berkas wajib + bundel baru + peluncur 1.2.4 | 23 pemeriksaan PASS, 0 FAIL |
| Aplikasi dijalankan saat pasang | 0 proses |
| Basis data asli sebelum/sesudah pasang | 4 berkas identik |
| Copot senyap | exit 0, folder uji terhapus, 0 proses tertinggal |
| Basis data asli sebelum/sesudah copot | 4 berkas identik |
| Kunci uninstall instalasi nyata | diekspor sebelum uji, dipulihkan sesudahnya; menunjuk `C:\Users\Administrator\AppData\Local\Programs\Seruni22Bakery\` versi 1.2.4 |
| Instalasi nyata per-pengguna | 397 berkas sebelum dan sesudah, tidak tersentuh |

## 4. Uji MSI (WiX, per mesin)

| Pemeriksaan | Hasil |
| --- | --- |
| Pasang `/qn` dengan token tidak meningkat | exit 1603, log: **Error 1925** "You do not have sufficient privileges ... for all users" |
| Pasang `/qn` dengan proses meningkat | exit 0 |
| Berkas terpasang | 393 berkas, bundel baru, peluncur 1.2.4, tanpa PDB/konfigurasi pengembangan |
| Entri Programs and Features | "Seruni 22 Bakery" 1.2.4 |
| Shortcut | Start Menu dan Public Desktop menunjuk `Seruni22Bakery.exe` |
| Aplikasi dijalankan saat pasang | 0 proses |
| Basis data asli sebelum/sesudah pasang | 4 berkas identik |
| Copot `/qn` | exit 0; folder Program Files, entri, dan kedua shortcut hilang; 0 proses |
| Basis data asli sebelum/sesudah copot | 4 berkas identik |
| Shortcut desktop instalasi per-pengguna | tetap ada dan masih menunjuk instalasi nyata |
| Instalasi nyata per-pengguna | 397 berkas sebelum dan sesudah, tidak tersentuh |
| Uji asap host dari payload MSI pada basis data scratch (port 5291) | `/health` 200 `{"version":"1.2.4"}`, SPA 200, aset `index-CKgTNzb_.js` 200 (573.778 bita) |

Catatan penting untuk operator: MSI memasang untuk semua pengguna, jadi pemasangan senyap **wajib dari shell yang meningkat**. Percobaan dari token biasa gagal dengan 1603/1925, bukan karena paketnya rusak.

Identitas paket: ProductCode MSI ini `{B1734125-F339-4527-AD56-CC888D911FC0}` (dibuat otomatis WiX per build; nilai `{DE2E612D-…}` milik build 10:34 dan tidak berlaku untuk paket 14:31). UpgradeCode `B975F792-6F80-4D56-8A64-D274A044DCF1` tetap antar versi.

## 5. Uji mesin bersih (Windows Sandbox, 2026-10-29 15:42–15:45)

Tidak ada mesin fisik atau VM terpisah yang tersambung ke mesin ini, jadi lingkungan bersih dibuat dengan Windows Sandbox: Windows 11 Enterprise build 26100, jaringan dimatikan, RAM VM 1536 MB, tanpa runtime .NET bersama (`C:\Program Files\dotnet` tidak ada) dan tanpa instalasi Seruni sebelumnya. Skrip validasi dijalankan dengan hak administrator (`WDAGUtilityAccount`).

| Pemeriksaan | Hasil |
| --- | --- |
| MSI di dalam sandbox utuh | 50.357.095 bita, SHA-256 `2972b145…` |
| Pasang `/qn` | exit 0; "Installation completed successfully" |
| Berkas terpasang | 393 berkas; `hostfxr.dll` + `coreclr.dll` ada; peluncur 1.2.4; host 1.2.4.0 |
| Bundel di paket | `index.html` `5364f0b3…`; `index-CKgTNzb_.js` 573.778 bita |
| Tidak ada berkas pengembangan/rahasia | tanpa `appsettings.Development.json`, PDB, basis data, `jwt.key` |
| Entri Programs and Features | "Seruni 22 Bakery" 1.2.4 |
| Shortcut | Start Menu + Public Desktop menunjuk `C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.exe` |
| Status tanda tangan | NotSigned (dilaporkan apa adanya) |
| Host dari hasil pasang (DB scratch `C:\data\uji.db`, port 5292) | `/health` 200 `version=1.2.4`; SPA 200; aset 200 (573.778 bita) |
| Migrasi basis data baru | 17 migrasi diterapkan (skema 17) |
| Copot `/qn` | exit 0; "Removal completed successfully"; folder program, kedua shortcut, dan ARP hilang |
| Data di luar folder program | `uji.db` + `uji.key` tetap ada setelah copot |
| Proses tersisa | 0 |

Rekap skrip: **30 PASS, 0 FAIL**. Bukti mentah (status per pemeriksaan, log `msiexec` pasang/copot, log host, skrip, dan `.wsb`) ada di `docs/paket-bukti-2026-10-29/clean-sandbox/`. Percobaan pertama pada hari yang sama gagal hanya pada cek ARP dan copot karena skrip uji masih memakai ProductCode build sebelumnya (exit 1605); konstanta itu diperbaiki, paket tidak diubah, lalu diulang dari sandbox baru dan lulus 30/30.

Yang **tidak** dicakup uji ini: sandbox bukan mesin fisik pemilik (tidak ada driver perangkat, tidak ada data nyata), paket tetap belum ditandatangani, dan aplikasi hasil paket belum dijalankan di mesin pemilik.

## 6. Bukti basis data tidak tersentuh

Basis data asli `%LOCALAPPDATA%\Seruni22Bakery\seruni22bakery.db` dan berkas pendampingnya memiliki SHA-256 yang sama sebelum dan sesudah seluruh uji:

- `seruni22bakery.db` `5fbcaae300ea0bab9ba89a9d053c937e91e542bb84934fb6c3e19f6ad68ed009`
- `seruni22bakery.db-wal` `bd3d4e944cea04344a925908bbb0660c67be60e31536f14b991d98fb7460e5d4`
- `seruni22bakery.db-shm` `8dc19bd00f34035b8c121a523c96f7cfa196093200a9673ffecf8dd7e1394a14`
- `jwt.key` `47a6cdd70ac0861c9a63d2a50a30d8ccc9a00ee87335843769680d3e628a5122`

Uji asap memakai basis data scratch di `C:\tmp`; tidak ada satu pun proses yang membaca basis data asli. Aplikasi terpasang **tidak dijalankan** di mesin ini karena mesin ini memegang data pemilik.

## 7. Penandatanganan Authenticode (siap, menunggu sertifikat)

Kedua artefak masih **NotSigned**. Mesin ini tidak punya sertifikat code signing: tidak ada sertifikat ber-EKU `1.3.6.1.5.5.7.3.3` di `Cert:\CurrentUser\My` maupun `Cert:\LocalMachine\My`, tidak ada berkas `.pfx/.p12`, dan tidak ada pembaca kartu pintar. `signtool.exe` 10.0.26100 (x64) tersedia.

Jalur tanda tangan sudah disiapkan di `scripts/sign-paket.ps1`:

- Mode token/HSM: `.\scripts\sign-paket.ps1 -Thumbprint <40 digit heksadesimal>` — sertifikat dicari di CurrentUser\My lalu LocalMachine\My, wajib punya private key yang bisa diakses, EKU code signing, dan belum kedaluwarsa.
- Mode berkas: `.\scripts\sign-paket.ps1 -PfxPath D:\sertifikat\seruni22.pfx` (sandi ditanyakan, tidak disimpan).
- Tanda tangan: `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256`, lalu `signtool verify /pa /v` dan `Get-AuthenticodeSignature`. Berkas yang gagal atau tidak berstatus `Valid` membuat skrip keluar 1.
- Tanpa argumen berkas, EXE dan MSI di `dist\installer` ditandatangani; `SHA256SUMS.txt` ditulis ulang karena isinya berubah; laporan per berkas (hash sebelum→sesudah, penerbit, serial, timestamp) masuk `dist\build-logs\signing-<cap>.log`.

Yang sudah diuji pada skrip: parser PowerShell 0 galat, dan uji negatif thumbprint `0000…0000` berhenti sebelum menandatangani apa pun dengan pesan "Sertifikat dengan thumbprint … tidak ditemukan" (keluar 1). Penanda tangan sungguhan belum pernah dijalankan karena sertifikatnya belum ada.

Setelah paket ditandatangani, SHA-256 EXE dan MSI berubah: `SHA256SUMS.txt` diperbarui otomatis oleh skrip, tetapi `BUILD-INFO-MSI.txt` dan hash MSI di skrip uji mesin bersih (`clean-sandbox/validasi.ps1`, `$msiSha = 2972B145…`) harus disesuaikan lalu uji sandbox dijalankan ulang.

**Keputusan 2026-10-29:** pembelian sertifikat **ditunda** sampai paket didistribusikan ke pihak lain. Selama serah terima dilakukan langsung ke mesin pemilik, integritas paket dibuktikan dengan SHA-256 resmi (`SHA256SUMS.txt`) yang dicatat di berita acara, dan paket tetap dilaporkan `NotSigned`. Azure Artifact Signing tidak bisa dipakai karena Public Trust tidak tersedia untuk organisasi Indonesia; jalur pembelian yang realistis adalah OV Certum (cloud SimplySign atau token USB).

## 8. Belum diuji

- Menjalankan aplikasi hasil paket di mesin ini (dilarang; data pemilik).
- Menjalankan aplikasi hasil paket di mesin fisik bersih milik pemilik (uji sandbox memakai DB scratch, bukan data nyata dan tanpa driver).
- Penandatanganan Authenticode (skrip siap; sertifikat belum terpasang, lihat bagian 7).
- Perangkat kasir fisik: printer thermal, pemindai barcode, laci uang.

Berkas uji sementara dihapus setelah verifikasi; angka di atas berasal dari log uji yang dijalankan 2026-10-29 14:31–14:37 dan 15:42–15:45.

## 9. Log mentah

`docs/paket-bukti-2026-10-29/` berisi:

- `seruni-exe-baru-install.log` — log Inno Setup pemasangan senyap EXE (baris akhir: "Installation process succeeded").
- `seruni-msi-baru.log` — log WiX untuk siklus pencopotan terakhir (baris akhir: "Removal completed successfully", kode 0). Pemasangan siklus yang sama juga keluar 0.
- `host-uji.log` — log host dari payload MSI pada basis data scratch.
- `clean-sandbox/` — bukti uji mesin bersih: `status.txt` (30 PASS/0 FAIL), `validasi.log`, `msi-install.log`, `msi-uninstall.log`, `host-uji.log`, `validasi.ps1`, `Seruni22-clean2.wsb`, dan `README.md`.

