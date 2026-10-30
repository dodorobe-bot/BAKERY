# Bukti uji peningkatan MSI 1.2.6 → 1.2.7 di Windows Sandbox

Dijalankan 2026-10-30 18:13–18:20 (+07:00) di Windows Sandbox: Windows 11 Enterprise
build 26100, tanpa jaringan (`<Networking>Disable</Networking>`), RAM VM 1536 MB,
pengguna `WDAGUtilityAccount` (elevated). Sandbox dipakai karena tidak ada mesin
fisik/VM terpisah yang tersambung ke mesin ini; sesi sandbox baru dibuang setiap kali
ditutup, jadi keadaan awalnya memang bersih.

Alur yang diuji dalam satu sesi: pasang 1.2.6 → isi data lewat API (ganti sandi,
buat produk, setel merek) → peningkatan senyap ke 1.2.7 → periksa berkas, identitas
produk, data, periksa-diri, dan fitur merek → copot 1.2.7 → pastikan data di luar
folder program tetap ada.

## Berkas

| Berkas | Isi |
|---|---|
| `status.txt` | 70 baris hasil per pemeriksaan (`PASS`/`FAIL` + rincian), diakhiri `rekap | PASS | lulus=65 gagal=0` |
| `validasi.log` | transkrip PowerShell lengkap dari skrip yang sama |
| `selesai.txt` | penanda guest selesai (`selesai 2026-10-30 18:20:38`) yang ditunggu runner di host |
| `msi-126-install.log` | log `msiexec /i 1.2.6 /qn /norestart /l*v`, exit 0 |
| `msi-127-upgrade.log` | log `msiexec /i 1.2.7 /qn /norestart /l*v`; `WIX_UPGRADE_DETECTED = {EDCBBD96-…}`, `RemoveExistingProducts`, `ProductRegister(…VersionString=1.2.7)`, "Installation success or error status: 0" |
| `msi-127-uninstall.log` | log `msiexec /x {A0AEA895-…} /qn /norestart /l*v`; "Removal completed successfully", exit 0 |
| `host-126.log`, `host-127.log` | keluaran host 1.2.6 dan host 1.2.7 hasil peningkatan (basis data scratch `C:\data\uji.db`, kunci `C:\data\uji.key`) |
| `host-126-err.log`, `host-127-err.log` | stderr kedua host; keduanya kosong (0 bita) |
| `uji-upgrade.ps1` | skrip guest yang dijalankan (salinan `C:\tmp\sbx-upgrade-127\uji-upgrade.ps1`) |
| `uji.wsb` | konfigurasi sandbox yang dipakai |
| `run-sandbox-upgrade.ps1` | runner di host: meluncurkan sandbox dan menunggu `selesai.txt` (bukan kode keluar `WindowsSandbox.exe`) |
| `hash-publish.txt` | 393 baris `relpath|SHA256|SIZE` dari `dist\publish-single`, dipakai membandingkan berkas terpasang |
| `run1/status.txt`, `run1/validasi.log` | percobaan pertama (61 lulus, 2 gagal karena cacat skrip uji — lihat catatan kejujuran) |

## Ringkasan hasil

- Lingkungan terbukti bersih: `C:\Program Files\dotnet` tidak ada, tidak ada folder
  program maupun entri ARP Seruni sebelum pemasangan; kedua MSI diverifikasi utuh di
  dalam sandbox (1.2.6 50.357.095 bita `461d1c77…`, 1.2.7 50.373.479 bita `5c4c31f8…`).
- Pasang 1.2.6 `/qn`: exit 0; 393 berkas; `hostfxr.dll` + `coreclr.dll` ada; tanpa
  `appsettings.Development.json`, PDB, basis data, atau kunci; peluncur 1.2.6 / host
  1.2.6.0; `Host.dll` `c9a5b020…`; `index.html` `298108b0…`; bundel
  `index-BPNiweyi.js` + `index-Bz7F3T1f.css`; ARP 1.2.6 `{EDCBBD96-…}` satu-satunya
  entri Seruni; kedua pintasan menunjuk `C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.exe`;
  tanda tangan NotSigned.
