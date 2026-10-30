# Bukti uji pasang/copot EXE 1.2.7 di mesin pemilik

Dijalankan 2026-10-31 03:17–03:20 (+07:00) di mesin pemilik yang sudah berisi instalasi MSI 1.2.7.
Instalasi MSI di `C:\Program Files\Seruni 22 Bakery` **tidak** disentuh: EXE dipasang per-pengguna
(`PrivilegesRequired=lowest`) dengan folder override `/DIR=C:\tmp\seruni-exe-127`, bukan folder
bawaannya, agar tidak bertumpuk dengan instalasi MSI.

Alur: potret keadaan → pasang senyap EXE → periksa isi folder, pintasan, ARP → jalankan host dari
folder hasil pasang pada basis data scratch `127.0.0.1:5312` → copot senyap → periksa sisa dan
keadaan MSI/pintasan/basis data nyata. Ditambah satu siklus terpisah yang membandingkan seluruh isi
folder hasil pasang dengan `dist\publish-single`.

Peluncur `Seruni22Bakery.exe` **tidak pernah dibuka** — di mesin ini peluncur langsung membaca basis
data pemilik. Bukti "aplikasi hasil pasang berjalan" memakai `Seruni22Bakery.Host.exe` dari folder
hasil pasang dengan basis data dan kunci JWT scratch di `C:\tmp\exe-127\data`.

## Berkas

| Berkas | Isi |
|---|---|
| `status.txt` | 43 baris siklus utama, diakhiri `rekap | PASS | lulus=42 gagal=0` |
| `status-hash.txt` | siklus pembanding payload: 393/393 berkas identik SHA-256/ukuran dengan `dist\publish-single`, lalu copot bersih |
| `exe-install.log` | log Inno `/LOG`; "Installation process succeeded."; tidak ada entri `[Run]` yang dijalankan pada pasang senyap |
| `exe-uninstall.log` | log Inno uninstaller; "Running Exec filename: taskkill /f /im Seruni22Bakery.Host.exe" lalu `.../Seruni22Bakery.exe`, "Uninstallation process succeeded." |
| `exe-install-hash.log`, `exe-uninstall-hash.log` | log Inno untuk siklus pembanding |
| `host-exe.log`, `host-exe-err.log` | keluaran host dari folder hasil pasang (stderr kosong) |
| `arp-sebelum.json`, `arp-sesudah-pasang.json`, `arp-sesudah-copot.json` | isi entri daftar program bertanda Seruni di HKLM/HKCU pada tiap tahap |
| `uji-penuh.ps1` | skrip siklus utama (pasang → host → copot) |
| `uji-hash.ps1` | skrip siklus pembanding payload |
| `pra-uji.ps1` | skrip potret keadaan sebelum uji |
| `run1/` | percobaan pertama (satu FAIL urutan periksa-diri, lihat catatan kejujuran) |
| `status-hash-run1.txt` | percobaan pembanding pertama (satu FAIL salah hitung berkas tambahan) |

## Ringkasan hasil

- EXE utuh: 42.641.033 bita, SHA-256 `adb779c1d6c4ceb1c424ae3700b6f435e6223ae372104827d3ef4b9a943cd36b`, sama dengan `SHA256SUMS.txt`.
- Keadaan awal: 0 proses Seruni, satu entri ARP (MSI 1.2.7), pintasan MSI di ProgramData Start Menu dan Public Desktop menunjuk `C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.exe`, tidak ada grup/pintasan per-pengguna.
- Pasang `/VERYSILENT /DIR=...`: exit 0; 397 berkas (393 payload + `app.ico` + `unins000.exe` + `unins000.dat` + `Uninstall Seruni 22 Bakery.lnk`); peluncur 1.2.7, host 1.2.7.0; `Host.dll` `4773a367…`, `index.html` `f4a2fc04…`, `index-DppHnhYj.js` 582.985 bita, tanpa bundel 1.2.6; tidak ada PDB/`appsettings.Development.json`/basis data/`jwt.key`.
- Pintasan EXE: grup Start Menu per-pengguna `…\Start Menu\Programs\Seruni 22 Bakery\` (aplikasi + uninstaller) dan Desktop per-pengguna, keduanya menunjuk `C:\tmp\seruni-exe-127\Seruni22Bakery.exe`. Pintasan MSI tidak berubah.
- ARP saat terpasang: entri per-pengguna `{5D9D507C-B7C6-4C4B-A6EE-6786A01F909A}_is1` "Seruni 22 Bakery (Sistem Toko & Pabrik Roti)" 1.2.7 **dan** entri MSI 1.2.7 (dua entri, sesuai dua produk berbeda).
- Host dari folder hasil pasang: `/health` 200 `version=1.2.7`; SPA 200 memuat `index-DppHnhYj.js`; aset 582.985 bita; login bawaan `mustChangePassword=True`; ganti sandi http 200; login sandi baru berhasil dan gerbang padam; periksa-diri **9 pemeriksaan, 0 gagal**, skema 17; `/api/brand` anonim 3 kunci; 17 migrasi diterapkan pada basis data scratch.
- Copot `/VERYSILENT`: exit 0; log mencatat `taskkill /f /im Seruni22Bakery.Host.exe` dan `/im Seruni22Bakery.exe` (dijalankan saat 0 proses Seruni berjalan); folder, grup Start Menu, pintasan Desktop per-pengguna, dan kunci ARP per-pengguna hilang; `uji.db` dan `uji.key` di `C:\tmp\exe-127\data` tetap ada.
- Sesudah copot: ARP kembali satu entri MSI 1.2.7; pintasan ProgramData + Public Desktop tetap menunjuk instalasi MSI; instalasi MSI utuh 393 berkas, host 1.2.7.0; basis data pemilik tetap 491.520 bita, mtime 2026-10-30 16:52:56.461, tanpa `-wal`/`-shm`; 0 proses Seruni.
- Siklus pembanding: **393/393 berkas payload identik SHA-256 dan ukuran** dengan `dist\publish-single`, 0 berkas hanya di satu sisi; berkas tambahan hanya `app.ico`, `unins000.exe`, `unins000.dat`, dan `Uninstall Seruni 22 Bakery.lnk`.

## Catatan kejujuran

- Percobaan pertama (`run1/`) mencatat satu FAIL: periksa-diri dipanggil sebelum sandi bawaan diganti, sehingga server menolak lewat gerbang wajib-ganti-sandi (`http 403`, tercatat sebagai 0 pemeriksaan). Itu urutan harness, bukan cacat produk; siklus utama mengulang dengan urutan yang benar dan lulus 9/9.
- Percobaan pembanding pertama (`status-hash-run1.txt`) mencatat satu FAIL karena skrip mengharapkan tiga berkas tambahan, padahal ada empat (`app.ico` ikut). Skrip dikoreksi; paket tidak diubah.
- Folder pasang bawaan Inno (`%LOCALAPPDATA%\Programs\Seruni22Bakery`) **tidak** dipakai pada uji ini; yang dijalankan adalah override `/DIR`. Pemetaan bawaan itu diambil dari definisi `setup.iss` (`PrivilegesRequired=lowest`, `DefaultDirName={autopf}\Seruni22Bakery`), bukan dari hasil pasang.
- Peluncur tidak dibuka (data pemilik), jadi uji ini tidak membuktikan tampilan jendela aplikasi. Yang dibuktikan: berkas terpasang, pintasan, ARP, dan perilaku host hasil pasang pada basis data scratch.
- Instalasi MSI pemilik tetap menjadi satu-satunya instalasi yang tersisa; tidak ada sisa folder, pintasan, entri ARP, atau proses dari uji ini.