- Host 1.2.6 menjawab `/health` 200 `version=1.2.6`, SPA 200; login bawaan
  `mustChangePassword=True`, sandi diganti lalu login sandi baru berhasil; produk uji
  dibuat (`id=1`, "Roti Uji Peningkatan", 15.000); 1.2.6 belum melayani `/api/brand`
  (http 401, endpoint baru di 1.2.7); merek disetel lewat `PUT /api/settings` dan
  terbaca kembali `brand_name=Toko Uji Sandbox`.
- Sidik basis data sebelum peningkatan dicatat dari tiga berkas:
  `uji.db=31b0f666…/4096`, `uji.db-wal=e941122d…/992952`, `uji.db-shm=af952b32…/32768`.
- Peningkatan `/qn`: exit 0; `WIX_UPGRADE_DETECTED = {EDCBBD96-732D-4854-A31B-0A36E6C43E77}`;
  `RemoveExistingProducts` berjalan; "Installation success or error status: 0";
  `ProductRegister(UpgradeCode={B975F792-…}, VersionString=1.2.7)`.
- Sesudah peningkatan: ARP hanya 1.2.7 `{A0AEA895-FC09-4312-8E24-8C9E0FEABDC8}` dan
  kunci 1.2.6 hilang; 393 berkas terpasang, **393 dibandingkan, 0 beda hash/ukuran,
  0 selisih nama** terhadap `hash-publish.txt`; peluncur 1.2.7 / host 1.2.7.0;
  `Host.dll` `4773a367…`; `index.html` `f4a2fc04…`; bundel `index-DppHnhYj.js` +
  `index-BNYyu2En.css` ada dan **bundel 1.2.6 tidak tersisa**; pintasan tetap menunjuk
  instalasi.
- Basis data tidak disentuh proses pasang: sidik `uji.db` + `-wal` + `-shm` **identik**
  dengan sebelum peningkatan.
- Host 1.2.7 hasil peningkatan menjawab `/health` 200 `version=1.2.7`, SPA 200, aset
  `index-DppHnhYj.js` 200 (582.985 bita); sandi hasil ganti di 1.2.6 masih berlaku dan
  `mustChangePassword=False`; produk dari 1.2.6 ada di daftar dan terbaca utuh
  (`id=1 name=Roti Uji Peningkatan harga=15000`); merek `Toko Uji Sandbox` masih
  tersimpan; `/api/brand` anonim menjawab tepat 3 kunci; periksa-diri basis data
  9 pemeriksaan 0 gagal dengan skema tetap 17; nama merek bisa diganti lagi
  ("Toko Uji Sandbox 127"), logo PNG bisa dipasang dan dihapus lagi.
- Copot 1.2.7 `/qn`: exit 0; folder program, kedua pintasan, dan entri ARP hilang;
  `uji.db` dan `uji.key` di `C:\data` (di luar folder program) **tetap ada**; tidak ada
  proses `Seruni22Bakery*` tersisa.

## Catatan kejujuran

- Percobaan 1 (17:54–18:00) menghasilkan 61 lulus, 2 gagal. Keduanya cacat skrip uji,
  bukan cacat paket: (a) uji membaca `/api/brand` pada 1.2.6 padahal endpoint itu baru
  ada di 1.2.7 (401), sehingga merek yang sebenarnya tersimpan terbaca kosong;
  (b) uji membaca `sellingPriceMinorUnits` dari daftar produk, padahal daftar memakai
  `sellingPrice` — nilai yang benar baru terbaca dari `GET /api/products/{id}`. Kedua
  pemeriksaan diperbaiki lalu dijalankan ulang; bukti percobaan 1 ada di `run1/`.
- Percobaan 2 (18:02) tidak menghasilkan bukti sama sekali: runner lama menganggap
  sandbox mati bila proses `WindowsSandboxServer` belum ada 5 detik setelah peluncuran,
  padahal sesi baru memunculkan proses itu setelah booting. Runner sekarang menunggu
  120 detik tanpa proses dan tanpa kemajuan `status.txt`, dan tidak pernah menimpa
  bukti saat sesi lain masih aktif.
- Percobaan 3 (18:13–18:20, hasil yang diarsipkan di folder ini) lulus 65/65.
- Sandbox memakai MSI yang sama persis dengan yang diverifikasi di mesin pemilik
  (ukuran dan SHA-256 cocok), tetapi tanpa jaringan dan tanpa driver perangkat kasir;
  uji ini tidak membuktikan apa pun soal printer thermal, pemindai barcode, atau laci uang.
